using HSMS.Application.Reporting.Datasets;

namespace HSMS.Application.Reporting;

/// <summary>
/// Renders a strongly-typed report dataset into PDF bytes.
/// Implementations are interchangeable: the default <see cref="QuestPdfReportRenderEngine"/>
/// renders hospital-style PDFs in pure managed code; a future RDLC engine can plug in
/// without rewriting any dataset builder or validator.
/// </summary>
public interface IReportRenderEngine
{
    Task<RenderedReport> RenderLoadRecordAsync(LoadRecordReportData data, CancellationToken cancellationToken);
    Task<RenderedReport> RenderBiLogSheetAsync(BiLogSheetReportData data, CancellationToken cancellationToken);
    Task<RenderedReport> RenderQaTestAsync(QaTestReportData data, CancellationToken cancellationToken);
}

public sealed class RenderedReport
{
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public int PageCount { get; set; }
}
