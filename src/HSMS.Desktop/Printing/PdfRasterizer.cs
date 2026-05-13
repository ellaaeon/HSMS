using System.Diagnostics;
using System.IO;

namespace HSMS.Desktop.Printing;

/// <summary>
/// Rasterizes PDF pages to PNG bytes for spooler-based printing. The implementation is dependency-light:
/// when no managed PDF rasterizer is registered (current default), the desktop falls back to printing
/// the PDF via the OS file-association handler (Adobe / Edge), which is reliable on Windows workstations.
///
/// To enable in-process rasterization (recommended later), install <c>Docnet.Core</c> in this project and
/// replace <see cref="RasterizeAsync"/> with a Docnet-backed implementation. The print queue + log pipeline
/// does not need to change.
/// </summary>
public sealed class PdfRasterizer
{
    public Task<RasterizedPdf> RasterizeAsync(byte[] pdfBytes, CancellationToken cancellationToken)
    {
        // Default fallback: write to a temp PDF and ask the shell to print it. Printers + driver dialogs
        // pick up correct DPI from the installed driver. Returns an empty rasterized result so the caller
        // knows not to render anything via System.Drawing.Printing.
        var tempPath = Path.Combine(Path.GetTempPath(), $"hsms_print_{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(tempPath, pdfBytes);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = tempPath,
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
        }
        catch
        {
            // If the shell verb fails (rare on Windows workstations without a default PDF viewer),
            // surface this through the print queue's retry machinery via an exception caller observes.
            File.Delete(tempPath);
            throw;
        }

        return Task.FromResult(new RasterizedPdf());
    }
}

public sealed class RasterizedPdf
{
    public List<byte[]> Pages { get; } = [];
}
