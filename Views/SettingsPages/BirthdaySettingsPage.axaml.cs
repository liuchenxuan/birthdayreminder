using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BirthdayReminder.Models;
using BirthdayReminder.Services;
using BirthdayReminder.ViewModels;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;

namespace BirthdayReminder.Views.SettingsPages;

/// <summary>生日提醒设置页：名单管理、导入导出、提醒规则、内容模板、测试。</summary>
[SettingsPageInfo("community.birthdayreminder.settings", "生日提醒")]
public partial class BirthdaySettingsPage : SettingsPageBase
{
    private readonly BirthdayDataService _data;
    private readonly ReminderEngine _engine;

    /// <summary>页面的视图模型（XAML 通过 RelativeSource 绑定到这里，避免宿主改写 DataContext）。</summary>
    public BirthdayPageViewModel Vm { get; }

    public BirthdaySettingsPage(BirthdayDataService data, ReminderEngine engine)
    {
        _data = data;
        _engine = engine;
        Vm = new BirthdayPageViewModel(data, engine);
        InitializeComponent();
    }

    // ───────────────────────── 测试 ─────────────────────────

    private void ButtonTestFull_OnClick(object? sender, RoutedEventArgs e) => _engine.RunTest(TestKind.Full);
    private void ButtonTestBanner_OnClick(object? sender, RoutedEventArgs e) => _engine.RunTest(TestKind.Banner);
    private void ButtonTestCelebration_OnClick(object? sender, RoutedEventArgs e) => _engine.RunTest(TestKind.Celebration);
    private void ButtonTestSpeech_OnClick(object? sender, RoutedEventArgs e) => _engine.RunTest(TestKind.Speech);

    private void ButtonClearFired_OnClick(object? sender, RoutedEventArgs e)
    {
        _data.ClearFiredKeys();
        Vm.StatusText = "已清除“已提醒”记录，今天的提醒会按规则重新触发。";
    }

    // ───────────────────────── 时间点 ─────────────────────────

    private void ButtonAddTrigger_OnClick(object? sender, RoutedEventArgs e) =>
        _data.Settings.Triggers.Add(new ReminderTrigger());

    private void ButtonRemoveTrigger_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is ReminderTrigger trigger)
            _data.Settings.Triggers.Remove(trigger);
    }

    // ───────────────────────── 名单 ─────────────────────────

    private void ButtonAddEntry_OnClick(object? sender, RoutedEventArgs e)
    {
        Vm.SearchText = ""; // 清除搜索，保证新增的空白行可见
        var entry = _data.AddBlank();
        Vm.StatusText = "已添加一行，请填写姓名与生日。";
        _ = entry;
    }

    private void ButtonRemoveEntry_OnClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is BirthdayEntry entry)
            _data.Remove(entry);
    }

    private async void ButtonImport_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择包含“姓名、生日”的 Excel / CSV 文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Excel / CSV") { Patterns = new[] { "*.xlsx", "*.csv" } }
                }
            });
            if (files.Count == 0) return;

            await using var source = await files[0].OpenReadAsync();
            using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer);
            buffer.Position = 0;

            var rows = ExcelImporter.Read(buffer, files[0].Name);
            if (rows.Count == 0)
            {
                Vm.StatusText = "没有读取到有效数据，请确认表头为“姓名、生日”。";
                return;
            }

            var result = _data.Import(rows, Vm.ImportMode);
            Vm.StatusText = result.ToString();
        }
        catch (Exception ex)
        {
            Vm.StatusText = $"导入失败：{ex.Message}";
        }
    }

    private async void ButtonTemplate_OnClick(object? sender, RoutedEventArgs e) => await ExportAsync(true);

    private async void ButtonExport_OnClick(object? sender, RoutedEventArgs e) => await ExportAsync(false);

    private async Task ExportAsync(bool templateOnly)
    {
        try
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;

            var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = templateOnly ? "保存导入模板" : "导出生日名单",
                SuggestedFileName = templateOnly ? "生日导入模板.xlsx" : "生日名单.xlsx",
                DefaultExtension = "xlsx",
                FileTypeChoices = new[] { new FilePickerFileType("Excel 工作簿") { Patterns = new[] { "*.xlsx" } } }
            });
            if (file == null) return;

            await using var stream = await file.OpenWriteAsync();
            ExcelImporter.ExportXlsx(stream, _data.Settings.Entries, templateOnly);
            Vm.StatusText = templateOnly ? "导入模板已保存。" : $"已导出 {_data.Settings.Entries.Count} 人。";
        }
        catch (Exception ex)
        {
            Vm.StatusText = $"保存失败：{ex.Message}";
        }
    }
}
