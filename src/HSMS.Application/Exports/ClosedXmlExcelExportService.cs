using ClosedXML.Excel;

namespace HSMS.Application.Exports;

/// <summary>
/// ClosedXML-backed implementation. Buffers rows in-memory before writing to <paramref name="output"/>.
/// For datasets &gt; ~100k rows consider switching to a streaming SAX engine; the interface is unchanged.
/// </summary>
public sealed class ClosedXmlExcelExportService : IExcelExportService
{
    public async Task WriteSheetAsync<TRow>(
        string sheetName,
        IReadOnlyList<ExcelColumn<TRow>> columns,
        IAsyncEnumerable<TRow> rows,
        Stream output,
        IProgress<ExcelExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(SafeSheetName(sheetName));
        WriteHeaderRow(ws, columns);

        var rowIndex = 2;
        var written = 0;
        await foreach (var row in rows.WithCancellation(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteDataRow(ws, columns, rowIndex, row);
            rowIndex++;
            written++;
            if (written % 500 == 0)
            {
                progress?.Report(new ExcelExportProgress { RowsWritten = written });
            }
        }

        FormatTable(ws, columns, rowIndex);
        progress?.Report(new ExcelExportProgress { RowsWritten = written, Stage = "Saving" });
        workbook.SaveAs(output);
        progress?.Report(new ExcelExportProgress { RowsWritten = written, Stage = "Done" });
    }

    public Task WriteSheetAsync<TRow>(
        string sheetName,
        IReadOnlyList<ExcelColumn<TRow>> columns,
        IEnumerable<TRow> rows,
        Stream output,
        IProgress<ExcelExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add(SafeSheetName(sheetName));
        WriteHeaderRow(ws, columns);

        var rowIndex = 2;
        var written = 0;
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteDataRow(ws, columns, rowIndex, row);
            rowIndex++;
            written++;
            if (written % 500 == 0)
            {
                progress?.Report(new ExcelExportProgress { RowsWritten = written });
            }
        }

        FormatTable(ws, columns, rowIndex);
        progress?.Report(new ExcelExportProgress { RowsWritten = written, Stage = "Saving" });
        workbook.SaveAs(output);
        progress?.Report(new ExcelExportProgress { RowsWritten = written, Stage = "Done" });
        return Task.CompletedTask;
    }

    private static void WriteHeaderRow<TRow>(IXLWorksheet ws, IReadOnlyList<ExcelColumn<TRow>> columns)
    {
        for (var c = 0; c < columns.Count; c++)
        {
            ws.Cell(1, c + 1).Value = columns[c].Header;
        }

        var headerRange = ws.Range(1, 1, 1, columns.Count);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.FromArgb(0xEE, 0xF6, 0xFF);
        headerRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.SheetView.FreezeRows(1);
    }

    private static void WriteDataRow<TRow>(IXLWorksheet ws, IReadOnlyList<ExcelColumn<TRow>> columns, int rowIndex, TRow row)
    {
        for (var c = 0; c < columns.Count; c++)
        {
            var col = columns[c];
            var raw = col.Extractor(row);
            var cell = ws.Cell(rowIndex, c + 1);
            switch (raw)
            {
                case null:
                    cell.Value = string.Empty;
                    break;
                case DateTime dt:
                    cell.Value = dt;
                    break;
                case decimal d:
                    cell.Value = d;
                    break;
                case double dbl:
                    cell.Value = dbl;
                    break;
                case int i:
                    cell.Value = i;
                    break;
                case long l:
                    cell.Value = l;
                    break;
                case bool b:
                    cell.Value = b;
                    break;
                default:
                    cell.Value = raw.ToString();
                    break;
            }
            if (!string.IsNullOrWhiteSpace(col.ExcelFormat))
            {
                cell.Style.NumberFormat.Format = col.ExcelFormat;
            }
        }
    }

    private static void FormatTable<TRow>(IXLWorksheet ws, IReadOnlyList<ExcelColumn<TRow>> columns, int lastRowIndex)
    {
        for (var c = 0; c < columns.Count; c++)
        {
            var width = columns[c].Width;
            if (width.HasValue)
            {
                ws.Column(c + 1).Width = width.Value;
            }
            else
            {
                ws.Column(c + 1).AdjustToContents(1, lastRowIndex);
            }
        }

        if (lastRowIndex >= 2)
        {
            ws.SheetView.FreezeRows(1);
            ws.RangeUsed()?.SetAutoFilter();
        }
    }

    private static string SafeSheetName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Sheet" : name.Trim();
        if (trimmed.Length > 31) trimmed = trimmed[..31];
        foreach (var bad in new[] { '/', '\\', '?', '*', ':', '[', ']' })
        {
            trimmed = trimmed.Replace(bad, '_');
        }
        return trimmed;
    }
}
