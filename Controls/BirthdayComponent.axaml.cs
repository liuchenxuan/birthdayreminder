using Avalonia;
using Avalonia.Threading;
using BirthdayReminder.Models;
using BirthdayReminder.Services;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace BirthdayReminder.Controls;

/// <summary>
/// 生日组件：今日寿星 + 最近的生日（+倒计时）+ 本月剩余生日。
/// 内容较多时可切换为“轮播”模式，在固定高度的主界面上依次显示。
/// </summary>
[ComponentInfo("7C5E2B9A-4D1F-4A63-9E08-B3F6A1D27C54", "生日提醒",
    description: "显示今日寿星、下一个生日及倒计时、本月剩余生日。")]
public partial class BirthdayComponent : ComponentBase<BirthdayComponentSettings>
{
    private readonly BirthdayDataService _data;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private int _rotateIndex;
    private DateTime _lastRotate = DateTime.MinValue;
    private bool _hooked;

    public BirthdayComponent(BirthdayDataService data)
    {
        _data = data;
        InitializeComponent();
        _timer.Tick += (_, _) => Refresh();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_hooked && Settings != null)
        {
            Settings.PropertyChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
            _hooked = true;
        }

        _data.EntriesChanged += OnDataChanged;
        _timer.Start();
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        // 组件卸载时必须取消订阅，否则会造成内存泄漏
        _timer.Stop();
        _data.EntriesChanged -= OnDataChanged;
    }

    private void OnDataChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        if (Settings == null) return;
        var today = DateTime.Now.Date;
        var segments = new List<(string Icon, string Text)>();

        if (Settings.ShowToday)
        {
            var text = BirthdaySummary.Today(_data, today, Settings.ShowAge);
            if (text.Length > 0) segments.Add(("🎂", $"今日寿星：{text}"));
        }

        if (Settings.ShowNext)
        {
            var text = BirthdaySummary.Next(_data, today, Settings.ShowCountdown, Settings.ShowAge);
            if (text.Length > 0) segments.Add(("🎁", $"下一位：{text}"));
        }

        if (Settings.ShowMonthList)
        {
            var text = BirthdaySummary.RestOfMonth(_data, today, includeToday: !Settings.ShowToday);
            if (text.Length > 0) segments.Add(("📅", $"本月：{text}"));
        }

        if (segments.Count == 0)
        {
            IconText.Text = "🎂";
            MainText.Text = Settings.EmptyText;
            Root.IsVisible = !Settings.HideWhenEmpty;
            return;
        }

        Root.IsVisible = true;
        if (Settings.Mode == 1 && segments.Count > 1)
        {
            // 轮播：每隔 N 秒切换到下一段
            var now = DateTime.Now;
            if ((now - _lastRotate).TotalSeconds >= Math.Max(2, Settings.RotateSeconds))
            {
                _rotateIndex++;
                _lastRotate = now;
            }

            var seg = segments[_rotateIndex % segments.Count];
            IconText.Text = seg.Icon;
            MainText.Text = seg.Text;
        }
        else
        {
            IconText.Text = segments[0].Icon;
            MainText.Text = string.Join("　|　", segments.Select(s => s.Text));
        }
    }
}
