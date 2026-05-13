using System.Security.Claims;
using System.Security.Cryptography;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Api.Infrastructure.Files;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using HSMS.Shared.Contracts.Receipts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/sterilizations/{sterilizationId:int}/receipts")]
[Authorize]
public sealed class ReceiptsController(
    HsmsDbContext dbContext,
    IOptions<StorageOptions> storageOptions,
    IAuditService auditService,
    ReceiptDerivationQueue derivationQueue) : ControllerBase
{
    private static readonly HashSet<string> AllowedExt = [".jpg", ".jpeg", ".png", ".pdf"];

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<ReceiptListItemDto>> Upload(
        int sterilizationId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > storageOptions.Value.MaxUploadBytes)
        {
            return BadRequest(new ApiError { Code = "FILE_TOO_LARGE", Message = "Invalid file size." });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext))
        {
            return BadRequest(new ApiError { Code = "FILE_TYPE_NOT_ALLOWED", Message = "Only JPG, PNG and PDF are allowed." });
        }

        var cycle = await dbContext.Sterilizations.SingleOrDefaultAsync(x => x.SterilizationId == sterilizationId, cancellationToken);
        if (cycle is null)
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Sterilization cycle not found." });
        }

        var now = DateTime.UtcNow;
        var yearPath = Path.Combine(storageOptions.Value.ReceiptsRootPath, now.Year.ToString("0000"));
        var tmpPath = Path.Combine(storageOptions.Value.ReceiptsRootPath, "_tmp");
        Directory.CreateDirectory(yearPath);
        Directory.CreateDirectory(tmpPath);
        Directory.CreateDirectory(Path.Combine(yearPath, "_derived"));

        var stamp = now.ToString("yyyyMMdd_HHmmss");
        var guid = Guid.NewGuid().ToString("N");
        var finalFileName = $"cycle_{cycle.CycleNo}_{stamp}_{guid}{ext}";
        var finalPath = Path.Combine(yearPath, finalFileName);
        var tempFilePath = Path.Combine(tmpPath, $"{guid}.uploading");

        string shaHex;
        await using (var source = file.OpenReadStream())
        await using (var target = System.IO.File.Create(tempFilePath))
        using (var sha = SHA256.Create())
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                sha.TransformBlock(buffer, 0, read, null, 0);
            }
            sha.TransformFinalBlock([], 0, 0);
            shaHex = Convert.ToHexString(sha.Hash!).ToLowerInvariant();
        }

        // Per-cycle dedupe: same content uploaded twice for the same cycle is rejected so audit trails stay clean.
        var dupe = await dbContext.CycleReceipts.AsNoTracking()
            .AnyAsync(x => x.SterilizationId == sterilizationId && x.Sha256 == shaHex, cancellationToken);
        if (dupe)
        {
            try { System.IO.File.Delete(tempFilePath); } catch { /* best-effort */ }
            return Conflict(new ApiError
            {
                Code = "RECEIPT_DUPLICATE",
                Message = "This file has already been attached to this cycle."
            });
        }

        var entity = new CycleReceipt
        {
            SterilizationId = sterilizationId,
            FilePath = finalPath,
            FileName = finalFileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            Sha256 = shaHex,
            CapturedAt = now
        };

        dbContext.CycleReceipts.Add(entity);
        dbContext.CycleReceiptDerivationStates.Add(new CycleReceiptDerivationState
        {
            ReceiptId = 0, // placeholder; updated after first SaveChanges via shadow ID propagation
            State = CycleReceiptDerivationStates.Pending,
            UpdatedAtUtc = now
        });

        // We can't insert the derivation state row until we have the receipt id. Save receipt first, then state.
        dbContext.CycleReceiptDerivationStates.Local.Clear();

        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                dbContext.CycleReceiptDerivationStates.Add(new CycleReceiptDerivationState
                {
                    ReceiptId = entity.ReceiptId,
                    State = CycleReceiptDerivationStates.Pending,
                    UpdatedAtUtc = now
                });

                await auditService.AppendAsync(dbContext,
                    module: "Sterilization",
                    entityName: "cycle_receipts",
                    entityId: entity.ReceiptId.ToString(),
                    action: "Create",
                    actorAccountId: GetActorId(),
                    clientMachine: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    oldValues: null,
                    newValues: new { entity.FileName, entity.ContentType, entity.FileSizeBytes, sha256 = shaHex },
                    correlationId: Guid.NewGuid(),
                    cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                try { System.IO.File.Delete(tempFilePath); } catch { /* best-effort cleanup */ }
                throw;
            }
        }

        System.IO.File.Move(tempFilePath, finalPath, overwrite: false);

        // Schedule derivation asynchronously. Failures are isolated to the worker; the upload itself has succeeded.
        await derivationQueue.EnqueueAsync(entity.ReceiptId, cancellationToken);

        return Ok(new ReceiptListItemDto
        {
            ReceiptId = entity.ReceiptId,
            SterilizationId = sterilizationId,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            FileSizeBytes = entity.FileSizeBytes,
            CapturedAtUtc = entity.CapturedAt,
            DerivationState = CycleReceiptDerivationStates.Pending,
            HasPreview = false,
            HasThumbnail = false
        });
    }

    /// <summary>Lazy-loadable list, optionally paged. Includes derivation state so the UI can show the correct thumbnail or "rendering" state.</summary>
    [HttpGet]
    public async Task<ActionResult<List<ReceiptListItemDto>>> List(
        int sterilizationId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, 200);

        var receipts = await dbContext.CycleReceipts.AsNoTracking()
            .Where(x => x.SterilizationId == sterilizationId)
            .OrderByDescending(x => x.CapturedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var ids = receipts.Select(r => r.ReceiptId).ToList();
        var states = await dbContext.CycleReceiptDerivationStates.AsNoTracking()
            .Where(x => ids.Contains(x.ReceiptId))
            .ToDictionaryAsync(x => x.ReceiptId, x => x.State, cancellationToken);

        var assets = await dbContext.CycleReceiptAssets.AsNoTracking()
            .Where(x => ids.Contains(x.ReceiptId))
            .Select(x => new { x.ReceiptId, x.AssetKind })
            .ToListAsync(cancellationToken);

        var assetsByReceipt = assets.GroupBy(a => a.ReceiptId).ToDictionary(
            g => g.Key,
            g => new { Preview = g.Any(a => a.AssetKind == CycleReceiptAssetKinds.PreviewPng),
                       Thumb = g.Any(a => a.AssetKind == CycleReceiptAssetKinds.ThumbnailPng) });

        var rows = receipts.ConvertAll(r =>
        {
            assetsByReceipt.TryGetValue(r.ReceiptId, out var avail);
            // Image originals always count as preview-available (no derivation needed).
            var ext = Path.GetExtension(r.FileName).ToLowerInvariant();
            var isImage = ext is ".jpg" or ".jpeg" or ".png";
            return new ReceiptListItemDto
            {
                ReceiptId = r.ReceiptId,
                SterilizationId = r.SterilizationId,
                FileName = r.FileName,
                ContentType = r.ContentType,
                FileSizeBytes = r.FileSizeBytes,
                CapturedAtUtc = r.CapturedAt,
                DerivationState = states.GetValueOrDefault(r.ReceiptId, CycleReceiptDerivationStates.Pending),
                HasPreview = avail?.Preview == true || isImage,
                HasThumbnail = avail?.Thumb == true
            };
        });

        return Ok(rows);
    }

    [HttpGet("{receiptId:int}")]
    public async Task<IActionResult> Download(int sterilizationId, int receiptId, CancellationToken cancellationToken)
    {
        var row = await dbContext.CycleReceipts
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.SterilizationId == sterilizationId && x.ReceiptId == receiptId, cancellationToken);
        if (row is null || !System.IO.File.Exists(row.FilePath))
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Receipt file not found on server." });
        }

        var stream = System.IO.File.OpenRead(row.FilePath);
        return File(stream, row.ContentType, row.FileName);
    }

    [HttpGet("{receiptId:int}/preview")]
    public async Task<IActionResult> Preview(int sterilizationId, int receiptId, CancellationToken cancellationToken)
    {
        var row = await dbContext.CycleReceipts.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SterilizationId == sterilizationId && x.ReceiptId == receiptId, cancellationToken);
        if (row is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Receipt not found." });

        var ext = Path.GetExtension(row.FileName).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png")
        {
            if (!System.IO.File.Exists(row.FilePath))
            {
                return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Original image missing on disk." });
            }
            return PhysicalFile(row.FilePath, row.ContentType);
        }

        var asset = await dbContext.CycleReceiptAssets.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ReceiptId == receiptId && x.AssetKind == CycleReceiptAssetKinds.PreviewPng, cancellationToken);
        if (asset is null || !System.IO.File.Exists(asset.FilePath))
        {
            return NotFound(new ApiError
            {
                Code = "PREVIEW_PENDING",
                Message = "Preview is still being generated. Try again in a few seconds."
            });
        }

        Response.Headers.CacheControl = "public,max-age=86400";
        return PhysicalFile(asset.FilePath, "image/png");
    }

    [HttpGet("{receiptId:int}/thumbnail")]
    public async Task<IActionResult> Thumbnail(int sterilizationId, int receiptId, CancellationToken cancellationToken)
    {
        var asset = await dbContext.CycleReceiptAssets.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ReceiptId == receiptId && x.AssetKind == CycleReceiptAssetKinds.ThumbnailPng, cancellationToken);
        if (asset is null || !System.IO.File.Exists(asset.FilePath))
        {
            return NotFound(new ApiError
            {
                Code = "THUMBNAIL_PENDING",
                Message = "Thumbnail is still being generated."
            });
        }

        Response.Headers.CacheControl = "public,max-age=86400";
        return PhysicalFile(asset.FilePath, "image/png");
    }

    private int? GetActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
