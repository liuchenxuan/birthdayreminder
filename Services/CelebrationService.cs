using Avalonia.Threading;
using BirthdayReminder.Controls;

namespace BirthdayReminder.Services;

/// <summary>管理全屏庆祝动画窗口，同一时间只保留一个。</summary>
public class CelebrationService
{
    private CelebrationWindow? _current;

    public void Show(string title, string subtitle, int style, double seconds)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _current?.Close();
            }
            catch
            {
                // ignored
            }

            var window = new CelebrationWindow(title, subtitle, style, seconds);
            _current = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_current, window)) _current = null;
            };
            window.Show();
        });
    }

    public void Close() => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            _current?.Close();
        }
        catch
        {
            // ignored
        }
    });
}
