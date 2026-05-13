using System.Security.Claims;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
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
    private static readonly string[] AllowedTestTypes = ["Leak", "BowieDick"];
    private static readonly string[] AllowedResults = ["Pass", "Fail"];

    [HttpGet]
    public async Task<ActionResult<List<QaTestListItemDto>>> List([FromQuery] QaTestQueryDto query, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(query.Take, 1, 1000);
        var q = dbContext.QaTests.AsNoTracking().AsQueryable();
        if (query.FromUtc.HasValue) q = q.Where(x => x.TestDateTime >= query.FromUtc.Value);
        if (query.ToUtc.HasValue) q = q.Where(x => x.TestDateTime <= query.ToUtc.Value);
        if (!string.IsNullOrWhiteSpace(query.TestType)) q = q.Where(x => x.TestType == query.TestType);
        if (!string.IsNullOrWhiteSpace(query.Result)) q = q.Where(x => x.Result == query.Result);
        if (query.PendingApprovalOnly == true) q = q.Where(x => x.ApprovedAt == null);
        if (query.SterilizationId.HasValue) q = q.Where(x => x.SterilizationId == query.SterilizationId.Value);

        var rows = await (from t in q
                          join s in dbContext.Sterilizations.AsNoTracking() on t.SterilizationId equals s.SterilizationId
                          join u in dbContext.SterilizerUnits.AsNoTracking() on s.SterilizerId equals u.SterilizerId into su
                          from u in su.DefaultIfEmpty()
                          join a in dbContext.Accounts.AsNoTracking() on t.ApprovedBy equals a.AccountId into approver
                          from a in approver.DefaultIfEmpty()
                          orderby t.TestDateTime descending
                          select new QaTestListItemDto
                          {
                              QaTestId = t.QaTestId,
                              SterilizationId = t.SterilizationId,
                              CycleNo = s.CycleNo,
                              SterilizerNo = u != null ? u.SterilizerNumber : "",
                              TestType = t.TestType,
                              TestDateTimeUtc = t.TestDateTime,
                              Result = t.Result,
                              MeasuredValue = t.MeasuredValue,
                              Unit = t.Unit,
                              PerformedBy = t.PerformedBy,
                              ApprovedBy = t.ApprovedBy,
                              ApprovedByUsername = a != null ? a.Username : null,
                              ApprovedAtUtc = t.ApprovedAt,
                              ApprovedRemarks = t.ApprovedRemarks,
                              RowVersion = Convert.ToBase64String(t.RowVersion)
                          })
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create(QaTestUpsertDto request, CancellationToken cancellationToken)
    {
        if (!AllowedTestTypes.Contains(request.TestType))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Test type must be Leak or BowieDick." });
        }
        if (!AllowedResults.Contains(request.Result))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "Result must be Pass or Fail." });
        }

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
            catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
            {
                await tx.RollbackAsync(cancellationToken);
                return Conflict(new ApiError
                {
                    Code = "DUPLICATE_QA_TEST",
                    Message = $"A {request.TestType} test already exists for this cycle on {request.TestDateTimeUtc:yyyy-MM-dd}."
                });
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

    [HttpPost("{qaTestId:int}/approve")]
    public async Task<ActionResult<object>> Approve(int qaTestId, QaTestApproveDto request, CancellationToken cancellationToken)
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

        if (entity.ApprovedAt is not null)
        {
            return Conflict(new ApiError { Code = "ALREADY_APPROVED", Message = "This QA test was already approved." });
        }

        var actor = GetActorId();
        if (actor is null)
        {
            return Unauthorized(new ApiError { Code = "UNAUTHORIZED", Message = "Sign in required." });
        }

        var oldValue = new { entity.ApprovedAt, entity.ApprovedBy };
        entity.ApprovedBy = actor;
        entity.ApprovedAt = DateTime.UtcNow;
        entity.ApprovedRemarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await auditService.AppendAsync(dbContext, "QATest", "qa_tests", entity.QaTestId.ToString(), "Approve", actor,
                request.ClientMachine, oldValue, new { entity.ApprovedAt, entity.ApprovedBy, entity.ApprovedRemarks }, Guid.NewGuid(), cancellationToken);
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
            rowVersion = Convert.ToBase64String(entity.RowVersion),
            approvedAt = entity.ApprovedAt,
            approvedBy = entity.ApprovedBy
        });
    }

    private int? GetActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
