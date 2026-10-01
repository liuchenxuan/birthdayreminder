using System.Collections.Specialized;
using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using Avalonia.Threading;
using BirthdayReminder.Models;

namespace BirthdayReminder.Services;

public enum ImportMode
{
    /// <summary>合并：已有同名同生日的记录跳过，其余追加。</summary>
    Merge = 0,

    /// <summary>更新：同名则覆盖其生日/备注，其余追加。</summary>
    Update = 1,

    /// <summary>覆盖：清空现有名单后导入。</summary>
    Replace = 2
}

public sealed class ImportResult
{
    public int Added { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; } = new();

    public override string ToString()
    {
        var text = $"导入完成：新增 {Added} 人，更新 {Updated} 人，跳过 {Skipped} 人";
        if (Errors.Count > 0)
            text += $"，{Errors.Count} 行无法识别（{string.Join("；", Errors.Take(3))}{(Errors.Count > 3 ? "…" : "")}）";
        return text + "。";
    }
}

/// <summary>
/// 名单与设置的唯一数据源：负责读写 Settings.json（防抖保存）、搜索、导入以及各种“最近生日”查询。
/// </summary>
public class BirthdayDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;
    private readonly object _saveLock = new();
    private CancellationTokenSource? _saveCts;

    public PluginSettings Settings { get; }

    /// <summary>名单增删时触发（不含单条记录的字段编辑）。</summary>
    public event EventHandler? EntriesChanged;

    /// <summary>任何数据（名单、字段、设置）变化时触发。可能不在 UI 线程。</summary>
    public event EventHandler? DataChanged;

    public BirthdayDataService(string path)
    {
        _path = path;
        Settings = Load(path);
        BirthdayCalculator.Feb29OnMarch1 = Settings.Feb29Mode == 1;
        PruneFiredKeys();
        Hook();
    }

    // ─────────────────────────── 持久化 ───────────────────────────

    private static PluginSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<PluginSettings>(File.ReadAllText(path), JsonOptions);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // 配置损坏：备份后使用默认配置，避免插件无法启动
            try
            {
                File.Copy(path, path + $".broken-{DateTime.Now:yyyyMMddHHmmss}", true);
            }
            catch
            {
                // ignored
            }
        }

        return new PluginSettings();
    }

    private void Hook()
    {
        Settings.PropertyChanged += OnItemChanged;
        AttachEntries(Settings.Entries);
        AttachTriggers(Settings.Triggers);

        Settings.Entries.CollectionChanged += (_, e) =>
        {
            Detach(e.OldItems);
            AttachItems(e.NewItems);
            EntriesChanged?.Invoke(this, EventArgs.Empty);
            NotifyChanged();
        };
        Settings.Triggers.CollectionChanged += (_, e) =>
        {
            Detach(e.OldItems);
            AttachItems(e.NewItems);
            NotifyChanged();
        };
    }

    private void AttachEntries(IEnumerable<BirthdayEntry> items)
    {
        foreach (var i in items) i.PropertyChanged += OnItemChanged;
    }

    private void AttachTriggers(IEnumerable<ReminderTrigger> items)
    {
        foreach (var i in items) i.PropertyChanged += OnItemChanged;
    }

    private void AttachItems(System.Collections.IList? items)
    {
        if (items == null) return;
        foreach (var i in items.OfType<INotifyPropertyChanged>()) i.PropertyChanged += OnItemChanged;
    }

    private void Detach(System.Collections.IList? items)
    {
        if (items == null) return;
        foreach (var i in items.OfType<INotifyPropertyChanged>()) i.PropertyChanged -= OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BirthdayEntry.Info)) return; // 纯显示字段
        if (sender is PluginSettings && e.PropertyName == nameof(PluginSettings.Feb29Mode))
            BirthdayCalculator.Feb29OnMarch1 = Settings.Feb29Mode == 1;
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        DataChanged?.Invoke(this, EventArgs.Empty);
        RequestSave();
    }

    /// <summary>防抖保存：连续修改 600ms 后统一写盘。</summary>
    public void RequestSave()
    {
        CancellationTokenSource cts;
        lock (_saveLock)
        {
            _saveCts?.Cancel();
            cts = _saveCts = new CancellationTokenSource();
        }

        _ = Task.Delay(600, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            // 序列化需要在 UI 线程执行，避免和界面编辑并发修改集合
            Dispatcher.UIThread.Post(Flush);
        }, TaskScheduler.Default);
    }

    /// <summary>立即保存。</summary>
    public void Flush()
    {
        try
        {
            lock (_saveLock)
            {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(Settings, JsonOptions));
                File.Move(tmp, _path, true);
            }
        }
        catch
        {
            // 保存失败不应影响主程序
        }
    }

    // ─────────────────────────── 防重复记录 ───────────────────────────

    private void PruneFiredKeys()
    {
        var threshold = DateTime.Today.AddDays(-3).ToString("yyyyMMdd");
        Settings.FiredKeys.RemoveAll(k =>
        {
            var parts = k.Split('|');
            return parts.Length < 2 || string.CompareOrdinal(parts[1], threshold) < 0;
        });
    }

    public bool IsFired(string key) => Settings.FiredKeys.Contains(key);

    public void MarkFired(string key)
    {
        if (Settings.FiredKeys.Contains(key)) return;
        Settings.FiredKeys.Add(key);
        RequestSave();
    }

    public void ClearFiredKeys()
    {
        Settings.FiredKeys.Clear();
        RequestSave();
    }

    // ─────────────────────────── 查询 ───────────────────────────

    public IEnumerable<BirthdayEntry> ActiveEntries =>
        Settings.Entries.Where(e => e.Enabled && !string.IsNullOrWhiteSpace(e.Name));

    /// <summary>计算 today 当天需要提醒的所有人（按提前天数升序，再按姓名）。</summary>
    public List<BirthdayHit> GetHits(DateTime today, IEnumerable<int> offsets)
    {
        today = today.Date;
        var list = new List<BirthdayHit>();
        var offsetList = offsets.Distinct().OrderBy(x => x).ToList();
        foreach (var e in ActiveEntries.ToList())
        {
            foreach (var offset in offsetList)
            {
                var target = today.AddDays(offset);
                if (!BirthdayCalculator.OccursOn(e, target)) continue;
                list.Add(new BirthdayHit
                {
                    Entry = e,
                    Offset = offset,
                    Date = target,
                    Age = BirthdayCalculator.AgeOn(e, target)
                });
            }
        }

        return list.OrderBy(h => h.Offset).ThenBy(h => h.Entry.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>今日寿星。</summary>
    public List<BirthdayHit> GetToday(DateTime today) => GetHits(today, new[] { 0 });

    /// <summary>最近的下一个生日（不含今天）；同一天有多人时一并返回。</summary>
    public (DateTime Date, List<BirthdayHit> People)? GetNext(DateTime today)
    {
        today = today.Date;
        DateTime? best = null;
        var people = new List<BirthdayHit>();
        foreach (var e in ActiveEntries.ToList())
        {
            var next = BirthdayCalculator.NextOccurrence(e, today.AddDays(1));
            if (next == null) continue;
            if (best == null || next < best)
            {
                best = next;
                people.Clear();
            }

            if (next == best)
                people.Add(new BirthdayHit
                {
                    Entry = e,
                    Date = next.Value,
                    Offset = (int)(next.Value - today).TotalDays,
                    Age = BirthdayCalculator.AgeOn(e, next.Value)
                });
        }

        return best == null ? null : (best.Value, people.OrderBy(p => p.Entry.Name).ToList());
    }

    /// <summary>本月尚未过去的生日（已过的不计入）。</summary>
    public List<BirthdayHit> GetRestOfMonth(DateTime today, bool includeToday)
    {
        today = today.Date;
        var list = new List<BirthdayHit>();
        foreach (var e in ActiveEntries.ToList())
        {
            var d = BirthdayCalculator.Occurrence(today.Year, e.Month, e.Day);
            if (d == null || d.Value.Month != today.Month) continue;
            if (d.Value < today || (!includeToday && d.Value == today)) continue;
            list.Add(new BirthdayHit
            {
                Entry = e,
                Date = d.Value,
                Offset = (int)(d.Value - today).TotalDays,
                Age = BirthdayCalculator.AgeOn(e, d.Value)
            });
        }

        return list.OrderBy(h => h.Date).ThenBy(h => h.Entry.Name).ToList();
    }

    /// <summary>
    /// 搜索：姓名/备注包含关键字；也支持 “5月”“5月12日”“05-12”“2010” 这样的日期关键字。
    /// </summary>
    public IEnumerable<BirthdayEntry> Search(string? keyword)
    {
        var q = (keyword ?? "").Trim();
        if (q.Length == 0) return Settings.Entries;

        return Settings.Entries.Where(e =>
            e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            e.Note.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            e.BirthdayText.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            $"{e.Month}月{e.Day}日".Contains(q, StringComparison.Ordinal) ||
            $"{e.Month}月".Equals(q, StringComparison.Ordinal) ||
            (e.Year > 0 && $"{e.Year}年".Equals(q, StringComparison.Ordinal)));
    }

    // ─────────────────────────── 编辑 / 导入 ───────────────────────────

    public BirthdayEntry AddBlank()
    {
        var entry = new BirthdayEntry { Name = "", Month = DateTime.Today.Month, Day = DateTime.Today.Day };
        Settings.Entries.Add(entry);
        return entry;
    }

    public void Remove(BirthdayEntry entry) => Settings.Entries.Remove(entry);

    public ImportResult Import(IEnumerable<ExcelImporter.ParsedRow> rows, ImportMode mode)
    {
        var result = new ImportResult();
        var parsed = new List<(string Name, int Year, int Month, int Day, string Note)>();
        foreach (var row in rows)
        {
            if (!BirthdayParser.TryParse(row.RawBirthday, out var y, out var m, out var d))
            {
                result.Errors.Add($"第{row.RowNumber}行 {row.Name}");
                continue;
            }

            parsed.Add((row.Name, y ?? 0, m, d, row.Note));
        }

        if (mode == ImportMode.Replace && parsed.Count > 0)
            Settings.Entries.Clear();

        foreach (var p in parsed)
        {
            var sameName = Settings.Entries.FirstOrDefault(e => e.Name == p.Name);
            if (sameName != null)
            {
                var identical = sameName.Month == p.Month && sameName.Day == p.Day &&
                                (p.Year == 0 || sameName.Year == p.Year);
                if (mode == ImportMode.Update && !identical)
                {
                    sameName.Year = p.Year;
                    sameName.Month = p.Month;
                    sameName.Day = p.Day;
                    if (!string.IsNullOrWhiteSpace(p.Note)) sameName.Note = p.Note;
                    result.Updated++;
                    continue;
                }

                if (identical || mode == ImportMode.Update)
                {
                    result.Skipped++;
                    continue;
                }
                // Merge 模式下同名不同生日：视为两个人（比如重名），追加
            }

            Settings.Entries.Add(new BirthdayEntry
            {
                Name = p.Name, Year = p.Year, Month = p.Month, Day = p.Day, Note = p.Note
            });
            result.Added++;
        }

        return result;
    }
}
