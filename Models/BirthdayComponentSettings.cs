using CommunityToolkit.Mvvm.ComponentModel;

namespace BirthdayReminder.Models;

/// <summary>“生日提醒”主界面组件的设置。主界面上每个摆放的组件各自独立。</summary>
public class BirthdayComponentSettings : ObservableObject
{
    private bool _showToday = true;
    private bool _showNext = true;
    private bool _showCountdown = true;
    private bool _showMonthList;
    private bool _showAge = true;
    private int _mode;
    private double _rotateSeconds = 5;
    private string _emptyText = "近期没有生日";
    private bool _hideWhenEmpty;

    /// <summary>显示今日寿星。</summary>
    public bool ShowToday { get => _showToday; set => SetProperty(ref _showToday, value); }

    /// <summary>显示下一个生日是谁。</summary>
    public bool ShowNext { get => _showNext; set => SetProperty(ref _showNext, value); }

    /// <summary>下一个生日显示“还有 N 天”倒计时。</summary>
    public bool ShowCountdown { get => _showCountdown; set => SetProperty(ref _showCountdown, value); }

    /// <summary>显示本月剩余（尚未过去）的生日列表。</summary>
    public bool ShowMonthList { get => _showMonthList; set => SetProperty(ref _showMonthList, value); }

    public bool ShowAge { get => _showAge; set => SetProperty(ref _showAge, value); }

    /// <summary>0 = 合并显示在一行；1 = 依次轮播（组件高度有限时推荐）。</summary>
    public int Mode { get => _mode; set => SetProperty(ref _mode, value); }

    public double RotateSeconds { get => _rotateSeconds; set => SetProperty(ref _rotateSeconds, value); }
    public string EmptyText { get => _emptyText; set => SetProperty(ref _emptyText, value ?? ""); }

    /// <summary>没有任何内容时隐藏组件。</summary>
    public bool HideWhenEmpty { get => _hideWhenEmpty; set => SetProperty(ref _hideWhenEmpty, value); }
}
