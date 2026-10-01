using BirthdayReminder.Models;

namespace BirthdayReminder.Services;

public enum TestKind
{
    /// <summary>完整流程：横幅 + 全屏动画 + 语音。</summary>
    Full,
    Banner,
    Celebration,
    Speech
}

/// <summary>一次合并后的提醒内容。所有命中的人（当天 + 各档提前）都会合并到同一条里。</summary>
public sealed class ReminderMessage
{
    public string MaskText { get; init; } = "";
    public string OverlayText { get; init; } = "";
    public string SpeechText { get; init; } = "";
    public string CelebrationTitle { get; init; } = "";
    public string CelebrationSubtitle { get; init; } = "";
    public bool HasToday { get; init; }
    public IReadOnlyList<BirthdayHit> Hits { get; init; } = Array.Empty<BirthdayHit>();

    public bool ShowBanner { get; set; }
    public bool ShowCelebration { get; set; }
    public bool Speak { get; set; }
    public bool IsTest { get; set; }
}

public static class ReminderMessageBuilder
{
    public static ReminderMessage Build(IReadOnlyList<BirthdayHit> hits, PluginSettings s)
    {
        var today = hits.Where(h => h.Offset == 0).ToList();
        var hasToday = today.Count > 0;

        var lines = new List<string>();
        var speeches = new List<string>();
        foreach (var group in hits.GroupBy(h => h.Offset).OrderBy(g => g.Key))
        {
            var vars = Vars(group.ToList(), s, group.Key);
            lines.Add(Render(group.Key == 0 ? s.TodayLineFormat : s.UpcomingLineFormat, vars));
            speeches.Add(Render(group.Key == 0 ? s.SpeechTodayFormat : s.SpeechUpcomingFormat, vars));
        }

        // 遮罩：优先显示今日寿星，否则显示预告
        var maskSource = hasToday ? today : hits.ToList();
        var maskVars = Vars(maskSource, s, hasToday ? 0 : maskSource.Min(h => h.Offset));
        var mask = Render(hasToday ? s.MaskTodayFormat : s.MaskUpcomingFormat, maskVars);

        var celebration = hasToday ? today : maskSource;
        var celebrationVars = Vars(celebration, s, hasToday ? 0 : celebration.Min(h => h.Offset));

        return new ReminderMessage
        {
            MaskText = mask,
            OverlayText = string.Join("；", lines.Where(l => l.Length > 0)),
            SpeechText = string.Join("。", speeches.Where(l => l.Length > 0)),
            CelebrationTitle = hasToday
                ? Render(s.CelebrationTitleFormat, celebrationVars)
                : Render(s.MaskUpcomingFormat, celebrationVars),
            CelebrationSubtitle = Render(s.CelebrationSubtitleFormat, celebrationVars),
            HasToday = hasToday,
            Hits = hits
        };
    }

    /// <summary>为一组同档提醒生成占位符字典。</summary>
    public static Dictionary<string, string> Vars(IReadOnlyList<BirthdayHit> group, PluginSettings s, int offset)
    {
        var names = group.Select(h => h.Entry.Name).ToList();
        var items = group.Select(h => Render(h.Age != null ? s.ItemFormat : s.ItemFormatNoAge, ItemVars(h)));
        var first = group.FirstOrDefault();
        var shortNames = names.Count <= 3
            ? string.Join("、", names)
            : $"{string.Join("、", names.Take(3))}等{names.Count}人";

        return new Dictionary<string, string>
        {
            ["items"] = string.Join("、", items),
            ["names"] = string.Join("、", names),
            ["shortnames"] = shortNames,
            ["count"] = group.Count.ToString(),
            ["when"] = BirthdayCalculator.FormatWhen(offset),
            ["days"] = offset.ToString(),
            ["date"] = first != null ? BirthdayCalculator.FormatDate(first.Date) : "",
            ["name"] = first?.Entry.Name ?? "",
            ["age"] = first?.Age?.ToString() ?? ""
        };
    }

    public static Dictionary<string, string> ItemVars(BirthdayHit h) => new()
    {
        ["name"] = h.Entry.Name,
        ["age"] = h.Age?.ToString() ?? "",
        ["date"] = BirthdayCalculator.FormatDate(h.Date),
        ["days"] = h.Offset.ToString(),
        ["when"] = BirthdayCalculator.FormatWhen(h.Offset)
    };

    /// <summary>把模板里的 {占位符} 替换为实际值；未知占位符原样保留。</summary>
    public static string Render(string? template, IDictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(template)) return "";
        var text = template;
        foreach (var pair in vars)
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        return text;
    }
}
