using System.Globalization;
using System.Text.RegularExpressions;
using BirthdayReminder.Models;

namespace BirthdayReminder.Services;

/// <summary>一次命中的提醒：某人在 <see cref="Date"/> 过生日，距今 <see cref="Offset"/> 天。</summary>
public sealed class BirthdayHit
{
    public required BirthdayEntry Entry { get; init; }

    /// <summary>距离生日还有几天，0 = 今天。</summary>
    public required int Offset { get; init; }

    public required DateTime Date { get; init; }

    /// <summary>这次生日将满的年龄；未填写出生年份则为 null。</summary>
    public int? Age { get; init; }
}

/// <summary>公历生日的所有计算：下次生日、年龄、倒计时。每年自动循环，不涉及农历。</summary>
public static class BirthdayCalculator
{
    /// <summary>平年的 2 月 29 日是否顺延到 3 月 1 日（否则提前到 2 月 28 日）。</summary>
    public static bool Feb29OnMarch1 { get; set; }

    /// <summary>某年的生日具体是哪一天；日期无效返回 null。</summary>
    public static DateTime? Occurrence(int year, int month, int day)
    {
        if (month is < 1 or > 12 || day < 1 || day > 31) return null;
        if (month == 2 && day == 29 && !DateTime.IsLeapYear(year))
            return Feb29OnMarch1 ? new DateTime(year, 3, 1) : new DateTime(year, 2, 28);
        if (day > DateTime.DaysInMonth(year, month)) return null;
        return new DateTime(year, month, day);
    }

    /// <summary>从 <paramref name="from"/> 起（含当天）的下一次生日。</summary>
    public static DateTime? NextOccurrence(BirthdayEntry e, DateTime from)
    {
        from = from.Date;
        for (var y = from.Year; y <= from.Year + 8; y++)
        {
            var d = Occurrence(y, e.Month, e.Day);
            if (d != null && d.Value >= from) return d;
        }

        return null;
    }

    public static bool OccursOn(BirthdayEntry e, DateTime date) =>
        Occurrence(date.Year, e.Month, e.Day)?.Date == date.Date;

    /// <summary>生日当天将满的年龄。</summary>
    public static int? AgeOn(BirthdayEntry e, DateTime occurrence) =>
        e.Year > 0 ? occurrence.Year - e.Year : null;

    /// <summary>今天的实际年龄（尚未过生日则减 1）。</summary>
    public static int? CurrentAge(BirthdayEntry e, DateTime today)
    {
        if (e.Year <= 0) return null;
        var thisYear = Occurrence(today.Year, e.Month, e.Day);
        var age = today.Year - e.Year;
        if (thisYear != null && today.Date < thisYear.Value) age--;
        return Math.Max(age, 0);
    }

    public static string FormatWhen(int offset) => offset switch
    {
        0 => "今天",
        1 => "明天",
        2 => "后天",
        _ => $"{offset}天后"
    };

    public static string FormatDate(DateTime d) => $"{d.Month}月{d.Day}日";
}

/// <summary>解析 Excel/CSV/手输的生日文本，支持多种常见格式，且只接受公历。</summary>
public static class BirthdayParser
{
    private static readonly Regex FullDate = new(
        @"^\s*(\d{4})\s*[-/.年]\s*(\d{1,2})\s*[-/.月]\s*(\d{1,2})\s*日?\s*(?:[ T]\d.*)?$",
        RegexOptions.Compiled);

    private static readonly Regex Compact = new(@"^\s*(\d{4})(\d{2})(\d{2})\s*$", RegexOptions.Compiled);

    private static readonly Regex MonthDay = new(
        @"^\s*(\d{1,2})\s*[-/.月]\s*(\d{1,2})\s*日?\s*$", RegexOptions.Compiled);

    public static bool TryParse(object? raw, out int? year, out int month, out int day)
    {
        year = null;
        month = 0;
        day = 0;

        switch (raw)
        {
            case null:
                return false;
            case DateTime dt:
                return Validate(dt.Year, dt.Month, dt.Day, out year, out month, out day);
            case DateOnly d:
                return Validate(d.Year, d.Month, d.Day, out year, out month, out day);
            case double or float or decimal or int or long or short:
            {
                var number = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                if (number >= 19000101 && number <= 99991231)
                    return TryParse(((long)number).ToString(CultureInfo.InvariantCulture), out year, out month, out day);
                if (number is >= 1 and < 80000)
                {
                    var dt = DateTime.FromOADate(number);
                    return Validate(dt.Year, dt.Month, dt.Day, out year, out month, out day);
                }

                return false;
            }
        }

        var s = raw.ToString()?.Trim();
        if (string.IsNullOrEmpty(s)) return false;

        var m = FullDate.Match(s);
        if (m.Success)
            return Validate(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                out year, out month, out day);

        m = Compact.Match(s);
        if (m.Success)
            return Validate(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                out year, out month, out day);

        m = MonthDay.Match(s);
        if (m.Success)
            return Validate(null, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                out year, out month, out day);

        if (DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.None, out var parsed) ||
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return Validate(parsed.Year, parsed.Month, parsed.Day, out year, out month, out day);

        return false;
    }

    private static bool Validate(int? y, int m, int d, out int? year, out int month, out int day)
    {
        year = null;
        month = 0;
        day = 0;
        if (m is < 1 or > 12 || d < 1) return false;
        if (y is { } yy && (yy < 1900 || yy > DateTime.Now.Year)) return false;
        if (d > DateTime.DaysInMonth(y ?? 2000, m)) return false;
        year = y;
        month = m;
        day = d;
        return true;
    }
}
