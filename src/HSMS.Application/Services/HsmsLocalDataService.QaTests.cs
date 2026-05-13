using HSMS.Application.Audit;
using HSMS.Persistence.Entities;
using HSMS.Shared.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    private static readonly string[] QaAllowedTestTypes = ["Leak", "BowieDick"];
    private static readonly string[] QaAllowedResults = ["Pass", "Fail"];

    public async Task<(IReadOnlyList<QaTestListItemDto> items, string? error)> ListQaTestsAsync(
        QaTestQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var take = Math.Clamp(query.Take, 1, 1000);

        var q = db.QaTests.AsNoTracking().AsQueryable();
        if (query.FromUtc.HasValue) q = q.Where(x => x.TestDateTime >= query.FromUtc.Value);
        if (query.ToUtc.HasValue) q = q.Where(x => x.TestDateTime <= query.ToUtc.Value);
        if (!string.IsNullOrWhiteSpace(query.TestType)) q = q.Where(x => x.TestType == query.TestType);
        if (!string.IsNullOrWhiteSpace(query.Result)) q = q.Where(x => x.Result == query.Result);
        if (query.PendingApprovalOnly == true) q = q.Where(x => x.ApprovedAt == null);
        if (query.SterilizationId.HasValue) q = q.Where(x => x.SterilizationId == query.SterilizationId.Value);

        var rows = await (from t in q
                          join s in db.Sterilizations.AsNoTracking() on t.SterilizationId equals s.SterilizationId
                          join u in db.SterilizerUnits.AsNoTracking() on s.SterilizerId equals u.SterilizerId into su
                          from u in su.DefaultIfEmpty()
                          join a in db.Accounts.AsNoTracking() on t.ApprovedBy equals a.AccountId into approver
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

        return (rows, null);
    }

    public async Task<(int qaTestId, string? rowVersion, string? error)> CreateQaTestAsync(
        int sterilizationId,
        string testType,
        DateTime testDateTimeUtc,
        string result,
        decimal? measuredValue,
        string? unit,
        string? notes,
        string? performedBy,
        string? clientMachine,
        CancellationToken cancellationToken = default)
    {
        if (!QaAllowedTestTypes.Contains(testType))
        {
            return (0, null, "Test type must be Leak or BowieDick.");
        }
        if (!QaAllowedResults.Contains(result))
        {
            return (0, null, "Result must be Pass or Fail.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var cycleExists = await db.Sterilizations.AnyAsync(x => x.SterilizationId == sterilizationId, cancellationToken);
        if (!cycleExists)
        {
            return (0, null, "Sterilization cycle not found.");
        }

        var entity = new QaTest
        {
            SterilizationId = sterilizationId,
            TestType = testType,
            TestDateTime = testDateTimeUtc,
            Result = result,
            MeasuredValue = measuredValue,
            Unit = string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(),
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
            PerformedBy = string.IsNullOrWhiteSpace(performedBy) ? null : performedBy.Trim()
        };
        db.QaTests.Add(entity);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);

            await auditService.AppendAsync(
                db,
                "QATest",
                "qa_tests",
                entity.QaTestId.ToString(),
                "Create",
                Actor(),
                clientMachine,
                null,
                new { entity.TestType, entity.Result, entity.SterilizationId },
                Guid.NewGuid(),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            await tx.RollbackAsync(cancellationToken);
            return (0, null, $"A {testType} test already exists for this cycle on {testDateTimeUtc:yyyy-MM-dd}.");
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return (entity.QaTestId, Convert.ToBase64String(entity.RowVersion), null);
    }

    public async Task<(string? rowVersion, string? error)> ApproveQaTestAsync(
        int qaTestId,
        string rowVersionBase64,
        string? remarks,
        string? clientMachine,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rowVersionBase64))
        {
            return (null, "rowVersion is required.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.QaTests.SingleOrDefaultAsync(x => x.QaTestId == qaTestId, cancellationToken);
        if (entity is null)
        {
            return (null, "QA test not found.");
        }

        if (!entity.RowVersion.SequenceEqual(Convert.FromBase64String(rowVersionBase64)))
        {
            return (null, "Concurrency conflict. Please refresh.");
        }

        if (entity.ApprovedAt is not null)
        {
            return (Convert.ToBase64String(entity.RowVersion), "This QA test was already approved.");
        }

        var actor = Actor();
        if (actor is null)
        {
            return (null, "Sign in required.");
        }

        var oldValue = new { entity.ApprovedAt, entity.ApprovedBy };
        entity.ApprovedBy = actor;
        entity.ApprovedAt = DateTime.UtcNow;
        entity.ApprovedRemarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await auditService.AppendAsync(
                db,
                "QATest",
                "qa_tests",
                entity.QaTestId.ToString(),
                "Approve",
                actor,
                clientMachine,
                oldValue,
                new { entity.ApprovedAt, entity.ApprovedBy, entity.ApprovedRemarks },
                Guid.NewGuid(),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return (Convert.ToBase64String(entity.RowVersion), null);
    }
}
