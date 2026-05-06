using HSMS.Application.Audit;
using HSMS.Application.Services;
using HSMS.Persistence.Data;
using HSMS.Application.Security;
using HSMS.Shared.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/masters")]
[Authorize]
public sealed class MastersController(HsmsDbContext dbContext, ICurrentUserAccessor currentUserAccessor) : ControllerBase
{
    [HttpGet("sterilizers")]
    public async Task<ActionResult<List<SterilizerListItemDto>>> Sterilizers(CancellationToken cancellationToken)
    {
        try
        {
            var list = await dbContext.SterilizerUnits.AsNoTracking()
                .OrderBy(x => x.SterilizerId)
                .Select(x => new SterilizerListItemDto
                {
                    SterilizerId = x.SterilizerId,
                    SterilizerNo = x.SterilizerNumber,
                    Model = x.Model,
                    Manufacturer = x.Manufacturer,
                    PurchaseDate = x.PurchaseDate,
                    IsActive = x.IsActive
                })
                .ToListAsync(cancellationToken);

            return Ok(list);
        }
        catch (SqlException ex) when (IsMissingSterilizerColumns(ex))
        {
            return StatusCode(500, new ApiError
            {
                Code = "SCHEMA_OUTDATED",
                Message = "Database is missing sterilizer columns. Run ddl/003_hsms_sterilizer_manufacturer_purchase_date.sql and restart HSMS.Api."
            });
        }
    }

