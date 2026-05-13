namespace HSMS.Desktop.Printing;

public interface IPrinterService
{
    /// <summary>Names of installed printers visible to the current user (sorted, default first).</summary>
    IReadOnlyList<string> EnumeratePrinters();
    string? GetDefaultPrinterName();

    /// <summary>
    /// Renders the supplied PDF bytes onto the named printer (Windows spooler). The PDF is rasterized via
    /// <see cref="HSMS.Desktop.Printing.PdfRasterizer"/>, then each page is sent through <c>System.Drawing.Printing</c>.
    /// Supports thermal (58mm/80mm) and A4 — the spooler picks page size from the printer's installed driver.
    /// </summary>
    Task PrintPdfAsync(byte[] pdfBytes, string? printerName, int copies, CancellationToken cancellationToken);
}
