namespace HSMS.Shared.Contracts.Reporting;

/// <summary>
/// API result shape when rendering returns a JSON body (preview-mode). For direct-print use the API also exposes
/// <c>POST /api/reports/render/pdf</c> which returns the raw PDF stream + metadata via response headers.
/// </summary>
public sealed class ReportRenderResponseDto
{
    public string ReportType { get; set; } = string.Empty;
    public Guid CorrelationId { get; set; }

    /// <summary>Base64-encoded PDF bytes. Desktop converts to <c>byte[]</c> and feeds the spooler.</summary>
    public string PdfBase64 { get; set; } = string.Empty;

    public int PageCount { get; set; }
    public long PdfSizeBytes { get; set; }

    /// <summary>Rendered at UTC. Useful for client-side caching keys.</summary>
    public DateTime RenderedAtUtc { get; set; }

    /// <summary>Non-blocking warnings surfaced by validation (e.g. missing receipt, missing optional fields).</summary>
    public List<ReportWarningDto> Warnings { get; set; } = [];
}

public sealed class ReportWarningDto
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

/// <summary>HTTP response header names used by /api/reports/render/pdf so callers can read metadata without parsing the body.</summary>
public static class ReportResponseHeaders
{
    public const string CorrelationId = "X-HSMS-Report-CorrelationId";
    public const string PageCount = "X-HSMS-Report-PageCount";
    public const string Warnings = "X-HSMS-Report-Warnings";
}
