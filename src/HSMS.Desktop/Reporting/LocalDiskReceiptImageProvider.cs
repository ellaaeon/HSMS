using System.IO;
using HSMS.Application.Reporting.Builders;
using HSMS.Persistence.Data;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Desktop.Reporting;

/// <summary>
/// Standalone-WPF receipt image provider. Reads bytes directly from disk; for PDFs returns the derived
/// preview PNG written by Module 2 alongside the original.
/// </summary>
public sealed class LocalDiskReceiptImageProvider(IDbContextFactory<HsmsDbContext> dbFactory) : IReceiptImageProvider
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
            var derived = DerivedPreviewPath(receipt.FilePath);
            if (File.Exists(derived))
            {
                return await File.ReadAllBytesAsync(derived, cancellationToken);
            }
            return null;
        }

        if (!File.Exists(receipt.FilePath)) return null;
        return await File.ReadAllBytesAsync(receipt.FilePath, cancellationToken);
    }

    private static string DerivedPreviewPath(string originalFilePath)
    {
        var dir = Path.GetDirectoryName(originalFilePath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(originalFilePath);
        return Path.Combine(dir, "_derived", $"{name}.preview.png");
    }
}
