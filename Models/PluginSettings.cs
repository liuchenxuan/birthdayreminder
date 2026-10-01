using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BirthdayReminder.Models;

/// <summary>
/// 插件全局设置，序列化保存到插件配置目录下的 Settings.json。
/// 可用于文本模板的占位符见 <see cref="TokenHelp"/>。
/// </summary>
public class PluginSettings : ObservableObject
{
    public const string TokenHelp =
        "可用占位符：{name} 姓名　{age} 将满年龄　{date} 日期(5月12日)　{days} 剩余天数　{when} 今天/明天/3天后　" +
        "{items} 按“单人格式”拼好的人员列表　{names} 姓名列表　{shortnames} 最多 3 个姓名+“等N人”　{count} 人数";

    // ───────── 名单与时间点 ─────────
    public ObservableCollection<BirthdayEntry> Entries { get; set; } = new();

    public ObservableCollection<ReminderTrigger> Triggers { get; set; } = new()
    {
        new ReminderTrigger { Kind = TriggerKinds.FixedTime, Time = "08:00" }
    };

    // ───────── 提前提醒 ─────────
    private bool _remind0 = true;
    private bool _remind1 = true;
    private bool _remind3;
    private bool _remind7;
    private bool _remind30;
    private string _customOffsets = "";

    public bool Remind0Day { get => _remind0; set => SetProperty(ref _remind0, value); }
    public bool Remind1Day { get => _remind1; set => SetProperty(ref _remind1, value); }
    public bool Remind3Days { get => _remind3; set => SetProperty(ref _remind3, value); }
    public bool Remind7Days { get => _remind7; set => SetProperty(ref _remind7, value); }
    public bool Remind30Days { get => _remind30; set => SetProperty(ref _remind30, value); }

    /// <summary>自定义提前天数，用逗号/空格/顿号分隔，例如 “14,60”。</summary>
    public string CustomOffsets { get => _customOffsets; set => SetProperty(ref _customOffsets, value ?? ""); }

    // ───────── 提醒方式 ─────────
    private bool _enableBanner = true;
    private bool _enableCelebration = true;
    private bool _celebrationOnlyToday = true;
    private int _celebrationStyle;
    private double _celebrationSeconds = 8;
    private bool _enableSpeech = true;
    private bool _standaloneSpeech = true;
    private double _bannerMaskSeconds = 4;
    private double _bannerOverlaySeconds = 10;

    public bool EnableBanner { get => _enableBanner; set => SetProperty(ref _enableBanner, value); }
    public bool EnableCelebration { get => _enableCelebration; set => SetProperty(ref _enableCelebration, value); }
    public bool CelebrationOnlyToday { get => _celebrationOnlyToday; set => SetProperty(ref _celebrationOnlyToday, value); }

    /// <summary>0 = 彩带纷飞，1 = 气球升空，2 = 烟花绽放。</summary>
    public int CelebrationStyle { get => _celebrationStyle; set => SetProperty(ref _celebrationStyle, value); }

    public double CelebrationSeconds { get => _celebrationSeconds; set => SetProperty(ref _celebrationSeconds, value); }
    public bool EnableSpeech { get => _enableSpeech; set => SetProperty(ref _enableSpeech, value); }

    /// <summary>
    /// 使用 ClassIsland 语音服务独立播报，不依赖“提醒 → 生日提醒”里的语音开关。
    /// </summary>
    public bool StandaloneSpeech { get => _standaloneSpeech; set => SetProperty(ref _standaloneSpeech, value); }

    public double BannerMaskSeconds { get => _bannerMaskSeconds; set => SetProperty(ref _bannerMaskSeconds, value); }
    public double BannerOverlaySeconds { get => _bannerOverlaySeconds; set => SetProperty(ref _bannerOverlaySeconds, value); }

    // ───────── 防重复 / 其它 ─────────
    private bool _dedupAcrossTriggers;
    private int _catchUpMinutes = 180;
    private int _feb29Mode;

    /// <summary>
    /// 为 true 时：同一个人同一档提醒当天只提醒一次（不论由哪个时间点触发）；
    /// 为 false 时：每个时间点各自提醒一次。
    /// </summary>
    public bool DedupAcrossTriggers { get => _dedupAcrossTriggers; set => SetProperty(ref _dedupAcrossTriggers, value); }

    /// <summary>软件在提醒时间之后才启动时，在这个分钟数内仍会补发提醒。</summary>
    public int CatchUpMinutes { get => _catchUpMinutes; set => SetProperty(ref _catchUpMinutes, value); }

    /// <summary>平年如何处理 2 月 29 日生日：0 = 2 月 28 日，1 = 3 月 1 日。</summary>
    public int Feb29Mode { get => _feb29Mode; set => SetProperty(ref _feb29Mode, value); }

    // ───────── 文本模板 ─────────
    private string _maskToday = "🎂 {shortnames} 生日快乐";
    private string _maskUpcoming = "🎁 {count}位生日将至";
    private string _itemFormat = "{name}（{age}岁）";
    private string _itemFormatNoAge = "{name}";
    private string _todayLine = "今天是{items}的生日，祝你们生日快乐！";
    private string _upcomingLine = "{when}是{items}的生日（{date}）";
    private string _speechToday = "今天是{names}的生日，祝{names}生日快乐";
    private string _speechUpcoming = "{when}是{names}的生日";
    private string _celebrationTitle = "🎂 生日快乐！";
    private string _celebrationSubtitle = "{items}";

    public string MaskTodayFormat { get => _maskToday; set => SetProperty(ref _maskToday, value ?? ""); }
    public string MaskUpcomingFormat { get => _maskUpcoming; set => SetProperty(ref _maskUpcoming, value ?? ""); }
    public string ItemFormat { get => _itemFormat; set => SetProperty(ref _itemFormat, value ?? ""); }
    public string ItemFormatNoAge { get => _itemFormatNoAge; set => SetProperty(ref _itemFormatNoAge, value ?? ""); }
    public string TodayLineFormat { get => _todayLine; set => SetProperty(ref _todayLine, value ?? ""); }
    public string UpcomingLineFormat { get => _upcomingLine; set => SetProperty(ref _upcomingLine, value ?? ""); }
    public string SpeechTodayFormat { get => _speechToday; set => SetProperty(ref _speechToday, value ?? ""); }
    public string SpeechUpcomingFormat { get => _speechUpcoming; set => SetProperty(ref _speechUpcoming, value ?? ""); }
    public string CelebrationTitleFormat { get => _celebrationTitle; set => SetProperty(ref _celebrationTitle, value ?? ""); }
    public string CelebrationSubtitleFormat { get => _celebrationSubtitle; set => SetProperty(ref _celebrationSubtitle, value ?? ""); }

    /// <summary>已经提醒过的记录（防重复），形如 T|20250512|触发器ID、H|20250512|人员ID|偏移。</summary>
    public List<string> FiredKeys { get; set; } = new();

    /// <summary>当前启用的提前天数集合（升序、去重）。0 表示当天。</summary>
    public List<int> GetOffsets()
    {
        var set = new SortedSet<int>();
        if (Remind0Day) set.Add(0);
        if (Remind1Day) set.Add(1);
        if (Remind3Days) set.Add(3);
        if (Remind7Days) set.Add(7);
        if (Remind30Days) set.Add(30);

        foreach (var part in (CustomOffsets ?? "").Split(new[] { ',', '，', '、', ' ', ';', '；' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (int.TryParse(part, out var n) && n is >= 0 and <= 366) set.Add(n);
        }

        return set.ToList();
    }
}
