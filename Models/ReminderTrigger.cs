using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BirthdayReminder.Models;

/// <summary>触发类型。</summary>
public static class TriggerKinds
{
    /// <summary>每天固定时间点（如 08:00）。</summary>
    public const int FixedTime = 0;

    /// <summary>第 N 节课下课后（进入课间时）。</summary>
    public const int AfterClass = 1;

    /// <summary>放学时。</summary>
    public const int AfterSchool = 2;
}

/// <summary>
/// 一个“提醒时间点”。可以同时配置多个，例如 08:00 + 第 2 节课下课后。
/// 每个时间点每天最多触发一次，所有命中的提醒会合并为一条。
/// </summary>
public class ReminderTrigger : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private bool _enabled = true;
    private int _kind = TriggerKinds.FixedTime;
    private string _time = "08:00";
    private int _classIndex = 2;

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public int Kind
    {
        get => _kind;
        set
        {
            if (!SetProperty(ref _kind, value)) return;
            OnPropertyChanged(nameof(IsFixedTime));
            OnPropertyChanged(nameof(IsAfterClass));
        }
    }

    /// <summary>HH:mm</summary>
    public string Time
    {
        get => _time;
        set => SetProperty(ref _time, value ?? "");
    }

    /// <summary>第几节课下课后（从 1 开始，仅统计“上课”类型的时间点）。</summary>
    public int ClassIndex
    {
        get => _classIndex;
        set => SetProperty(ref _classIndex, value);
    }

    [JsonIgnore] public bool IsFixedTime => Kind == TriggerKinds.FixedTime;
    [JsonIgnore] public bool IsAfterClass => Kind == TriggerKinds.AfterClass;

    public bool TryGetTimeOfDay(out TimeSpan time)
    {
        var raw = (Time ?? "").Trim().Replace('：', ':');
        return TimeSpan.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture, out time)
               && time >= TimeSpan.Zero && time < TimeSpan.FromDays(1);
    }
}
