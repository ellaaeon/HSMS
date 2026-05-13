using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Desktop.Printing;

/// <summary>
/// In-memory queue with disk persistence (so jobs survive an app restart). Implements the state machine
/// described in module 1: Queued -> Rendering -> ReadyToPrint -> Printing -> Printed -> Logged (or Failed).
/// Retry policy: exponential backoff (1s, 4s, 9s) for printer errors; logging retries are independent so
/// we never reprint just because /api/print-logs blipped.
/// </summary>
public sealed class PrintQueueService : IPrintQueueService, IDisposable
{
    private static readonly string QueueDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HSMS", "PrintQueue");

    private readonly IReportClient _reportClient;
    private readonly IPrinterService _printerService;
    private readonly IPrintLogClient _printLogClient;
    private readonly ConcurrentDictionary<Guid, PrintJob> _jobs = new();
    private readonly SemaphoreSlim _persistLock = new(1, 1);

    public event Action<PrintJob>? JobChanged;

    public PrintQueueService(IReportClient reportClient, IPrinterService printerService, IPrintLogClient printLogClient)
    {
        _reportClient = reportClient;
        _printerService = printerService;
        _printLogClient = printLogClient;

        try
        {
            Directory.CreateDirectory(QueueDir);
            foreach (var file in Directory.EnumerateFiles(QueueDir, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var job = JsonSerializer.Deserialize<PrintJob>(json);
                    if (job is null) continue;
                    if (job.State is PrintJobState.Logged or PrintJobState.Cancelled)
                    {
                        File.Delete(file);
                        continue;
                    }
                    _jobs[job.CorrelationId] = job;
                }
                catch
                {
                    // Discard corrupt queue entries silently rather than crash the app on startup.
                }
            }
        }
        catch
        {
            // Persistence is best-effort - continue with an in-memory queue if disk is unavailable.
        }
    }

    public IReadOnlyList<PrintJob> Snapshot() => _jobs.Values.OrderBy(j => j.EnqueuedAtUtc).ToList();

    public PrintJob Enqueue(ReportRenderRequestDto request)
    {
        var job = new PrintJob
        {
            CorrelationId = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId,
            Request = request
        };
        request.CorrelationId = job.CorrelationId;
        _jobs[job.CorrelationId] = job;
        Persist(job);
        Notify(job);
        _ = Task.Run(() => DrivePrintFlowAsync(job, CancellationToken.None));
        return job;
    }

    public async Task<PrintJob> RenderForPreviewAsync(ReportRenderRequestDto request, CancellationToken cancellationToken)
    {
        var job = new PrintJob
        {
            CorrelationId = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId,
            Request = request
        };
        request.CorrelationId = job.CorrelationId;
        _jobs[job.CorrelationId] = job;
        Persist(job);
        Notify(job);

        await RenderAsync(job, cancellationToken);
        return job;
    }

    public Task PrintAsync(PrintJob job, CancellationToken cancellationToken) => DriveFromReadyAsync(job, cancellationToken);

    public Task RetryAsync(PrintJob job, CancellationToken cancellationToken)
    {
        if (job.State is PrintJobState.Failed or PrintJobState.Cancelled)
        {
            job.State = PrintJobState.Queued;
            job.LastError = null;
            Persist(job); Notify(job);
        }
        return DrivePrintFlowAsync(job, cancellationToken);
    }

    public async Task FlushPendingLogsAsync(CancellationToken cancellationToken)
    {
        foreach (var job in _jobs.Values.Where(j => j.State == PrintJobState.Printed).ToList())
        {
            await TryLogAsync(job, cancellationToken);
        }
    }

