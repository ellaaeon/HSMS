using System.Security.Claims;
using HSMS.Application.Audit;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/sterilizations")]
[Authorize]
public sealed class SterilizationsController(HsmsDbContext dbContext, IAuditService auditService) : ControllerBase
{
    [HttpGet("search")]
    public async Task<ActionResult<List<SterilizationSearchItemDto>>> Search(
        [FromQuery] string? cycleNo,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? status,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 200);
        var q = dbContext.Sterilizations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(cycleNo))
        {
            q = q.Where(x => x.CycleNo.StartsWith(cycleNo));
        }
        if (fromUtc.HasValue) q = q.Where(x => x.CycleDateTime >= fromUtc.Value);
        if (toUtc.HasValue) q = q.Where(x => x.CycleDateTime <= toUtc.Value);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.CycleStatus == status);

        var data = await q.OrderByDescending(x => x.CycleDateTime)
            .Take(take)
            .Select(x => new SterilizationSearchItemDto
            {
                SterilizationId = x.SterilizationId,
                CycleNo = x.CycleNo,
                CycleDateTimeUtc = x.CycleDateTime,
                SterilizerNo = dbContext.SterilizerUnits
                    .Where(u => u.SterilizerId == x.SterilizerId)
                    .Select(u => u.SterilizerNumber)
                    .FirstOrDefault() ?? x.SterilizerId.ToString(),
                CycleStatus = x.CycleStatus
            })
            .ToListAsync(cancellationToken);

        return Ok(data);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SterilizationDetailsDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var row = await dbContext.Sterilizations
            .AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Receipts)
            .SingleOrDefaultAsync(x => x.SterilizationId == id, cancellationToken);

        if (row is null)
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Sterilization cycle not found." });
        }

        return Ok(new SterilizationDetailsDto
        {
            SterilizationId = row.SterilizationId,
            CycleNo = row.CycleNo,
            SterilizerId = row.SterilizerId,
            SterilizationType = row.SterilizationType,
            CycleProgram = row.CycleProgram,
            CycleDateTimeUtc = row.CycleDateTime,
            OperatorName = row.OperatorName,
            TemperatureC = row.TemperatureC,
            Pressure = row.Pressure,
            ExposureTimeMinutes = row.ExposureTimeMinutes,
            BiLotNo = row.BiLotNo,
            BiResult = row.BiResult,
            BiResultUpdatedAtUtc = row.BiResultUpdatedAt,
            CycleStatus = row.CycleStatus,
            DoctorRoomId = row.DoctorRoomId,
            Implants = row.Implants,
            Notes = row.Notes,
            RowVersion = Convert.ToBase64String(row.RowVersion),
            Items = row.Items.Select(i => new SterilizationItemDto
            {
                SterilizationItemId = i.SterilizationItemId,
                DeptItemId = i.DeptItemId,
                DepartmentName = i.DepartmentName,
                DoctorOrRoom = i.DoctorOrRoom,
                ItemName = i.ItemName,
                Pcs = i.Pcs,
                Qty = i.Qty,
                RowVersion = Convert.ToBase64String(i.RowVersion)
            }).ToList(),
            Receipts = row.Receipts.Select(r => new ReceiptMetadataDto
            {
                ReceiptId = r.ReceiptId,
                FileName = r.FileName,
                ContentType = r.ContentType,
                FileSizeBytes = r.FileSizeBytes,
                CapturedAtUtc = r.CapturedAt
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] SterilizationUpsertDto request, CancellationToken cancellationToken)
    {
        if (SterilizationUpsertValidator.Validate(request) is { } validationError)
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = validationError });
        }

        if (await dbContext.Sterilizations.AnyAsync(x => x.CycleNo == request.CycleNo, cancellationToken))
        {
            return Conflict(new ApiError
            {
                Code = "DUPLICATE_CYCLE_NO",
                Message = "Cycle number already exists. Please open the existing record."
            });
        }

        var entity = new Sterilization
        {
            CycleNo = request.CycleNo.Trim(),
            SterilizerId = request.SterilizerId,
            SterilizationType = request.SterilizationType.Trim(),
            CycleProgram = string.IsNullOrWhiteSpace(request.CycleProgram) ? null : request.CycleProgram.Trim(),
            CycleDateTime = request.CycleDateTimeUtc,
            OperatorName = request.OperatorName.Trim(),
            TemperatureC = request.TemperatureC,
            Pressure = request.Pressure,
            ExposureTimeMinutes = request.ExposureTimeMinutes,
            BiLotNo = string.IsNullOrWhiteSpace(request.BiLotNo) ? null : request.BiLotNo.Trim(),
            BiResult = string.IsNullOrWhiteSpace(request.BiResult) ? null : request.BiResult.Trim(),
            BiTimeIn = RegisterLoadBiTimeInRules.BiTimeInUtcForCreate(request),
            LoadQty = SterilizationLoadQty.FromItems(request.Items),
            CycleStatus = request.CycleStatus,
            DoctorRoomId = request.DoctorRoomId,
            Implants = request.Implants,
            Notes = request.Notes
        };

        if (!string.IsNullOrWhiteSpace(entity.BiResult))
        {
            entity.BiResultUpdatedAt = DateTime.UtcNow;
        }

        foreach (var item in request.Items)
        {
            entity.Items.Add(new SterilizationItem
            {
                DeptItemId = item.DeptItemId,
                DepartmentName = string.IsNullOrWhiteSpace(item.DepartmentName) ? null : item.DepartmentName.Trim(),
                DoctorOrRoom = string.IsNullOrWhiteSpace(item.DoctorOrRoom) ? null : item.DoctorOrRoom.Trim(),
                ItemName = item.ItemName.Trim(),
                Pcs = Math.Max(1, item.Pcs),
                Qty = Math.Max(1, item.Qty)
            });
        }

        dbContext.Sterilizations.Add(entity);
        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditService.AppendAsync(dbContext,
                    module: AuditModules.Sterilization,
                    entityName: "tbl_sterilization",
                    entityId: entity.SterilizationId.ToString(),
                    action: AuditActions.SterilizationCreate,
                    actorAccountId: GetActorId(),
                    clientMachine: request.ClientMachine,
                    oldValues: null,
                    newValues: new { entity.CycleNo, entity.CycleStatus, items = request.Items.Count },
                    correlationId: Guid.NewGuid(),
                    cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return CreatedAtAction(nameof(GetById), new { id = entity.SterilizationId }, new
        {
            sterilizationId = entity.SterilizationId,
            cycleNo = entity.CycleNo,
            rowVersion = Convert.ToBase64String(entity.RowVersion)
        });
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<object>> Update(int id, [FromBody] SterilizationUpsertDto request, CancellationToken cancellationToken)
    {
        if (SterilizationUpsertValidator.Validate(request) is { } validationError)
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = validationError });
        }

        var entity = await dbContext.Sterilizations.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.SterilizationId == id, cancellationToken);

        if (entity is null)
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Sterilization cycle not found." });
        }

        if (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "rowVersion is required." });
        }

        var incomingVersion = Convert.FromBase64String(request.RowVersion);
        if (!entity.RowVersion.SequenceEqual(incomingVersion))
        {
            return Conflict(new ApiError
            {
                Code = "CONCURRENCY_CONFLICT",
                Message = "Someone updated this record. Press F5 to reload."
            });
        }

        var oldSnapshot = new { entity.CycleStatus, entity.OperatorName, ItemsCount = entity.Items.Count };

        entity.SterilizerId = request.SterilizerId;
        entity.SterilizationType = request.SterilizationType.Trim();
        entity.CycleProgram = string.IsNullOrWhiteSpace(request.CycleProgram) ? null : request.CycleProgram.Trim();
        entity.CycleDateTime = request.CycleDateTimeUtc;
        entity.OperatorName = request.OperatorName.Trim();
        entity.TemperatureC = request.TemperatureC;
        entity.Pressure = request.Pressure;
        entity.ExposureTimeMinutes = request.ExposureTimeMinutes;
        entity.BiLotNo = string.IsNullOrWhiteSpace(request.BiLotNo) ? null : request.BiLotNo.Trim();
        var newBiResult = string.IsNullOrWhiteSpace(request.BiResult) ? null : request.BiResult.Trim();
        if (!string.Equals(entity.BiResult, newBiResult, StringComparison.Ordinal))
        {
            entity.BiResultUpdatedAt = DateTime.UtcNow;
        }

        entity.BiResult = newBiResult;
        entity.CycleStatus = request.CycleStatus;
        entity.DoctorRoomId = request.DoctorRoomId;
        entity.Implants = request.Implants;
        entity.Notes = request.Notes;
        entity.LoadQty = SterilizationLoadQty.FromItems(request.Items);
        if (RegisterLoadBiTimeInRules.BiTimeInUtcForUpdate(entity.BiTimeIn, request) is { } biTimeInUtc)
        {
            entity.BiTimeIn = biTimeInUtc;
        }

        dbContext.SterilizationItems.RemoveRange(entity.Items);
        entity.Items = request.Items.Select(x => new SterilizationItem
        {
            SterilizationId = id,
            DeptItemId = x.DeptItemId,
            DepartmentName = string.IsNullOrWhiteSpace(x.DepartmentName) ? null : x.DepartmentName.Trim(),
            DoctorOrRoom = string.IsNullOrWhiteSpace(x.DoctorOrRoom) ? null : x.DoctorOrRoom.Trim(),
            ItemName = x.ItemName.Trim(),
            Pcs = Math.Max(1, x.Pcs),
            Qty = Math.Max(1, x.Qty)
        }).ToList();

        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditService.AppendAsync(dbContext,
                    module: AuditModules.Sterilization,
                    entityName: "tbl_sterilization",
                    entityId: id.ToString(),
                    action: AuditActions.SterilizationUpdate,
                    actorAccountId: GetActorId(),
                    clientMachine: request.ClientMachine,
                    oldValues: oldSnapshot,
                    newValues: new { entity.CycleStatus, entity.OperatorName, itemsCount = entity.Items.Count },
                    correlationId: Guid.NewGuid(),
                    cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return Ok(new { rowVersion = Convert.ToBase64String(entity.RowVersion) });
    }

    [HttpPatch("{id:int}/bi-result")]
    public async Task<ActionResult<object>> PatchBiResult(int id, [FromBody] SterilizationBiResultPatchDto request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "rowVersion is required." });
        }

        var trimmed = string.IsNullOrWhiteSpace(request.BiResult) ? null : request.BiResult.Trim();
        if (trimmed is null || !BiResultValues.IsAllowed(trimmed))
        {
            return BadRequest(new ApiError
            {
                Code = "VALIDATION_FAILED",
                Message = "BI result must be Pending, Pass, Fail, or N/A."
            });
        }

        var entity = await dbContext.Sterilizations
            .SingleOrDefaultAsync(x => x.SterilizationId == id, cancellationToken);

        if (entity is null)
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Sterilization cycle not found." });
        }

        byte[] incomingVersion;
        try
        {
            incomingVersion = Convert.FromBase64String(request.RowVersion);
        }
        catch (FormatException)
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "rowVersion is not valid Base64." });
        }

        if (!entity.RowVersion.SequenceEqual(incomingVersion))
        {
            return Conflict(new ApiError
            {
                Code = "CONCURRENCY_CONFLICT",
                Message = "Someone updated this record. Press F5 to reload."
            });
        }

        var previousBi = entity.BiResult;
        if (string.Equals(previousBi, trimmed, StringComparison.Ordinal))
        {
            return Ok(new
            {
                rowVersion = Convert.ToBase64String(entity.RowVersion),
                biResultUpdatedAtUtc = entity.BiResultUpdatedAt
            });
        }

        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var affected = await dbContext.Sterilizations
                    .Where(x => x.SterilizationId == id && x.RowVersion == incomingVersion)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.BiResult, trimmed)
                        .SetProperty(x => x.BiResultUpdatedAt, DateTime.UtcNow), cancellationToken);

                if (affected == 0)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return Conflict(new ApiError
                    {
                        Code = "CONCURRENCY_CONFLICT",
                        Message = "Someone updated this record. Press F5 to reload."
                    });
                }

                await auditService.AppendAsync(dbContext,
                    module: AuditModules.Sterilization,
                    entityName: "tbl_sterilization",
                    entityId: id.ToString(),
                    action: AuditActions.SterilizationUpdate,
                    actorAccountId: GetActorId(),
                    clientMachine: request.ClientMachine,
                    oldValues: new { biResult = previousBi },
                    newValues: new { biResult = trimmed },
                    correlationId: Guid.NewGuid(),
                    cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        var meta = await dbContext.Sterilizations.AsNoTracking()
            .Where(x => x.SterilizationId == id)
            .Select(x => new { x.RowVersion, x.BiResultUpdatedAt })
            .FirstAsync(cancellationToken);

        return Ok(new
        {
            rowVersion = Convert.ToBase64String(meta.RowVersion),
            biResultUpdatedAtUtc = meta.BiResultUpdatedAt
        });
    }

    private int? GetActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
