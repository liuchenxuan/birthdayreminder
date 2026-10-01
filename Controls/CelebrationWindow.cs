using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BirthdayReminder.Controls;

/// <summary>
/// 生日当天的全屏强调动画：透明、置顶、无边框的窗口，叠加彩带 / 气球 / 烟花粒子和一张“生日快乐”卡片。
/// 在 Windows 上窗口点击穿透，不会影响正在上课的操作；其它平台点击任意位置可关闭。
/// 全部使用代码构建，不依赖 XAML，便于独立修改动画。
/// </summary>
public class CelebrationWindow : Window
{
    public const int StyleConfetti = 0;
    public const int StyleBalloons = 1;
    public const int StyleFireworks = 2;

    private sealed class Particle
    {
        public Control Visual = null!;
        public RotateTransform? Rotate;
        public double X, Y, Vx, Vy, Gravity, Angle, AngularVelocity;
        public double Life, MaxLife; // MaxLife <= 0 表示不限寿命（靠出屏回收）
        public double SwayAmplitude, SwayPhase, BaseX;
        public bool Recycle;
        public bool Dead;
    }

    private static readonly string[] Palette =
    {
        "#FF5A5F", "#FFB400", "#00C48C", "#3D8BFF", "#B15CFF", "#FF7AB6", "#FFD93D", "#4ADEDE"
    };

    private static readonly string[] Emojis = { "🎉", "🎊", "🎂", "🎁", "✨", "🥳", "🍰", "🎈" };

    private readonly Canvas _canvas = new() { IsHitTestVisible = false };
    private readonly Border _card = new();
    private readonly List<Particle> _particles = new();
    private readonly Random _rnd = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private readonly int _style;
    private readonly double _seconds;
    private double _w, _h, _last, _nextBurst;
    private bool _seeded;

    public CelebrationWindow(string title, string subtitle, int style, double seconds)
    {
        _style = style;
        _seconds = Math.Clamp(seconds, 3, 60);

        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        Width = 1280;
        Height = 720;

        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 64,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var subtitleBlock = new TextBlock
        {
            Text = subtitle,
            FontSize = 34,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FFE08A")),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 1000,
            Margin = new Thickness(0, 12, 0, 0),
            IsVisible = !string.IsNullOrWhiteSpace(subtitle),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _card.Child = new StackPanel { Children = { titleBlock, subtitleBlock } };
        _card.Background = new SolidColorBrush(Color.Parse("#B8151B2E"));
        _card.CornerRadius = new CornerRadius(28);
        _card.Padding = new Thickness(48, 32);
        _card.MaxWidth = 1100;
        _card.HorizontalAlignment = HorizontalAlignment.Center;
        _card.VerticalAlignment = VerticalAlignment.Center;
        _card.IsHitTestVisible = false;
        _card.RenderTransformOrigin = RelativePoint.Center;
        _card.RenderTransform = new ScaleTransform(0.01, 0.01);

        Content = new Panel { Children = { _canvas, _card } };

        Opened += OnOpened;
        Closed += (_, _) => _timer.Stop();
        PointerPressed += OnPointerPressedClose;
        _timer.Tick += OnTick;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen != null)
            {
                var b = screen.Bounds;
                var scale = screen.Scaling <= 0 ? 1 : screen.Scaling;
                Position = new PixelPoint(b.X, b.Y);
                Width = b.Width / scale;
                Height = b.Height / scale;
            }
        }
        catch
        {
            // 取不到屏幕信息时沿用默认尺寸
        }

