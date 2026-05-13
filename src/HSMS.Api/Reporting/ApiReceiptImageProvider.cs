using HSMS.Application.Reporting.Builders;
using HSMS.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Api.Reporting;

/// <summary>
/// API-side <see cref="IReceiptImageProvider"/>. For images returns the original bytes; for PDFs returns
/// the derived preview PNG (see Module 2 / <c>ReceiptDerivationService</c>). Returns null when no usable image is available.
/// </summary>
public sealed class ApiReceiptImageProvider(IDbContextFactory<HsmsDbContext> dbFactory) : IReceiptImageProvider
{
    public async Task<byte[]?> LoadReceiptImageAsync(int receiptId, CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var receipt = await db.CycleReceipts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ReceiptId == receiptId, cancellationToken);
        if (receipt is null) return null;

        var contentType = (receipt.ContentType ?? string.Empty).ToLowerInvariant();
        var ext = Path.GetExtension(receipt.FileName).ToLowerInvariant();

        if (contentType.Contains("pdf") || ext == ".pdf")
        {
            // Derived preview PNG written by Module 2 alongside the original.
            var derivedPath = ReceiptDerivedPaths.PreviewPngFor(receipt.FilePath);
            if (File.Exists(derivedPath))
            {
                return await File.ReadAllBytesAsync(derivedPath, cancellationToken);
            }
            // Derivation hasn't run yet (or failed); skip rather than embedding an unrenderable PDF.
            return null;
        }

        if (!File.Exists(receipt.FilePath)) return null;
        return await File.ReadAllBytesAsync(receipt.FilePath, cancellationToken);
    }
}

/// <summary>Single source of truth for derived asset path conventions used by Module 2.</summary>
public static class ReceiptDerivedPaths
{
    public static string PreviewPngFor(string originalFilePath)
    {
        var dir = Path.GetDirectoryName(originalFilePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(originalFilePath);
        return Path.Combine(dir, "_derived", $"{name}.preview.png");
    }

    public static string ThumbnailPngFor(string originalFilePath)
    {
        var dir = Path.GetDirectoryName(originalFilePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(originalFilePath);
        return Path.Combine(dir, "_derived", $"{name}.thumb.png");
    }
}
