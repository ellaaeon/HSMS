using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Runtime.Versioning;

namespace HSMS.Desktop.Printing;

/// <summary>
/// Windows-driver printing for both thermal (58mm/80mm) and A4 printers.
/// We delegate page-size + DPI handling to the Windows print spooler so each printer's installed driver
/// picks the right paper, avoiding clipping and surprise rescaling.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPrinterService(PdfRasterizer rasterizer) : IPrinterService
{
    public IReadOnlyList<string> EnumeratePrinters()
    {
        var defaults = GetDefaultPrinterName();
        var all = PrinterSettings.InstalledPrinters
            .Cast<string?>()
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
        all.Sort(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(defaults))
        {
            all.Remove(defaults);
            all.Insert(0, defaults);
        }

        return all;
    }

    public string? GetDefaultPrinterName()
    {
        try
        {
            using var doc = new PrintDocument();
            return doc.PrinterSettings.PrinterName;
        }
        catch
        {
            return null;
        }
    }

    public async Task PrintPdfAsync(byte[] pdfBytes, string? printerName, int copies, CancellationToken cancellationToken)
    {
        if (pdfBytes is null || pdfBytes.Length == 0)
        {
            throw new InvalidOperationException("Cannot print an empty PDF.");
        }

        copies = Math.Clamp(copies, 1, 20);

        // Rasterize PDF pages to bitmaps, then draw each page onto the spooler's canvas.
        // PdfRasterizer falls back to file-association printing when no rasterizer NuGet is installed.
        var rasterized = await rasterizer.RasterizeAsync(pdfBytes, cancellationToken);
        if (rasterized.Pages.Count == 0)
        {
            // Nothing to draw - the rasterizer printed via shell association as a fallback.
            return;
        }

        await Task.Run(() =>
        {
            using var doc = new PrintDocument();
            if (!string.IsNullOrWhiteSpace(printerName))
            {
                doc.PrinterSettings.PrinterName = printerName;
            }

            doc.PrinterSettings.Copies = (short)copies;
            doc.DefaultPageSettings.Margins = new Margins(20, 20, 20, 20);
            // Let the printer choose orientation - thermal prints portrait by default; landscape pages get auto-rotated by drivers.

            var pageIndex = 0;
            doc.PrintPage += (_, e) =>
            {
                if (pageIndex >= rasterized.Pages.Count) { e.HasMorePages = false; return; }
                using var stream = new MemoryStream(rasterized.Pages[pageIndex]);
                using var img = Image.FromStream(stream);

                var bounds = e.PageBounds;
                var scale = Math.Min(
                    (float)bounds.Width / img.Width,
                    (float)bounds.Height / img.Height);
                var w = img.Width * scale;
                var h = img.Height * scale;
                var x = bounds.X + (bounds.Width - w) / 2f;
                var y = bounds.Y + (bounds.Height - h) / 2f;

                e.Graphics?.DrawImage(img, x, y, w, h);

                pageIndex++;
                e.HasMorePages = pageIndex < rasterized.Pages.Count;
            };

            doc.Print();
        }, cancellationToken);
    }
}
