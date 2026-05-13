namespace HSMS.Application.Exports;

/// <summary>
/// Reusable streaming-style Excel exporter (Module 7).
/// Backed by ClosedXML; we keep an interface so callers can later swap to a streaming
/// SAX implementation (e.g. EPPlus) for very large datasets without churning consumers.
/// </summary>
public interface IExcelExportService
{
    /// <summary>
    /// Writes a single sheet workbook to <paramref name="output"/>.
    /// </summary>
    /// <param name="sheetName">Worksheet tab name (max 31 chars - automatically truncated).</param>
    /// <param name="columns">Column definitions: header text + extractor callable + optional Excel format.</param>
    /// <param name="rows">Row data source.</param>
    /// <param name="output">Output stream that the workbook is written to.</param>
    /// <param name="progress">Optional progress callback invoked every 500 rows.</param>
    /// <param name="cancellationToken">Cancellation propagated to row iteration and stream writes.</param>
    Task WriteSheetAsync<TRow>(
        string sheetName,
        IReadOnlyList<ExcelColumn<TRow>> columns,
        IAsyncEnumerable<TRow> rows,
        Stream output,
        IProgress<ExcelExportProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Synchronous overload for in-memory enumerations (audit/print log exports).</summary>
    Task WriteSheetAsync<TRow>(
        string sheetName,
        IReadOnlyList<ExcelColumn<TRow>> columns,
        IEnumerable<TRow> rows,
        Stream output,
        IProgress<ExcelExportProgress>? progress,
        CancellationToken cancellationToken);
}

public sealed class ExcelColumn<TRow>(string header, Func<TRow, object?> extractor, string? excelFormat = null, double? width = null)
{
    public string Header { get; } = header;
    public Func<TRow, object?> Extractor { get; } = extractor;
    public string? ExcelFormat { get; } = excelFormat;
    public double? Width { get; } = width;
}

public sealed class ExcelExportProgress
{
    public int RowsWritten { get; init; }
    public int? TotalRows { get; init; }
    public string Stage { get; init; } = "Writing";
}
