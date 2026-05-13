using Docnet.Core;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using HSMS.Api.Reporting;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Png;

namespace HSMS.Api.Infrastructure.Files;

public sealed class ReceiptDerivationService(
    IDbContextFactory<HsmsDbContext> dbFactory,
    IOptions<StorageOptions> storageOptions,
    ILogger<ReceiptDerivationService> logger) : IReceiptDerivationService
{
    private const int PreviewMaxWidthPx = 1200;
    private const int PreviewMaxHeightPx = 1700;
    private const int ThumbnailMaxPx = 256;

    public async Task<ReceiptDerivationResult> DeriveAsync(int receiptId, CancellationToken cancellationToken)
    {
        var result = new ReceiptDerivationResult();

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var receipt = await db.CycleReceipts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ReceiptId == receiptId, cancellationToken);
        if (receipt is null)
        {
            result.Error = "Receipt not found.";
            await UpdateStateAsync(db, receiptId, CycleReceiptDerivationStates.Failed, "Receipt not found.", cancellationToken);
            return result;
        }

        if (!File.Exists(receipt.FilePath))
        {
            result.Error = "Original file is missing on disk.";
            await UpdateStateAsync(db, receiptId, CycleReceiptDerivationStates.Failed, result.Error, cancellationToken);
            return result;
        }

        var ext = Path.GetExtension(receipt.FileName).ToLowerInvariant();
        var contentType = (receipt.ContentType ?? string.Empty).ToLowerInvariant();
        var derivedDir = Path.Combine(Path.GetDirectoryName(receipt.FilePath) ?? string.Empty, "_derived");
        Directory.CreateDirectory(derivedDir);

        await UpdateStateAsync(db, receiptId, CycleReceiptDerivationStates.Running, null, cancellationToken);

        try
        {
            byte[] previewBytes;
            int previewWidth, previewHeight;

            if (contentType.Contains("pdf") || ext == ".pdf")
            {
                (previewBytes, previewWidth, previewHeight) = RasterizeFirstPdfPageToPng(receipt.FilePath);
            }
            else if (contentType.StartsWith("image/") || ext is ".png" or ".jpg" or ".jpeg")
            {
                using var img = await Image.LoadAsync<Rgba32>(receipt.FilePath, cancellationToken);
                (previewBytes, previewWidth, previewHeight) = ResizeAsPng(img, PreviewMaxWidthPx, PreviewMaxHeightPx);
            }
            else
            {
                result.NotApplicable = true;
                await UpdateStateAsync(db, receiptId, CycleReceiptDerivationStates.NotApplicable, null, cancellationToken);
                return result;
            }

            var previewPath = ReceiptDerivedPaths.PreviewPngFor(receipt.FilePath);
            await File.WriteAllBytesAsync(previewPath, previewBytes, cancellationToken);

            byte[] thumbBytes;
            int thumbWidth, thumbHeight;
            using (var preview = Image.Load<Rgba32>(previewBytes))
            {
                (thumbBytes, thumbWidth, thumbHeight) = ResizeAsPng(preview, ThumbnailMaxPx, ThumbnailMaxPx);
            }

            var thumbPath = ReceiptDerivedPaths.ThumbnailPngFor(receipt.FilePath);
            await File.WriteAllBytesAsync(thumbPath, thumbBytes, cancellationToken);

            await UpsertAssetAsync(db, receiptId, CycleReceiptAssetKinds.PreviewPng,
                previewPath, previewWidth, previewHeight, previewBytes.LongLength, cancellationToken);
            await UpsertAssetAsync(db, receiptId, CycleReceiptAssetKinds.ThumbnailPng,
                thumbPath, thumbWidth, thumbHeight, thumbBytes.LongLength, cancellationToken);
            await UpdateStateAsync(db, receiptId, CycleReceiptDerivationStates.Completed, null, cancellationToken);

            result.Success = true;
            result.PreviewPath = previewPath;
            result.PreviewWidth = previewWidth;
            result.PreviewHeight = previewHeight;
            result.PreviewSizeBytes = previewBytes.LongLength;
            result.ThumbnailPath = thumbPath;
            result.ThumbnailWidth = thumbWidth;
            result.ThumbnailHeight = thumbHeight;
            result.ThumbnailSizeBytes = thumbBytes.LongLength;
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Receipt derivation failed for receipt {ReceiptId}", receiptId);
            await UpdateStateAsync(db, receiptId, CycleReceiptDerivationStates.Failed, ex.Message, cancellationToken);
            result.Error = ex.Message;
            return result;
        }
    }

    private static (byte[] bytes, int width, int height) RasterizeFirstPdfPageToPng(string pdfPath)
    {
        using var library = DocLib.Instance;
        using var docReader = library.GetDocReader(pdfPath, new PageDimensions(PreviewMaxWidthPx, PreviewMaxHeightPx));
        using var pageReader = docReader.GetPageReader(0);

        var width = pageReader.GetPageWidth();
        var height = pageReader.GetPageHeight();
        var rawBytes = pageReader.GetImage();

        using var image = Image.LoadPixelData<Bgra32>(rawBytes, width, height);
        // Docnet emits BGRA; ImageSharp wants RGBA.
        image.Mutate(c => c.BackgroundColor(Color.White));
        using var rgba = image.CloneAs<Rgba32>();

        using var ms = new MemoryStream();
        rgba.SaveAsPng(ms, new PngEncoder());
        return (ms.ToArray(), width, height);
    }

    private static (byte[] bytes, int width, int height) ResizeAsPng(Image<Rgba32> source, int maxWidth, int maxHeight)
    {
        var ratio = Math.Min((double)maxWidth / source.Width, (double)maxHeight / source.Height);
        var newWidth = ratio < 1 ? Math.Max(1, (int)Math.Round(source.Width * ratio)) : source.Width;
        var newHeight = ratio < 1 ? Math.Max(1, (int)Math.Round(source.Height * ratio)) : source.Height;

        using var resized = source.Clone(c => c.Resize(newWidth, newHeight));
        using var ms = new MemoryStream();
        resized.SaveAsPng(ms, new PngEncoder());
        return (ms.ToArray(), newWidth, newHeight);
    }

    private static async Task UpsertAssetAsync(HsmsDbContext db, int receiptId, string kind, string path,
        int width, int height, long sizeBytes, CancellationToken cancellationToken)
    {
        var existing = await db.CycleReceiptAssets.SingleOrDefaultAsync(
            x => x.ReceiptId == receiptId && x.AssetKind == kind, cancellationToken);
        if (existing is null)
        {
            db.CycleReceiptAssets.Add(new CycleReceiptAsset
            {
                ReceiptId = receiptId,
                AssetKind = kind,
                FilePath = path,
                WidthPx = width,
                HeightPx = height,
                FileSizeBytes = sizeBytes,
                GeneratedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            existing.FilePath = path;
            existing.WidthPx = width;
            existing.HeightPx = height;
            existing.FileSizeBytes = sizeBytes;
            existing.GeneratedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task UpdateStateAsync(HsmsDbContext db, int receiptId, string state, string? error,
        CancellationToken cancellationToken)
    {
        var row = await db.CycleReceiptDerivationStates
            .SingleOrDefaultAsync(x => x.ReceiptId == receiptId, cancellationToken);
        if (row is null)
        {
            db.CycleReceiptDerivationStates.Add(new CycleReceiptDerivationState
            {
                ReceiptId = receiptId,
                State = state,
                LastError = error,
                Attempts = 1,
                UpdatedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            row.State = state;
            row.LastError = error;
            row.Attempts++;
            row.UpdatedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