    [HttpGet("departments")]
    public async Task<ActionResult<List<DepartmentListItemDto>>> Departments(CancellationToken cancellationToken)
    {
        var list = await dbContext.Departments.AsNoTracking()
            .OrderBy(x => x.DepartmentId)
            .Select(x => new DepartmentListItemDto
            {
                DepartmentId = x.DepartmentId,
                DepartmentName = x.DepartmentName,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    [HttpGet("doctors-rooms")]
    public async Task<ActionResult<List<DoctorRoomListItemDto>>> DoctorsRooms(CancellationToken cancellationToken)
    {
        var list = await dbContext.DoctorRooms.AsNoTracking()
            .OrderBy(x => x.DoctorRoomId)
            .Select(x => new DoctorRoomListItemDto
            {
                DoctorRoomId = x.DoctorRoomId,
                DoctorName = x.DoctorName,
                Room = x.Room,
                DisplayName = string.IsNullOrWhiteSpace(x.Room)
                    ? x.DoctorName
                    : $"{x.DoctorName} / {x.Room}",
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    [HttpGet("department-items")]
    public async Task<ActionResult<List<DepartmentItemListItemDto>>> DepartmentItems(CancellationToken cancellationToken)
    {
        var list = await (from di in dbContext.DepartmentItems.AsNoTracking()
                          join d in dbContext.Departments.AsNoTracking() on di.DepartmentId equals d.DepartmentId
                          where di.IsActive && d.IsActive
                          orderby di.DeptItemId
                          select new DepartmentItemListItemDto
                          {
                              DeptItemId = di.DeptItemId,
                              DepartmentId = di.DepartmentId,
                              DepartmentName = d.DepartmentName,
                              ItemName = di.ItemName,
                              DefaultQty = di.DefaultQty
                          })
            .ToListAsync(cancellationToken);

        return Ok(list);
    }

    [HttpGet("accounts")]
    public async Task<ActionResult<List<AccountListItemDto>>> Accounts(CancellationToken cancellationToken)
    {
        var accounts = await dbContext.Accounts.AsNoTracking()
            .OrderBy(x => x.AccountId)
            .ToListAsync(cancellationToken);
        if (accounts.Count == 0)
        {
            return Ok(new List<AccountListItemDto>());
        }

        var idStrings = accounts.Select(a => a.AccountId.ToString()).ToList();
        var auditProjected = await dbContext.AuditLogs.AsNoTracking()
            .Where(a => a.EntityName == AuditEntities.Account && idStrings.Contains(a.EntityId))
            .Select(a => new { a.EntityId, a.EventAt, a.Action, a.AuditId })
            .ToListAsync(cancellationToken);
        var auditRows = auditProjected.ConvertAll(x => (x.EntityId, x.EventAt, x.Action, x.AuditId));

        var list = AccountListMapper.Map(accounts, auditRows);
        return Ok(list);
    }

    [HttpPost("sterilizers")]
    public async Task<ActionResult<SterilizerListItemDto>> CreateSterilizer([FromBody] SterilizerUpsertDto request, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var sterilizerNo = request.SterilizerNo.Trim();
        if (string.IsNullOrWhiteSpace(sterilizerNo))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Sterilizer number is required." });
        }

        var entity = new HSMS.Persistence.Entities.SterilizerUnit
        {
            SterilizerNumber = sterilizerNo,
            Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
            Manufacturer = string.IsNullOrWhiteSpace(request.Manufacturer) ? null : request.Manufacturer.Trim(),
            PurchaseDate = request.PurchaseDate,
            IsActive = true
        };
        try
        {
            dbContext.SterilizerUnits.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            return BadRequest(new ApiError
            {
                Code = "VALIDATION_FAILED",
                Message = $"Sterilizer number '{sterilizerNo}' already exists."
            });
        }
        catch (SqlException ex) when (IsMissingSterilizerColumns(ex))
        {
            return StatusCode(500, new ApiError
            {
                Code = "SCHEMA_OUTDATED",
                Message = "Database is missing sterilizer columns. Run ddl/003_hsms_sterilizer_manufacturer_purchase_date.sql and restart HSMS.Api."
            });
        }

        return Ok(new SterilizerListItemDto
        {
            SterilizerId = entity.SterilizerId,
            SterilizerNo = entity.SterilizerNumber,
            Model = entity.Model,
            Manufacturer = entity.Manufacturer,
            PurchaseDate = entity.PurchaseDate,
            IsActive = entity.IsActive
        });
    }

    [HttpPut("sterilizers/{id:int}")]
    public async Task<ActionResult> UpdateSterilizer(int id, [FromBody] SterilizerUpsertDto request, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var entity = await dbContext.SterilizerUnits.SingleOrDefaultAsync(x => x.SterilizerId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Sterilizer not found." });

        entity.SterilizerNumber = request.SterilizerNo.Trim();
        entity.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        entity.Manufacturer = string.IsNullOrWhiteSpace(request.Manufacturer) ? null : request.Manufacturer.Trim();
        entity.PurchaseDate = request.PurchaseDate;
        entity.IsActive = true;
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (SqlException ex) when (IsMissingSterilizerColumns(ex))
        {
            return StatusCode(500, new ApiError
            {
                Code = "SCHEMA_OUTDATED",
                Message = "Database is missing sterilizer columns. Run ddl/003_hsms_sterilizer_manufacturer_purchase_date.sql and restart HSMS.Api."
            });
        }
        return NoContent();
    }

    [HttpDelete("sterilizers/{id:int}")]
    public async Task<ActionResult> DeleteSterilizer(int id, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var entity = await dbContext.SterilizerUnits.SingleOrDefaultAsync(x => x.SterilizerId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Sterilizer not found." });

        entity.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("departments")]
    public async Task<ActionResult<DepartmentListItemDto>> CreateDepartment([FromBody] DepartmentUpsertDto request, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var name = request.DepartmentName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Department name is required." });
        }

        var entity = new HSMS.Persistence.Entities.Department
        {
            DepartmentCode = await GenerateDepartmentCodeAsync(name, cancellationToken),
            DepartmentName = name,
            IsActive = true
        };
        dbContext.Departments.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new DepartmentListItemDto { DepartmentId = entity.DepartmentId, DepartmentName = entity.DepartmentName, IsActive = entity.IsActive });
    }

    [HttpPut("departments/{id:int}")]
    public async Task<ActionResult> UpdateDepartment(int id, [FromBody] DepartmentUpsertDto request, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var entity = await dbContext.Departments.SingleOrDefaultAsync(x => x.DepartmentId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Department not found." });

        entity.DepartmentName = request.DepartmentName.Trim();
        entity.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("departments/{id:int}")]
    public async Task<ActionResult> DeleteDepartment(int id, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var entity = await dbContext.Departments.SingleOrDefaultAsync(x => x.DepartmentId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Department not found." });

        entity.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("doctors-rooms")]
    public async Task<ActionResult<DoctorRoomListItemDto>> CreateDoctorRoom([FromBody] DoctorRoomUpsertDto request, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var doctorName = request.DoctorName.Trim();
        if (string.IsNullOrWhiteSpace(doctorName))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Doctor name is required." });
        }

        var entity = new HSMS.Persistence.Entities.DoctorRoom
        {
            DoctorName = doctorName,
            Room = string.IsNullOrWhiteSpace(request.Room) ? null : request.Room.Trim(),
            IsActive = true
        };
        dbContext.DoctorRooms.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new DoctorRoomListItemDto
        {
            DoctorRoomId = entity.DoctorRoomId,
            DoctorName = entity.DoctorName,
            Room = entity.Room,
            DisplayName = string.IsNullOrWhiteSpace(entity.Room) ? entity.DoctorName : $"{entity.DoctorName} / {entity.Room}",
            IsActive = entity.IsActive
        });
    }

    [HttpPut("doctors-rooms/{id:int}")]
    public async Task<ActionResult> UpdateDoctorRoom(int id, [FromBody] DoctorRoomUpsertDto request, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var entity = await dbContext.DoctorRooms.SingleOrDefaultAsync(x => x.DoctorRoomId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Doctor/room not found." });

        entity.DoctorName = request.DoctorName.Trim();
        entity.Room = string.IsNullOrWhiteSpace(request.Room) ? null : request.Room.Trim();
        entity.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("doctors-rooms/{id:int}")]
    public async Task<ActionResult> DeleteDoctorRoom(int id, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var entity = await dbContext.DoctorRooms.SingleOrDefaultAsync(x => x.DoctorRoomId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Doctor/room not found." });

        entity.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("department-items")]
    public async Task<ActionResult<DepartmentItemListItemDto>> CreateDepartmentItem([FromBody] DepartmentItemUpsertDto request, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var dept = await dbContext.Departments.SingleOrDefaultAsync(x => x.DepartmentId == request.DepartmentId && x.IsActive, cancellationToken);
        if (dept is null)
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Department is required." });
        }

        var itemName = request.ItemName.Trim();
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Item name is required." });
        }

        var entity = new HSMS.Persistence.Entities.DepartmentItem
        {
            DepartmentId = request.DepartmentId,
            ItemCode = await GenerateDepartmentItemCodeAsync(request.DepartmentId, itemName, cancellationToken),
            ItemName = itemName,
            DefaultQty = request.DefaultQty,
            IsActive = true
        };
        try
        {
            dbContext.DepartmentItems.Add(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            return BadRequest(new ApiError
            {
                Code = "VALIDATION_FAILED",
                Message = "This item already exists for the selected department."
            });
        }

        return Ok(new DepartmentItemListItemDto
        {
            DeptItemId = entity.DeptItemId,
            DepartmentId = entity.DepartmentId,
            DepartmentName = dept.DepartmentName,
            ItemName = entity.ItemName,
            DefaultQty = entity.DefaultQty
        });
    }

    [HttpPut("department-items/{id:int}")]
    public async Task<ActionResult> UpdateDepartmentItem(int id, [FromBody] DepartmentItemUpsertDto request, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var entity = await dbContext.DepartmentItems.SingleOrDefaultAsync(x => x.DeptItemId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Department item not found." });

        var dept = await dbContext.Departments.SingleOrDefaultAsync(x => x.DepartmentId == request.DepartmentId && x.IsActive, cancellationToken);
        if (dept is null)
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Department is required." });
        }

        entity.DepartmentId = request.DepartmentId;
        entity.ItemName = request.ItemName.Trim();
        entity.DefaultQty = request.DefaultQty;
        entity.IsActive = true;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("department-items/{id:int}")]
    public async Task<ActionResult> DeleteDepartmentItem(int id, CancellationToken cancellationToken)
    {
        if (ForbiddenUnlessAdmin() is ActionResult denied)
        {
            return denied;
        }

        var entity = await dbContext.DepartmentItems.SingleOrDefaultAsync(x => x.DeptItemId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Department item not found." });

        entity.IsActive = false;
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private ActionResult? ForbiddenUnlessAdmin()
    {
        if (RoleAuthorization.RequireAdmin(currentUserAccessor.GetCurrentUser()) is { } denied)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiError { Code = denied.Code, Message = denied.Message });
        }

        return null;
    }

    private static bool IsMissingSterilizerColumns(SqlException ex)
    {
        return ex.Number == 207 &&
               (ex.Message.Contains("manufacturer", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("purchase_date", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<string> GenerateDepartmentCodeAsync(string name, CancellationToken cancellationToken)
    {
        var compact = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(compact)) compact = "DEPT";

        var baseCode = compact.Length > 8 ? compact[..8] : compact;
        var code = baseCode;
        var i = 1;
        while (await dbContext.Departments.AnyAsync(x => x.DepartmentCode == code, cancellationToken))
        {
            i++;
            var prefix = baseCode[..Math.Min(6, baseCode.Length)];
            code = $"{prefix}{i:00}";
        }

        return code;
    }

    private async Task<string> GenerateDepartmentItemCodeAsync(int departmentId, string itemName, CancellationToken cancellationToken)
    {
        var compact = new string(itemName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(compact)) compact = "ITEM";

        // Leave room for numeric suffix.
        var baseCode = compact.Length > 24 ? compact[..24] : compact;
        var code = baseCode;
        var i = 1;
        while (await dbContext.DepartmentItems.AnyAsync(x => x.DepartmentId == departmentId && x.ItemCode == code, cancellationToken))
        {
            i++;
            var prefix = baseCode[..Math.Min(22, baseCode.Length)];
            code = $"{prefix}{i:00}";
        }

        return code.Length > 32 ? code[..32] : code;
    }
}
