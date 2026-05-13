using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Desktop.Printing;

public interface IPrintQueueService
{
    /// <summary>Snapshot of all jobs currently in the queue (oldest first).</summary>
    IReadOnlyList<PrintJob> Snapshot();

    /// <summary>Fires when any job state changes; UI subscribes for status bar / queue panel updates.</summary>
    event Action<PrintJob>? JobChanged;

    /// <summary>
    /// Enqueue a render+print job. Returns immediately; the queue worker advances state asynchronously.
    /// Use <see cref="EnqueueAndPreviewAsync"/> for UI flows that need PDF bytes back without going through the spooler.
    /// </summary>
    PrintJob Enqueue(ReportRenderRequestDto request);

    /// <summary>Same as Enqueue but the caller awaits the rendered PDF (preview-mode). The job stops at <see cref="PrintJobState.ReadyToPrint"/>.</summary>
    Task<PrintJob> RenderForPreviewAsync(ReportRenderRequestDto request, CancellationToken cancellationToken);

    /// <summary>Move a <see cref="PrintJobState.ReadyToPrint"/> job onto the spooler.</summary>
    Task PrintAsync(PrintJob job, CancellationToken cancellationToken);

    /// <summary>Manual retry hook for failed jobs.</summary>
    Task RetryAsync(PrintJob job, CancellationToken cancellationToken);

    Task FlushPendingLogsAsync(CancellationToken cancellationToken);
}
