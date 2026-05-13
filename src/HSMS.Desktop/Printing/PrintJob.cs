using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Desktop.Printing;

public enum PrintJobState
{
    Queued = 0,
    Rendering = 1,
    ReadyToPrint = 2,
    Printing = 3,
    Printed = 4,
    Logged = 5,
    Failed = 99,
    Cancelled = 100
}

/// <summary>
/// One row in the local print queue. Persisted to disk so jobs survive an app restart.
/// </summary>
public sealed class PrintJob
{
    public Guid CorrelationId { get; init; } = Guid.NewGuid();
    public DateTime EnqueuedAtUtc { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public ReportRenderRequestDto Request { get; init; } = new();

    public PrintJobState State { get; set; } = PrintJobState.Queued;
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    public byte[]? PdfBytes { get; set; }
    public int? PageCount { get; set; }
    public string? PrinterUsed { get; set; }

    /// <summary>Last printed log id returned by /api/print-logs.</summary>
    public long? PrintLogId { get; set; }

    /// <summary>Friendly summary used by status bars / queue panels.</summary>
    public string DescribeState() => State switch
    {
        PrintJobState.Queued => "Queued",
        PrintJobState.Rendering => "Rendering report…",
        PrintJobState.ReadyToPrint => "Ready to print",
        PrintJobState.Printing => "Printing…",
        PrintJobState.Printed => "Printed (logging…)",
        PrintJobState.Logged => "Done",
        PrintJobState.Failed => $"Failed — {LastError ?? "unknown"}",
        PrintJobState.Cancelled => "Cancelled",
        _ => State.ToString()
    };
}
