using System.Security.Claims;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Api.Controllers;

public sealed class QaTestUpsertDto
{
    public string? RowVersion { get; set; }
    public int SterilizationId { get; set; }
    public string TestType { get; set; } = string.Empty;
    public DateTime TestDateTimeUtc { get; set; }
    public string Result { get; set; } = string.Empty;
    public decimal? MeasuredValue { get; set; }
    public string? Unit { get; set; }
    public string? Notes { get; set; }
    public string? PerformedBy { get; set; }
    public string? ClientMachine { get; set; }
}

[ApiController]
[Route("api/qa-tests")]
[Authorize]
public sealed class QaTestsController(HsmsDbContext dbContext, IAuditService auditService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Create(QaTestUpsertDto request, CancellationToken cancellationToken)
    {
        var cycleExists = await dbContext.Sterilizations.AnyAsync(x => x.SterilizationId == request.SterilizationId, cancellationToken);
        if (!cycleExists)
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "Sterilization cycle not found." });
        }

        var entity = new QaTest
        {
            SterilizationId = request.SterilizationId,
            TestType = request.TestType,
            TestDateTime = request.TestDateTimeUtc,
            Result = request.Result,
            MeasuredValue = request.MeasuredValue,
            Unit = request.Unit,
            Notes = request.Notes,
            PerformedBy = request.PerformedBy
        };
        dbContext.QaTests.Add(entity);
        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditService.AppendAsync(dbContext, "QATest", "qa_tests", entity.QaTestId.ToString(), "Create", GetActorId(),
                    request.ClientMachine, null, new { entity.TestType, entity.Result }, Guid.NewGuid(), cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return Ok(new { qaTestId = entity.QaTestId, rowVersion = Convert.ToBase64String(entity.RowVersion) });
    }

    [HttpPut("{qaTestId:int}")]
    public async Task<ActionResult<object>> Update(int qaTestId, QaTestUpsertDto request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.QaTests.SingleOrDefaultAsync(x => x.QaTestId == qaTestId, cancellationToken);
        if (entity is null)
        {
            return NotFound(new ApiError { Code = "NOT_FOUND", Message = "QA test not found." });
        }

        if (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "rowVersion is required." });
        }

        if (!entity.RowVersion.SequenceEqual(Convert.FromBase64String(request.RowVersion)))
        {
            return Conflict(new ApiError { Code = "CONCURRENCY_CONFLICT", Message = "Someone updated this record. Press F5 to reload." });
        }

        var oldValue = new { entity.Result, entity.MeasuredValue, entity.Unit };
        entity.Result = request.Result;
        entity.MeasuredValue = request.MeasuredValue;
        entity.Unit = request.Unit;
        entity.Notes = request.Notes;
        entity.PerformedBy = request.PerformedBy;
        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditService.AppendAsync(dbContext, "QATest", "qa_tests", entity.QaTestId.ToString(), "Update", GetActorId(),
                    request.ClientMachine, oldValue, new { entity.Result, entity.MeasuredValue, entity.Unit }, Guid.NewGuid(), cancellationToken);

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

    private int? GetActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
