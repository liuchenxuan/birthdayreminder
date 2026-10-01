using System.Text;
using BirthdayReminder.Models;
using MiniExcelLibs;

namespace BirthdayReminder.Services;

/// <summary>
/// 从 Excel(.xlsx) / CSV 读取“姓名、生日”两列（可选“备注”列），并支持导出名单与导入模板。
/// 表头可以是：姓名/名字/Name、生日/出生日期/出生年月/Birthday；没有表头时按 A 列姓名、B 列生日处理。
/// </summary>
public static class ExcelImporter
{
    public sealed record ParsedRow(int RowNumber, string Name, object? RawBirthday, string Note);

    private static readonly string[] NameHeaders = { "姓名", "名字", "学生", "同学", "name" };
    private static readonly string[] BirthdayHeaders = { "生日", "出生日期", "出生年月", "出生", "日期", "birthday", "birth", "date" };
    private static readonly string[] NoteHeaders = { "备注", "说明", "note", "remark", "班级" };

    public static List<ParsedRow> Read(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var table = ext switch
        {
            ".xlsx" or ".xlsm" => ReadXlsx(stream),
            ".csv" or ".txt" => ReadCsv(stream),
            ".xls" => throw new NotSupportedException("暂不支持旧版 .xls，请在 Excel 中另存为 .xlsx 或 .csv 后再导入。"),
            _ => throw new NotSupportedException($"不支持的文件类型 {ext}，请选择 .xlsx 或 .csv 文件。")
        };
        return ParseTable(table);
    }

    // ───────────────────────── xlsx ─────────────────────────

    private static List<List<object?>> ReadXlsx(Stream stream)
    {
        var table = new List<List<object?>>();
        foreach (var row in MiniExcel.Query(stream, useHeaderRow: false, excelType: ExcelType.XLSX))
        {
            if (row is not IDictionary<string, object?> dict) continue;
            var cells = new List<object?>();
            foreach (var pair in dict)
            {
                var col = ColumnIndex(pair.Key);
                if (col < 0) continue;
                while (cells.Count <= col) cells.Add(null);
                cells[col] = pair.Value;
            }

            table.Add(cells);
        }

        return table;
    }

    private static int ColumnIndex(string letters)
    {
        var n = 0;
        foreach (var c in letters.ToUpperInvariant())
        {
            if (c is < 'A' or > 'Z') return -1;
            n = n * 26 + (c - 'A' + 1);
        }

        return n - 1;
    }

    // ───────────────────────── csv ─────────────────────────

    private static List<List<object?>> ReadCsv(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            // 中文 Excel 导出的 CSV 常为 GBK
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            text = Encoding.GetEncoding("GB18030").GetString(bytes);
        }

        var table = new List<List<object?>>();
        var row = new List<object?>();
        var cell = new StringBuilder();
        var inQuotes = false;

        void EndCell()
        {
            row.Add(cell.ToString());
            cell.Clear();
        }

        void EndRow()
        {
            EndCell();
            if (row.Any(c => !string.IsNullOrWhiteSpace(c?.ToString()))) table.Add(row);
            row = new List<object?>();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"') inQuotes = false;
                else cell.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',' or '，' or '\t' or ';': EndCell(); break;
                case '\r': break;
                case '\n': EndRow(); break;
                default: cell.Append(c); break;
            }
        }

        if (cell.Length > 0 || row.Count > 0) EndRow();
        return table;
    }

    // ───────────────────────── 通用解析 ─────────────────────────

    private static List<ParsedRow> ParseTable(List<List<object?>> table)
    {
        var result = new List<ParsedRow>();
        if (table.Count == 0) return result;

        int nameCol = 0, birthCol = 1, noteCol = 2, start = 0;
        for (var i = 0; i < Math.Min(table.Count, 5); i++)
        {
            var headers = table[i].Select(c => (c?.ToString() ?? "").Trim().ToLowerInvariant()).ToList();
            var n = headers.FindIndex(h => NameHeaders.Any(k => h.Contains(k)));
            var b = headers.FindIndex(h => BirthdayHeaders.Any(k => h.Contains(k)));
            if (n < 0 && b < 0) continue;

            nameCol = n >= 0 ? n : 0;
            birthCol = b >= 0 ? b : (nameCol == 0 ? 1 : 0);
            noteCol = headers.FindIndex(h => NoteHeaders.Any(k => h.Contains(k)));
            start = i + 1;
            break;
        }

        for (var i = start; i < table.Count; i++)
        {
            var cells = table[i];
            string Text(int col) => col >= 0 && col < cells.Count ? (cells[col]?.ToString() ?? "").Trim() : "";
            object? Raw(int col) => col >= 0 && col < cells.Count ? cells[col] : null;

            var name = Text(nameCol);
            var raw = Raw(birthCol);
            if (name.Length == 0 && string.IsNullOrWhiteSpace(raw?.ToString())) continue;
            if (name.Length == 0) continue;
            result.Add(new ParsedRow(i + 1, name, raw, noteCol >= 0 ? Text(noteCol) : ""));
        }

        return result;
    }

    // ───────────────────────── 导出 ─────────────────────────

    /// <summary>导出为 xlsx。templateOnly 为 true 时只导出带示例的导入模板。</summary>
    public static void ExportXlsx(Stream stream, IEnumerable<BirthdayEntry> entries, bool templateOnly)
    {
        var rows = new List<Dictionary<string, object?>>();
        if (templateOnly)
        {
            rows.Add(Row("张三", "2010-05-12", "示例：填写完整日期即可自动计算年龄"));
            rows.Add(Row("李四", "2009-11-03", ""));
            rows.Add(Row("王老师", "09-21", "示例：不知道出生年份时只写 月-日"));
        }
        else
        {
            foreach (var e in entries)
                rows.Add(Row(e.Name, e.BirthdayText, e.Note));
            if (rows.Count == 0) rows.Add(Row("", "", ""));
        }

        MiniExcel.SaveAs(stream, rows, true, "生日名单", ExcelType.XLSX);
    }

    private static Dictionary<string, object?> Row(string name, string birthday, string note) => new()
    {
        ["姓名"] = name,
        ["生日"] = birthday,
        ["备注"] = note
    };
}
