using System.Collections.ObjectModel;
using Avalonia.Threading;
using BirthdayReminder.Models;
using BirthdayReminder.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BirthdayReminder.ViewModels;

/// <summary>设置页的视图模型：负责搜索/排序过滤、今日概览和模板预览。</summary>
public class BirthdayPageViewModel : ObservableObject
{
    private readonly BirthdayDataService _data;
    private readonly ReminderEngine _engine;
    private string _searchText = "";
    private int _sortIndex;
    private int _importModeIndex;
    private string _statusText = "";
    private string _todaySummary = "";
    private string _nextSummary = "";
    private string _monthSummary = "";
    private string _countText = "";
    private string _previewMask = "";
    private string _previewOverlay = "";
    private string _previewSpeech = "";

    public PluginSettings Settings => _data.Settings;
    public string TokenHelp => PluginSettings.TokenHelp;

    /// <summary>经搜索/排序后展示在列表中的名单。</summary>
    public ObservableCollection<BirthdayEntry> FilteredEntries { get; } = new();

    public BirthdayPageViewModel(BirthdayDataService data, ReminderEngine engine)
    {
        _data = data;
        _engine = engine;
        _data.EntriesChanged += (_, _) => Dispatcher.UIThread.Post(RefreshList);
        _data.DataChanged += (_, _) => Dispatcher.UIThread.Post(RefreshSummary);
        RefreshList();
        RefreshSummary();
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? "")) RefreshList();
        }
    }

    /// <summary>0 = 录入顺序，1 = 按下一次生日，2 = 按姓名。</summary>
    public int SortIndex
    {
        get => _sortIndex;
        set
        {
            if (SetProperty(ref _sortIndex, value)) RefreshList();
        }
    }

    /// <summary>0 = 合并，1 = 更新，2 = 覆盖。</summary>
    public int ImportModeIndex
    {
        get => _importModeIndex;
        set => SetProperty(ref _importModeIndex, value);
    }

    public ImportMode ImportMode => (ImportMode)Math.Clamp(_importModeIndex, 0, 2);

    public string StatusText
    {
        get => _statusText;
        set => SetProperty(ref _statusText, value);
    }

    public string TodaySummary { get => _todaySummary; private set => SetProperty(ref _todaySummary, value); }
    public string NextSummary { get => _nextSummary; private set => SetProperty(ref _nextSummary, value); }
    public string MonthSummary { get => _monthSummary; private set => SetProperty(ref _monthSummary, value); }
    public string CountText { get => _countText; private set => SetProperty(ref _countText, value); }
    public string PreviewMask { get => _previewMask; private set => SetProperty(ref _previewMask, value); }
    public string PreviewOverlay { get => _previewOverlay; private set => SetProperty(ref _previewOverlay, value); }
    public string PreviewSpeech { get => _previewSpeech; private set => SetProperty(ref _previewSpeech, value); }

    public void RefreshList()
    {
        var today = DateTime.Now.Date;
        IEnumerable<BirthdayEntry> query = _data.Search(_searchText);
        query = _sortIndex switch
        {
            1 => query.OrderBy(e => BirthdayCalculator.NextOccurrence(e, today) ?? DateTime.MaxValue),
            2 => query.OrderBy(e => e.Name, StringComparer.CurrentCulture),
            _ => query
        };

        var list = query.ToList();
        FilteredEntries.Clear();
        foreach (var e in list) FilteredEntries.Add(e);
        RefreshSummary();
    }

    public void RefreshSummary()
    {
        var now = DateTime.Now;
        var today = now.Date;
        foreach (var e in _data.Settings.Entries) e.RefreshInfo(today);

        var t = BirthdaySummary.Today(_data, today);
        TodaySummary = t.Length > 0 ? $"🎂 今日寿星：{t}" : "今天没有人过生日";

        var next = BirthdaySummary.Next(_data, today, countdown: true);
        NextSummary = next.Length > 0 ? $"🎁 下一位：{next}" : "名单为空，先导入或添加生日吧";

        var month = BirthdaySummary.RestOfMonth(_data, today, includeToday: false, max: 12);
        MonthSummary = month.Length > 0 ? $"📅 本月剩余：{month}" : "本月已没有后续生日";

        var total = _data.Settings.Entries.Count;
        CountText = string.IsNullOrWhiteSpace(_searchText)
            ? $"共 {total} 人"
            : $"找到 {FilteredEntries.Count} / {total} 人";

        var sample = _engine.BuildSampleMessage();
        PreviewMask = sample.MaskText;
        PreviewOverlay = sample.OverlayText;
        PreviewSpeech = sample.SpeechText;
    }
}
