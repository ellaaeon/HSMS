using System.Security.Claims;
using System.Security.Cryptography;
using HSMS.Api.Infrastructure.Files;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/instrument-checks")]
[Authorize]
public sealed class InstrumentChecksController(
    HsmsDbContext dbContext,
    IOptions<StorageOptions> storageOptions,
    IAuditService auditService) : ControllerBase
{
    private static readonly HashSet<string> AllowedAttachmentExt = [".jpg", ".jpeg", ".png", ".pdf"];

    [HttpGet]
    public async Task<ActionResult<List<InstrumentCheckListItemDto>>> List(
        [FromQuery] string? query,
        [FromQuery] int take,
        CancellationToken cancellationToken)
    {
        if (take <= 0) take = 200;
        if (take > 2000) take = 2000;

        var src = dbContext.InstrumentChecks.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            src = src.Where(x =>
                x.ItemName.Contains(q) ||
                (x.SerialReference != null && x.SerialReference.Contains(q)) ||
                x.CheckedByName.Contains(q) ||
                (x.WitnessByName != null && x.WitnessByName.Contains(q)) ||
                (x.Remarks != null && x.Remarks.Contains(q)));
        }

        var rows = await (from c in src.OrderByDescending(x => x.CheckedAtUtc).Take(take)
                          join a in dbContext.Accounts.AsNoTracking() on c.WitnessApprovedBy equals a.AccountId into ap
                          from a in ap.DefaultIfEmpty()
                          select new InstrumentCheckListItemDto
                          {
                              InstrumentCheckId = c.InstrumentCheckId,
                              CheckedAtUtc = c.CheckedAtUtc,
                              ItemName = c.ItemName,
                              SerialReference = c.SerialReference,
                              CheckedByName = c.CheckedByName,
                              WitnessByName = c.WitnessByName,
                              Remarks = c.Remarks,
                              WitnessApprovedAtUtc = c.WitnessApprovedAt,
                              WitnessApprovedBy = c.WitnessApprovedBy,
                              WitnessApprovedByUsername = a != null ? a.Username : null,
                              AttachmentCount = dbContext.InstrumentCheckAttachments.AsNoTracking()
                                  .Count(x => x.InstrumentCheckId == c.InstrumentCheckId)
                          })
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<InstrumentCheckListItemDto>> Create(
        InstrumentCheckCreateDto request,
        CancellationToken cancellationToken)
    {
        var itemName = (request.ItemName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Item name is required." });
        }
        var checkedBy = (request.CheckedByName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(checkedBy))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Checked by name is required." });
        }

        var entity = new InstrumentCheck
        {
            CheckedAtUtc = DateTime.UtcNow,
            ItemName = itemName,
            SerialReference = string.IsNullOrWhiteSpace(request.SerialReference) ? null : request.SerialReference.Trim(),
            CheckedByName = checkedBy,
            WitnessByName = string.IsNullOrWhiteSpace(request.WitnessByName) ? null : request.WitnessByName.Trim(),
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
            CreatedBy = GetActorId()
        };

        dbContext.InstrumentChecks.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new InstrumentCheckListItemDto
        {
            InstrumentCheckId = entity.InstrumentCheckId,
            CheckedAtUtc = entity.CheckedAtUtc,
            ItemName = entity.ItemName,
            SerialReference = entity.SerialReference,
            CheckedByName = entity.CheckedByName,
            WitnessByName = entity.WitnessByName,
            Remarks = entity.Remarks
        });
    }

    [HttpPost("{id:int}/witness-approve")]
    public async Task<ActionResult<object>> WitnessApprove(
        int id,
        InstrumentCheckWitnessApproveDto request,
        CancellationToken cancellationToken)
    {
        var entity = await dbContext.InstrumentChecks.SingleOrDefaultAsync(x => x.InstrumentCheckId == id, cancellationToken);
        if (entity is null)
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Instrument check not found." });
        }

        var actor = GetActorId();
        if (actor is null)
        {
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Sign in required." });
        }

        if (entity.CreatedBy is not null && actor.Value == entity.CreatedBy.Value)
        {
            return Forbid();
        }

        if (entity.WitnessApprovedAt is not null)
        {
            return Conflict(new ApiError { Code = "ALREADY_APPROVED", Message = "This check was already witnessed." });
        }

        var oldValue = new { entity.WitnessApprovedAt, entity.WitnessApprovedBy };
        entity.WitnessApprovedAt = DateTime.UtcNow;
        entity.WitnessApprovedBy = actor;
        if (!string.IsNullOrWhiteSpace(request.Remarks))
        {
            var combined = string.IsNullOrWhiteSpace(entity.Remarks) ? request.Remarks.Trim() : $"{entity.Remarks}\nWitness: {request.Remarks.Trim()}";
            entity.Remarks = combined.Length > 2000 ? combined[..2000] : combined;
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.AppendAsync(dbContext, "InstrumentCheck", "tbl_instrument_checks",
                entity.InstrumentCheckId.ToString(), "WitnessApprove", actor, request.ClientMachine,
                oldValue, new { entity.WitnessApprovedAt, entity.WitnessApprovedBy }, Guid.NewGuid(), cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return Ok(new
        {
            instrumentCheckId = entity.InstrumentCheckId,
            witnessApprovedAtUtc = entity.WitnessApprovedAt,
            witnessApprovedBy = entity.WitnessApprovedBy
        });
    }

    [HttpGet("{id:int}/attachments")]
    public async Task<ActionResult<List<InstrumentCheckAttachmentListItemDto>>> ListAttachments(
        int id,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.InstrumentCheckAttachments.AsNoTracking()
            .Where(x => x.InstrumentCheckId == id)
            .OrderByDescending(x => x.CapturedAt)
            .Select(x => new InstrumentCheckAttachmentListItemDto
            {
                AttachmentId = x.AttachmentId,
                InstrumentCheckId = x.InstrumentCheckId,
                FileName = x.FileName,
                ContentType = x.ContentType,
                FileSizeBytes = x.FileSizeBytes,
                CapturedAtUtc = x.CapturedAt
            })
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult<InstrumentCheckAttachmentListItemDto>> UploadAttachment(
        int id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0 || file.Length > storageOptions.Value.MaxUploadBytes)
        {
            return BadRequest(new ApiError { Code = "FILE_TOO_LARGE", Message = "Invalid file size." });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedAttachmentExt.Contains(ext))
        {
            return BadRequest(new ApiError { Code = "FILE_TYPE_NOT_ALLOWED", Message = "Only JPG, PNG and PDF are allowed." });
        }

        var check = await dbContext.InstrumentChecks.SingleOrDefaultAsync(x => x.InstrumentCheckId == id, cancellationToken);
        if (check is null)
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Instrument check not found." });
        }

        var now = DateTime.UtcNow;
        var rootPath = Path.Combine(storageOptions.Value.ReceiptsRootPath, "instrument-checks", now.Year.ToString("0000"));
        var tmpPath = Path.Combine(storageOptions.Value.ReceiptsRootPath, "_tmp");
        Directory.CreateDirectory(rootPath);
        Directory.CreateDirectory(tmpPath);

        var stamp = now.ToString("yyyyMMdd_HHmmss");
        var guid = Guid.NewGuid().ToString("N");
        var finalFileName = $"check_{id}_{stamp}_{guid}{ext}";
        var finalPath = Path.Combine(rootPath, finalFileName);
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

        var dupe = await dbContext.InstrumentCheckAttachments.AsNoTracking()
            .AnyAsync(x => x.InstrumentCheckId == id && x.Sha256 == shaHex, cancellationToken);
        if (dupe)
        {
            try { System.IO.File.Delete(tempFilePath); } catch { /* best-effort */ }
            return Conflict(new ApiError
            {
                Code = "ATTACHMENT_DUPLICATE",
                Message = "This file has already been attached to this instrument check."
            });
        }

        try
        {
            System.IO.File.Move(tempFilePath, finalPath, overwrite: false);
        }
        catch
        {
            try { System.IO.File.Delete(tempFilePath); } catch { /* best-effort */ }
            throw;
        }

        var entity = new InstrumentCheckAttachment
        {
            InstrumentCheckId = id,
            FilePath = finalPath,
            FileName = finalFileName,
            ContentType = file.ContentType,
            FileSizeBytes = file.Length,
            Sha256 = shaHex,
            CapturedAt = now,
            CapturedBy = GetActorId()
        };

        dbContext.InstrumentCheckAttachments.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(new InstrumentCheckAttachmentListItemDto
        {
            AttachmentId = entity.AttachmentId,
            InstrumentCheckId = entity.InstrumentCheckId,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            FileSizeBytes = entity.FileSizeBytes,
            CapturedAtUtc = entity.CapturedAt
        });
    }

    [HttpGet("attachments/{attachmentId:int}")]
    public async Task<IActionResult> DownloadAttachment(int attachmentId, CancellationToken cancellationToken)
    {
        var att = await dbContext.InstrumentCheckAttachments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AttachmentId == attachmentId, cancellationToken);
        if (att is null) return NotFound();
        if (!System.IO.File.Exists(att.FilePath)) return NotFound();

        var stream = System.IO.File.OpenRead(att.FilePath);
        return File(stream, att.ContentType, att.FileName);
    }

    private int? GetActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
