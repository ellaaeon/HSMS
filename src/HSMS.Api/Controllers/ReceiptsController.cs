using System.Security.Claims;
using System.Security.Cryptography;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Api.Infrastructure.Files;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
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
    IAuditService auditService) : ControllerBase
{
    private static readonly HashSet<string> AllowedExt = [".jpg", ".jpeg", ".png", ".pdf"];

    [HttpPost]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<ReceiptMetadataDto>> Upload(
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

        var stamp = now.ToString("yyyyMMdd_HHmmss");
        var guid = Guid.NewGuid().ToString("N");
        var finalFileName = $"cycle_{cycle.CycleNo}_{stamp}_{guid}{ext}";
        var finalPath = Path.Combine(yearPath, finalFileName);
        var tempFilePath = Path.Combine(tmpPath, $"{guid}.uploading");

        string? shaHex;
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
        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditService.AppendAsync(dbContext,
                    module: "Sterilization",
                    entityName: "cycle_receipts",
                    entityId: entity.ReceiptId.ToString(),
                    action: "Create",
                    actorAccountId: GetActorId(),
                    clientMachine: HttpContext.Connection.RemoteIpAddress?.ToString(),
                    oldValues: null,
                    newValues: new { entity.FileName, entity.ContentType, entity.FileSizeBytes },
                    correlationId: Guid.NewGuid(),
                    cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                try
                {
                    if (System.IO.File.Exists(tempFilePath))
                    {
                        System.IO.File.Delete(tempFilePath);
                    }
                }
                catch
                {
                    /* best-effort cleanup */
                }

                throw;
            }
        }

        System.IO.File.Move(tempFilePath, finalPath, overwrite: false);

        return Ok(new ReceiptMetadataDto
        {
            ReceiptId = entity.ReceiptId,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            FileSizeBytes = entity.FileSizeBytes,
            CapturedAtUtc = entity.CapturedAt
        });
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

    private int? GetActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
