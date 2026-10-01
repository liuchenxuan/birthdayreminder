using BirthdayReminder.Models;

namespace BirthdayReminder.Services;

/// <summary>为主界面组件和设置页生成“今日寿星 / 下一个生日 / 本月列表”等简短文本。</summary>
public static class BirthdaySummary
{
    private static string Person(BirthdayHit h, bool withAge) =>
        withAge && h.Age != null ? $"{h.Entry.Name}（{h.Age}岁）" : h.Entry.Name;

    /// <summary>今日寿星，没有则返回空字符串。</summary>
    public static string Today(BirthdayDataService data, DateTime today, bool withAge = true)
    {
        var hits = data.GetToday(today);
        return hits.Count == 0 ? "" : string.Join("、", hits.Select(h => Person(h, withAge)));
    }

    /// <summary>下一个生日：谁、几天后。没有名单返回空字符串。</summary>
    public static string Next(BirthdayDataService data, DateTime today, bool countdown, bool withAge = true)
    {
        var next = data.GetNext(today);
        if (next == null) return "";
        var (date, people) = next.Value;
        var days = (date.Date - today.Date).Days;
        var who = string.Join("、", people.Select(p => Person(p, withAge)));
        var when = countdown ? $"还有{days}天" : BirthdayCalculator.FormatWhen(days);
        return $"{who} · {when}（{BirthdayCalculator.FormatDate(date)}）";
    }

    /// <summary>距离最近一个生日的天数，没有则为 null。</summary>
    public static int? DaysToNext(BirthdayDataService data, DateTime today)
    {
        var next = data.GetNext(today);
        return next == null ? null : (next.Value.Date.Date - today.Date).Days;
    }

    /// <summary>本月剩余生日列表（已过的不计入）。</summary>
    public static string RestOfMonth(BirthdayDataService data, DateTime today, bool includeToday, int max = 6)
    {
        var list = data.GetRestOfMonth(today, includeToday);
        if (list.Count == 0) return "";
        var shown = list.Take(max).Select(h => $"{h.Entry.Name} {h.Date.Month}/{h.Date.Day}");
        var text = string.Join("、", shown);
        return list.Count > max ? $"{text} 等{list.Count}人" : text;
    }
}
