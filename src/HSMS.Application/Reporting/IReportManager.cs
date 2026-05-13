using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Application.Reporting;

/// <summary>
/// Façade used by API controllers and the WPF desktop client (in-process). One method, one validated PDF output.
/// </summary>
public interface IReportManager
{
    Task<ReportRenderOutcome> RenderAsync(ReportRenderRequestDto request, CancellationToken cancellationToken);
}

public sealed class ReportRenderOutcome
{
    public bool Success { get; set; }
    public ReportValidationResult Validation { get; set; } = new();
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public int PageCount { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }
    public DateTime RenderedAtUtc { get; set; }
}
