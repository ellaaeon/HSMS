namespace HSMS.Shared.Contracts.Reporting;

/// <summary>
/// Single envelope used by the desktop client + API to request a rendered report.
/// Only one of <see cref="SterilizationId"/> or <see cref="QaTestId"/> applies depending on report type;
/// <see cref="BiLogSheet"/> uses the date range fields instead.
/// </summary>
public sealed class ReportRenderRequestDto
{
    /// <summary>One of <see cref="ReportType"/>.</summary>
    public string ReportType { get; set; } = string.Empty;

    public int? SterilizationId { get; set; }
    public int? QaTestId { get; set; }

    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }

    /// <summary>Optional sterilization-type filter for the BI log sheet (High temperature / Low temperature).</summary>
    public string? SterilizationTypeFilter { get; set; }

    /// <summary>When true the renderer embeds receipt images (originals or generated PNG previews) on page 2.</summary>
    public bool IncludeReceiptImages { get; set; } = true;

    /// <summary>Number of copies the desktop will request from the spooler. Stored on the print log for audit.</summary>
    public int Copies { get; set; } = 1;

    /// <summary>Optional explicit printer name. When null the desktop prints to the user's default printer.</summary>
    public string? PrinterName { get; set; }

    /// <summary>
    /// Client-generated correlation id. The desktop always sends a fresh GUID per F9/click;
    /// the API uses it to enforce idempotent print-log writes.
    /// </summary>
    public Guid CorrelationId { get; set; } = Guid.NewGuid();

    public string? ClientMachine { get; set; }
}