    private async Task DrivePrintFlowAsync(PrintJob job, CancellationToken cancellationToken)
    {
        try
        {
            await RenderAsync(job, cancellationToken);
            if (job.State == PrintJobState.ReadyToPrint)
            {
                await DriveFromReadyAsync(job, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            job.State = PrintJobState.Failed;
            job.LastError = ex.Message;
            Persist(job); Notify(job);
        }
    }

    private async Task RenderAsync(PrintJob job, CancellationToken cancellationToken)
    {
        job.State = PrintJobState.Rendering;
        job.StartedAtUtc = DateTime.UtcNow;
        Persist(job); Notify(job);

        var result = await _reportClient.RenderAsync(job.Request, cancellationToken);
        if (!result.Success)
        {
            job.State = PrintJobState.Failed;
            job.LastError = result.ErrorMessage ?? "Rendering failed.";
            Persist(job); Notify(job);
            return;
        }

        job.PdfBytes = result.PdfBytes;
        job.PageCount = result.PageCount;
        job.State = PrintJobState.ReadyToPrint;
        Persist(job); Notify(job);
    }

    private async Task DriveFromReadyAsync(PrintJob job, CancellationToken cancellationToken)
    {
        if (job.PdfBytes is null || job.PdfBytes.Length == 0)
        {
            job.State = PrintJobState.Failed;
            job.LastError = "No rendered PDF to print.";
            Persist(job); Notify(job);
            return;
        }

        job.State = PrintJobState.Printing;
        Persist(job); Notify(job);

        var maxAttempts = 3;
        Exception? lastError = null;
        for (var attempt = 1; attempt <= maxAttempts && !cancellationToken.IsCancellationRequested; attempt++)
        {
            job.Attempts = attempt;
            try
            {
                await _printerService.PrintPdfAsync(job.PdfBytes, job.Request.PrinterName, job.Request.Copies, cancellationToken);
                job.PrinterUsed = job.Request.PrinterName ?? _printerService.GetDefaultPrinterName();
                job.State = PrintJobState.Printed;
                job.CompletedAtUtc = DateTime.UtcNow;
                Persist(job); Notify(job);
                lastError = null;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
                job.LastError = ex.Message;
                Persist(job); Notify(job);
                if (attempt < maxAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt * attempt), cancellationToken);
                }
            }
        }

        if (lastError is not null)
        {
            job.State = PrintJobState.Failed;
            Persist(job); Notify(job);
            return;
        }

        await TryLogAsync(job, cancellationToken);
    }

    private async Task TryLogAsync(PrintJob job, CancellationToken cancellationToken)
    {
        try
        {
            var dto = new PrintLogCreateDto
            {
                ReportType = job.Request.ReportType,
                SterilizationId = job.Request.SterilizationId,
                QaTestId = job.Request.QaTestId,
                PrinterName = job.PrinterUsed ?? job.Request.PrinterName,
                Copies = job.Request.Copies,
                CorrelationId = job.CorrelationId,
                ClientMachine = string.IsNullOrWhiteSpace(job.Request.ClientMachine) ? Environment.MachineName : job.Request.ClientMachine,
                Parameters = new
                {
                    job.Request.FromUtc,
                    job.Request.ToUtc,
                    job.Request.SterilizationTypeFilter,
                    job.Request.IncludeReceiptImages,
                    job.PageCount
                }
            };
            job.PrintLogId = await _printLogClient.RecordPrintAsync(dto, cancellationToken);
            job.State = PrintJobState.Logged;
            Persist(job); Notify(job);

            // Once logged we can drop the persisted file - the audit history lives in the DB.
            try
            {
                var file = JobPath(job.CorrelationId);
                if (File.Exists(file)) File.Delete(file);
                _jobs.TryRemove(job.CorrelationId, out _);
            }
            catch { /* best-effort cleanup */ }
        }
        catch (Exception ex)
        {
            job.LastError = $"Print logged locally only — server logging failed: {ex.Message}";
            Persist(job); Notify(job);
            // Stay in Printed state so a future FlushPendingLogsAsync() retries the log call.
        }
    }

    private static string JobPath(Guid id) => Path.Combine(QueueDir, $"{id:N}.json");

    private void Persist(PrintJob job)
    {
        try
        {
            _persistLock.Wait();
            // Don't persist huge PDFs to disk - only metadata. Bytes are regenerated on retry by re-rendering.
            var snapshot = new PrintJob
            {
                CorrelationId = job.CorrelationId,
                EnqueuedAtUtc = job.EnqueuedAtUtc,
                StartedAtUtc = job.StartedAtUtc,
                CompletedAtUtc = job.CompletedAtUtc,
                Request = job.Request,
                State = job.State,
                Attempts = job.Attempts,
                LastError = job.LastError,
                PageCount = job.PageCount,
                PrinterUsed = job.PrinterUsed,
                PrintLogId = job.PrintLogId
            };
            File.WriteAllText(JobPath(job.CorrelationId), JsonSerializer.Serialize(snapshot));
        }
        catch
        {
            // Persistence failures are non-fatal.
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private void Notify(PrintJob job)
    {
        try
        {
            JobChanged?.Invoke(job);
        }
        catch
        {
            // UI handler exceptions never bubble back into the queue worker.
        }
    }

    public void Dispose()
    {
        _persistLock.Dispose();
    }
}