        MakeClickThrough();
        _clock.Start();
        _timer.Start();
    }

    /// <summary>Windows：给窗口加上 WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE，使其点击穿透且不抢焦点。</summary>
    private void MakeClickThrough()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (handle == IntPtr.Zero) return;
            const int GWL_EXSTYLE = -20;
            const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
            var style = GetWindowLong(handle, GWL_EXSTYLE);
            SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
            PointerPressed -= OnPointerPressedClose; // 已点击穿透，不再需要“点击关闭”
        }
        catch
        {
            // 失败则保持“点击关闭”行为
        }
    }

    private void OnPointerPressedClose(object? sender, Avalonia.Input.PointerPressedEventArgs e) => Close();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ───────────────────────── 动画循环 ─────────────────────────

    private void OnTick(object? sender, EventArgs e)
    {
        var t = _clock.Elapsed.TotalSeconds;
        var dt = Math.Min(t - _last, 0.05);
        _last = t;

        _w = Bounds.Width;
        _h = Bounds.Height;
        if (_w < 10 || _h < 10) return;

        if (!_seeded)
        {
            Seed();
            _seeded = true;
        }

        var spawning = t < _seconds * 0.75;
        if (_style == StyleFireworks && spawning && t >= _nextBurst)
        {
            Burst(_rnd.NextDouble() * _w * 0.8 + _w * 0.1, _rnd.NextDouble() * _h * 0.5 + _h * 0.08);
            _nextBurst = t + 0.45 + _rnd.NextDouble() * 0.4;
        }

        foreach (var p in _particles) Step(p, dt, t, spawning);
        foreach (var dead in _particles.Where(p => p.Dead).ToList())
        {
            _canvas.Children.Remove(dead.Visual);
            _particles.Remove(dead);
        }

        UpdateCard(t);

        // 结尾 1 秒整体淡出
        var left = _seconds - t;
        Opacity = left < 1 ? Math.Max(0, left) : 1;
        if (left <= 0) Close();
    }

    private void UpdateCard(double t)
    {
        var intro = Math.Clamp(t / 0.6, 0, 1);
        // easeOutBack
        const double c1 = 1.70158, c3 = c1 + 1;
        var eased = 1 + c3 * Math.Pow(intro - 1, 3) + c1 * Math.Pow(intro - 1, 2);
        var pulse = t > 0.6 ? 1 + 0.02 * Math.Sin(t * 3.2) : 1;
        var scale = Math.Max(0.01, eased * pulse);
        if (_card.RenderTransform is ScaleTransform st)
        {
            st.ScaleX = scale;
            st.ScaleY = scale;
        }
    }

    private void Seed()
    {
        switch (_style)
        {
            case StyleBalloons:
                for (var i = 0; i < 22; i++) AddBalloon(true);
                // 两侧礼花筒
                for (var i = 0; i < 70; i++) AddCannonConfetti(i % 2 == 0);
                break;
            case StyleFireworks:
                _nextBurst = 0;
                break;
            default:
                for (var i = 0; i < 140; i++) AddFallingConfetti(true);
                for (var i = 0; i < 18; i++) AddFallingEmoji(true);
                break;
        }
    }

    private void Step(Particle p, double dt, double t, bool spawning)
    {
        p.Life += dt;
        p.Vy += p.Gravity * dt;
        p.X += p.Vx * dt;
        p.Y += p.Vy * dt;
        p.Angle += p.AngularVelocity * dt;
        if (p.Rotate != null) p.Rotate.Angle = p.Angle;

        var x = p.SwayAmplitude > 0
            ? p.BaseX + Math.Sin(p.Life * 2 + p.SwayPhase) * p.SwayAmplitude + (p.X - p.BaseX)
            : p.X;
        Canvas.SetLeft(p.Visual, x);
        Canvas.SetTop(p.Visual, p.Y);

        if (p.MaxLife > 0)
        {
            var remain = 1 - p.Life / p.MaxLife;
            p.Visual.Opacity = Math.Clamp(remain * 1.6, 0, 1);
            if (remain <= 0) p.Dead = true;
            return;
        }

        var outside = p.Y > _h + 60 || p.Y < -120 || p.X < -120 || p.X > _w + 120;
        if (!outside) return;

        if (p.Recycle && spawning)
        {
            Respawn(p);
        }
        else
        {
            p.Dead = true;
        }
    }

    // ───────────────────────── 粒子工厂 ─────────────────────────

    private void Respawn(Particle p)
    {
        if (_style == StyleBalloons)
        {
            p.X = p.BaseX = _rnd.NextDouble() * _w;
            p.Y = _h + 40 + _rnd.NextDouble() * 120;
        }
        else
        {
            p.X = p.BaseX = _rnd.NextDouble() * _w;
            p.Y = -40 - _rnd.NextDouble() * 200;
        }

        p.Life = 0;
    }

    private IBrush RandomBrush() => new SolidColorBrush(Color.Parse(Palette[_rnd.Next(Palette.Length)]));

    private Particle Add(Control visual, double x, double y, bool rotate)
    {
        visual.IsHitTestVisible = false;
        visual.RenderTransformOrigin = RelativePoint.Center;
        var p = new Particle { Visual = visual, X = x, Y = y, BaseX = x };
        if (rotate)
        {
            p.Rotate = new RotateTransform(0);
            visual.RenderTransform = p.Rotate;
        }

        Canvas.SetLeft(visual, x);
        Canvas.SetTop(visual, y);
        _canvas.Children.Add(visual);
        _particles.Add(p);
        return p;
    }

    private void AddFallingConfetti(bool scatter)
    {
        var w = 6 + _rnd.NextDouble() * 8;
        var h = w * (1.4 + _rnd.NextDouble());
        var visual = new Border { Width = w, Height = h, Background = RandomBrush(), CornerRadius = new CornerRadius(1.5) };
        var p = Add(visual, _rnd.NextDouble() * _w, scatter ? -_rnd.NextDouble() * _h : -20, true);
        p.Vy = 110 + _rnd.NextDouble() * 220;
        p.Vx = (_rnd.NextDouble() - 0.5) * 70;
        p.Angle = _rnd.NextDouble() * 360;
        p.AngularVelocity = (_rnd.NextDouble() - 0.5) * 540;
        p.SwayAmplitude = 12 + _rnd.NextDouble() * 24;
        p.SwayPhase = _rnd.NextDouble() * 6;
        p.Recycle = true;
    }

    private void AddFallingEmoji(bool scatter)
    {
        var visual = new TextBlock
        {
            Text = Emojis[_rnd.Next(Emojis.Length)],
            FontSize = 28 + _rnd.NextDouble() * 30
        };
        var p = Add(visual, _rnd.NextDouble() * _w, scatter ? -_rnd.NextDouble() * _h : -40, true);
        p.Vy = 70 + _rnd.NextDouble() * 130;
        p.Angle = (_rnd.NextDouble() - 0.5) * 40;
        p.AngularVelocity = (_rnd.NextDouble() - 0.5) * 90;
        p.SwayAmplitude = 20 + _rnd.NextDouble() * 30;
        p.SwayPhase = _rnd.NextDouble() * 6;
        p.Recycle = true;
    }

    private void AddBalloon(bool scatter)
    {
        var visual = new TextBlock { Text = "🎈", FontSize = 44 + _rnd.NextDouble() * 40 };
        var p = Add(visual, _rnd.NextDouble() * _w, scatter ? _h + _rnd.NextDouble() * _h * 0.6 : _h + 40, false);
        p.Vy = -(80 + _rnd.NextDouble() * 150);
        p.SwayAmplitude = 18 + _rnd.NextDouble() * 26;
        p.SwayPhase = _rnd.NextDouble() * 6;
        p.Recycle = true;
    }

    private void AddCannonConfetti(bool left)
    {
        var w = 6 + _rnd.NextDouble() * 8;
        var visual = new Border { Width = w, Height = w * 1.6, Background = RandomBrush(), CornerRadius = new CornerRadius(1.5) };
        var p = Add(visual, left ? 0 : _w - 10, _h - 20, true);
        var angle = (_rnd.NextDouble() * 35 + 50) * Math.PI / 180; // 50°~85°
        var speed = 700 + _rnd.NextDouble() * 650;
        p.Vx = Math.Cos(angle) * speed * (left ? 1 : -1);
        p.Vy = -Math.Sin(angle) * speed;
        p.Gravity = 620;
        p.AngularVelocity = (_rnd.NextDouble() - 0.5) * 720;
        p.MaxLife = 3.2 + _rnd.NextDouble() * 1.5;
    }

    private void Burst(double cx, double cy)
    {
        var brush = RandomBrush();
        var second = RandomBrush();
        const int count = 38;
        for (var i = 0; i < count; i++)
        {
            var size = 4 + _rnd.NextDouble() * 4;
            var visual = new Border
            {
                Width = size, Height = size, CornerRadius = new CornerRadius(size),
                Background = i % 3 == 0 ? second : brush
            };
            var p = Add(visual, cx, cy, false);
            var angle = i * 2 * Math.PI / count + _rnd.NextDouble() * 0.2;
            var speed = 140 + _rnd.NextDouble() * 220;
            p.Vx = Math.Cos(angle) * speed;
            p.Vy = Math.Sin(angle) * speed;
            p.Gravity = 210;
            p.MaxLife = 1.2 + _rnd.NextDouble() * 0.6;
        }

        var spark = new TextBlock { Text = "✨", FontSize = 40 };
        var sp = Add(spark, cx - 20, cy - 20, false);
        sp.MaxLife = 0.8;
    }
}
