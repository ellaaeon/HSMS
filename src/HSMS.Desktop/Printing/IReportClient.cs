using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Desktop.Printing;

/// <summary>
/// Abstraction over &quot;render this report and give me PDF bytes&quot;.
/// Two implementations: in-process (calls <c>IReportManager</c> directly, used in standalone WPF deployment)
/// and HTTP (calls <c>POST /api/reports/render/pdf</c> when the desktop is paired with a remote API).
/// </summary>
public interface IReportClient
{
    Task<RenderClientResult> RenderAsync(ReportRenderRequestDto request, CancellationToken cancellationToken);
}

public sealed class RenderClientResult
{
    public bool Success { get; set; }
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public int PageCount { get; set; }
    public string? ErrorMessage { get; set; }
    public List<ReportWarningDto> Warnings { get; set; } = [];
}
