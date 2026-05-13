using HSMS.Application.Audit;
using HSMS.Application.Services;
using HSMS.Application.Security;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/masters/cycle-programs")]
[Authorize]
public sealed class CycleProgramsController(
    HsmsDbContext db,
    IAuditService auditService,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CycleProgramListItemDto>>> List(CancellationToken cancellationToken)
    {
        var rows = await db.CyclePrograms.AsNoTracking()
            .OrderBy(x => x.ProgramName)
            .Select(x => new CycleProgramListItemDto
            {
                CycleProgramId = x.CycleProgramId,
                ProgramCode = x.ProgramCode,
                ProgramName = x.ProgramName,
                SterilizationType = x.SterilizationType,
                DefaultTemperatureC = x.DefaultTemperatureC,
                DefaultPressure = x.DefaultPressure,
                DefaultExposureMinutes = x.DefaultExposureMinutes,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<CycleProgramListItemDto>> Create([FromBody] CycleProgramUpsertDto request, CancellationToken cancellationToken)
    {
        if (Forbid403() is { } denied) return denied;

        if (string.IsNullOrWhiteSpace(request.ProgramName))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Program name is required." });
        }

        var code = string.IsNullOrWhiteSpace(request.ProgramCode)
            ? GenerateCode(request.ProgramName)
            : request.ProgramCode.Trim();

        var entity = new CycleProgram
        {
            ProgramCode = code,
            ProgramName = request.ProgramName.Trim(),
            SterilizationType = string.IsNullOrWhiteSpace(request.SterilizationType) ? null : request.SterilizationType.Trim(),
            DefaultTemperatureC = request.DefaultTemperatureC,
            DefaultPressure = request.DefaultPressure,
            DefaultExposureMinutes = request.DefaultExposureMinutes,
            IsActive = true
        };

        try
        {
            db.CyclePrograms.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Program name or code already exists." });
        }

        await AppendAuditAsync("Create", entity.CycleProgramId, null,
            new { entity.ProgramCode, entity.ProgramName, entity.SterilizationType }, cancellationToken);

        return Ok(Map(entity));
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult> Update(int id, [FromBody] CycleProgramUpsertDto request, CancellationToken cancellationToken)
    {
        if (Forbid403() is { } denied) return denied;

        var entity = await db.CyclePrograms.SingleOrDefaultAsync(x => x.CycleProgramId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Cycle program not found." });

        var oldSnapshot = new { entity.ProgramName, entity.SterilizationType, entity.DefaultTemperatureC };

        entity.ProgramName = request.ProgramName.Trim();
        if (!string.IsNullOrWhiteSpace(request.ProgramCode)) entity.ProgramCode = request.ProgramCode.Trim();
        entity.SterilizationType = string.IsNullOrWhiteSpace(request.SterilizationType) ? null : request.SterilizationType.Trim();
        entity.DefaultTemperatureC = request.DefaultTemperatureC;
        entity.DefaultPressure = request.DefaultPressure;
        entity.DefaultExposureMinutes = request.DefaultExposureMinutes;
        entity.IsActive = true;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Program name or code already exists." });
        }

        await AppendAuditAsync("Update", entity.CycleProgramId, oldSnapshot,
            new { entity.ProgramName, entity.SterilizationType, entity.DefaultTemperatureC }, cancellationToken);

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<ActionResult> Disable(int id, CancellationToken cancellationToken)
    {
        if (Forbid403() is { } denied) return denied;

        var entity = await db.CyclePrograms.SingleOrDefaultAsync(x => x.CycleProgramId == id, cancellationToken);
        if (entity is null) return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Cycle program not found." });

        if (!entity.IsActive) return NoContent(); // idempotent

        entity.IsActive = false;
        entity.DisabledAt = DateTime.UtcNow;
        entity.DisabledBy = currentUser.GetCurrentUser()?.AccountId;
        await db.SaveChangesAsync(cancellationToken);

        await AppendAuditAsync("Deactivate", entity.CycleProgramId, new { wasActive = true }, new { wasActive = false }, cancellationToken);
        return NoContent();
    }

    private async Task AppendAuditAsync(string actionVerb, int entityId, object? oldValues, object? newValues, CancellationToken ct)
    {
        await auditService.AppendAsync(db, AuditModules.Masters, "tbl_cycle_programs",
            entityId.ToString(), $"Masters.CycleProgram.{actionVerb}", currentUser.GetCurrentUser()?.AccountId,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            oldValues, newValues, Guid.NewGuid(), ct);
        await db.SaveChangesAsync(ct);
    }

    private ActionResult? Forbid403()
    {
        if (RoleAuthorization.RequireAdmin(currentUser.GetCurrentUser()) is { } denied)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ApiError { Code = denied.Code, Message = denied.Message });
        }
        return null;
    }

    private static string GenerateCode(string name)
    {
        var compact = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(compact)) compact = "PROG";
        return compact.Length > 16 ? compact[..16] : compact;
    }

    private static CycleProgramListItemDto Map(CycleProgram x) => new()
    {
        CycleProgramId = x.CycleProgramId,
        ProgramCode = x.ProgramCode,
        ProgramName = x.ProgramName,
        SterilizationType = x.SterilizationType,
        DefaultTemperatureC = x.DefaultTemperatureC,
        DefaultPressure = x.DefaultPressure,
        DefaultExposureMinutes = x.DefaultExposureMinutes,
        IsActive = x.IsActive
    };
}
