using System.Text.Json.Serialization;
using BirthdayReminder.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BirthdayReminder.Models;

/// <summary>
/// 一条生日记录。仅使用公历；出生年份可以为空（Year = 0），此时不计算年龄。
/// </summary>
public class BirthdayEntry : ObservableObject
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _name = "";
    private int _year;
    private int _month = 1;
    private int _day = 1;
    private string _note = "";
    private bool _enabled = true;
    private string _info = "";

    public string Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? "");
    }

    /// <summary>出生年份，0 表示未知。</summary>
    public int Year
    {
        get => _year;
        set
        {
            if (SetProperty(ref _year, value)) OnPropertyChanged(nameof(BirthdayText));
        }
    }

    public int Month
    {
        get => _month;
        set
        {
            if (SetProperty(ref _month, value)) OnPropertyChanged(nameof(BirthdayText));
        }
    }

    public int Day
    {
        get => _day;
        set
        {
            if (SetProperty(ref _day, value)) OnPropertyChanged(nameof(BirthdayText));
        }
    }

    public string Note
    {
        get => _note;
        set => SetProperty(ref _note, value ?? "");
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    /// <summary>用于界面编辑：yyyy-MM-dd 或 MM-dd。</summary>
    [JsonIgnore]
    public string BirthdayText
    {
        get => Year > 0 ? $"{Year:D4}-{Month:D2}-{Day:D2}" : $"{Month:D2}-{Day:D2}";
        set
        {
            if (BirthdayParser.TryParse(value, out var y, out var m, out var d))
            {
                Year = y ?? 0;
                Month = m;
                Day = d;
            }

            // 解析失败时通知界面还原为原有值
            OnPropertyChanged();
        }
    }

    /// <summary>界面上显示的附加信息（现年龄、下次生日倒计时），由 <see cref="RefreshInfo"/> 更新。</summary>
    [JsonIgnore]
    public string Info
    {
        get => _info;
        private set => SetProperty(ref _info, value);
    }

    public void RefreshInfo(DateTime today)
    {
        var next = BirthdayCalculator.NextOccurrence(this, today);
        if (next == null)
        {
            Info = "日期无效";
            return;
        }

        var days = (next.Value.Date - today.Date).TotalDays;
        var age = BirthdayCalculator.AgeOn(this, next.Value);
        var when = days == 0 ? "今天" : $"{days:0}天后";
        Info = age is { } a ? $"将满 {a} 岁 · {when}" : $"{when}（{Month}月{Day}日）";
    }
}
