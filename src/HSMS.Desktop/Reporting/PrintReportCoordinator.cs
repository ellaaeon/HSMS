using System.Diagnostics;
using System.IO;
using System.Windows;
using HSMS.Desktop.Printing;
using HSMS.Desktop.Reporting.Views;
using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Desktop.Reporting;

/// <summary>
/// High-level entry point used by MainWindow for F9 / Print button. Provides:
/// - direct print (F9): instant, uses default printer + last-used copies
/// - preview modal (Print button): preview, choose printer, choose copies, export PDF
/// - export PDF: render and save to disk
/// All paths share the same render -> queue -> log pipeline so audit trails are identical.
/// </summary>
public sealed class PrintReportCoordinator(IPrintQueueService queue, IPrinterService printerService)
{
    private static int _lastCopies = 1;
    private static string? _lastPrinter;

    public IPrintQueueService Queue => queue;

    /// <summary>F9 / quick print. No dialog. Uses the last-known printer + copies.</summary>
    public async Task QuickPrintAsync(ReportRenderRequestDto request, Window? owner)
    {
        request.PrinterName ??= _lastPrinter ?? printerService.GetDefaultPrinterName();
        if (request.Copies <= 0) request.Copies = _lastCopies;
        _lastPrinter = request.PrinterName;
        _lastCopies = request.Copies;

        var loading = LoadingOverlay.Show(owner, "Generating report…");
        try
        {
            var job = await queue.RenderForPreviewAsync(request, CancellationToken.None);
            loading.Close();

            if (job.State == PrintJobState.Failed)
            {
                MessageBox.Show(owner, $"Could not generate report:\n\n{job.LastError}", "HSMS",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await queue.PrintAsync(job, CancellationToken.None);
        }
        catch (Exception ex)
        {
            loading.Close();
            MessageBox.Show(owner, $"Print failed:\n\n{ex.Message}", "HSMS",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Show the preview modal. The user can flip printer, change copies, click Print, or click Export PDF.
    /// </summary>
    public async Task ShowPreviewAsync(ReportRenderRequestDto request, Window? owner)
    {
        var loading = LoadingOverlay.Show(owner, "Generating report…");
        PrintJob job;
        try
        {
            job = await queue.RenderForPreviewAsync(request, CancellationToken.None);
        }
        finally
        {
            loading.Close();
        }

        if (job.State == PrintJobState.Failed || job.PdfBytes is null)
        {
            MessageBox.Show(owner, $"Could not generate report:\n\n{job.LastError}", "HSMS",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var win = new ReportPreviewWindow(job, queue, printerService) { Owner = owner };
        win.ShowDialog();
    }

    /// <summary>Save the rendered PDF to disk and open it (no print, no log).</summary>
    public async Task ExportToPdfAsync(ReportRenderRequestDto request, string targetPath, Window? owner)
    {
        var loading = LoadingOverlay.Show(owner, "Generating PDF…");
        try
        {
            var job = await queue.RenderForPreviewAsync(request, CancellationToken.None);
            if (job.State == PrintJobState.Failed || job.PdfBytes is null)
            {
                MessageBox.Show(owner, $"Could not generate report:\n\n{job.LastError}", "HSMS",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await File.WriteAllBytesAsync(targetPath, job.PdfBytes);
            try
            {
                Process.Start(new ProcessStartInfo(targetPath) { UseShellExecute = true });
            }
            catch
            {
                // Best-effort: file is on disk even if we can't auto-open it.
            }
        }
        finally
        {
            loading.Close();
        }
    }
}
