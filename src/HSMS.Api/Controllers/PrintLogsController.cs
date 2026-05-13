using System.Security.Claims;
using System.Text.Json;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using HSMS.Shared.Contracts.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/print-logs")]
[Authorize]
public sealed class PrintLogsController(HsmsDbContext dbContext, IAuditService auditService) : ControllerBase
{
    /// <summary>
    /// Idempotent on <see cref="PrintLogCreateDto.CorrelationId"/>: retries return the existing
    /// <c>printLogId</c> with HTTP 200, never duplicating audit history.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<object>> Create(PrintLogCreateDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ReportType))
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "reportType is required." });
        }
        if (request.CorrelationId == Guid.Empty)
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "correlationId is required." });
        }
        if (request.Copies < 1 || request.Copies > 20)
        {
            return BadRequest(new ApiError { Code = "VALIDATION_FAILED", Message = "copies must be between 1 and 20." });
        }

        var existing = await dbContext.PrintLogs.AsNoTracking()
            .Where(x => x.CorrelationId == request.CorrelationId)
            .Select(x => new { x.PrintLogId, x.PrintedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            return Ok(new { printLogId = existing.PrintLogId, printedAtUtc = existing.PrintedAt, deduplicated = true });
        }

        var entity = new PrintLog
        {
            PrintedAt = DateTime.UtcNow,
            PrintedBy = GetActorId(),
            ReportType = request.ReportType,
            SterilizationId = request.SterilizationId,
            QaTestId = request.QaTestId,
            PrinterName = request.PrinterName,
            Copies = request.Copies,
            ParametersJson = request.Parameters is null ? null : JsonSerializer.Serialize(request.Parameters),
            CorrelationId = request.CorrelationId,
            ReportVersion = "questpdf-1",
            StationId = request.ClientMachine
        };

        dbContext.PrintLogs.Add(entity);
        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);

            await auditService.AppendAsync(dbContext, "Reporting", "print_logs", entity.PrintLogId.ToString(),
                "Print", GetActorId(), request.ClientMachine,
                oldValues: null,
                newValues: new { entity.ReportType, entity.Copies, entity.PrinterName, entity.SterilizationId, entity.QaTestId },
                request.CorrelationId, cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            // Race: another writer hit the unique correlation_id index between our SELECT and INSERT. Resolve by
            // re-reading and returning the original row so duplicate-prevention still holds.
            await tx.RollbackAsync(cancellationToken);
            var deduped = await dbContext.PrintLogs.AsNoTracking()
                .Where(x => x.CorrelationId == request.CorrelationId)
                .Select(x => new { x.PrintLogId, x.PrintedAt })
                .FirstAsync(cancellationToken);
            return Ok(new { printLogId = deduped.PrintLogId, printedAtUtc = deduped.PrintedAt, deduplicated = true });
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return Ok(new { printLogId = entity.PrintLogId, printedAtUtc = entity.PrintedAt, deduplicated = false });
    }

    /// <summary>Module 5 (Audit + Print Log Viewer) - server-paged list with filters.</summary>
    [HttpGet]
    public async Task<ActionResult<List<PrintLogRowDto>>> List([FromQuery] PrintLogQueryDto query, CancellationToken cancellationToken)
    {
        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 500);

        var q = dbContext.PrintLogs.AsNoTracking().AsQueryable();
        if (query.FromUtc is { } fromUtc) q = q.Where(x => x.PrintedAt >= fromUtc);
        if (query.ToUtc is { } toUtc) q = q.Where(x => x.PrintedAt <= toUtc);
        if (!string.IsNullOrWhiteSpace(query.ReportType)) q = q.Where(x => x.ReportType == query.ReportType);
        if (query.PrintedByAccountId is int actorId) q = q.Where(x => x.PrintedBy == actorId);
        if (query.SterilizationId is int sId) q = q.Where(x => x.SterilizationId == sId);
        if (query.QaTestId is int qId) q = q.Where(x => x.QaTestId == qId);

        if (!string.IsNullOrWhiteSpace(query.UserSearch))
        {
            var term = query.UserSearch.Trim();
            var matchedAccounts = await dbContext.Accounts.AsNoTracking()
                .Where(a => a.Username.Contains(term)
                    || (a.FirstName != null && a.FirstName.Contains(term))
                    || (a.LastName != null && a.LastName.Contains(term)))
                .Select(a => a.AccountId)
                .ToListAsync(cancellationToken);
            q = q.Where(x => x.PrintedBy != null && matchedAccounts.Contains(x.PrintedBy.Value));
        }

        var page = await q
            .OrderByDescending(x => x.PrintedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var actorIds = page.Where(x => x.PrintedBy.HasValue).Select(x => x.PrintedBy!.Value).Distinct().ToList();
        var accountUsernames = actorIds.Count == 0
            ? new Dictionary<int, string>()
            : await dbContext.Accounts.AsNoTracking()
                .Where(a => actorIds.Contains(a.AccountId))
                .ToDictionaryAsync(a => a.AccountId, a => a.Username, cancellationToken);

        var sterIds = page.Where(x => x.SterilizationId.HasValue).Select(x => x.SterilizationId!.Value).Distinct().ToList();
        var cycleNumbers = sterIds.Count == 0
            ? new Dictionary<int, string>()
            : await dbContext.Sterilizations.AsNoTracking()
                .Where(s => sterIds.Contains(s.SterilizationId))
                .ToDictionaryAsync(s => s.SterilizationId, s => s.CycleNo, cancellationToken);

        var rows = page.ConvertAll(p => new PrintLogRowDto
        {
            PrintLogId = p.PrintLogId,
            PrintedAtUtc = p.PrintedAt,
            PrintedByAccountId = p.PrintedBy,
            PrintedByUsername = p.PrintedBy.HasValue ? accountUsernames.GetValueOrDefault(p.PrintedBy.Value) : null,
            ReportType = p.ReportType,
            SterilizationId = p.SterilizationId,
            CycleNo = p.SterilizationId.HasValue ? cycleNumbers.GetValueOrDefault(p.SterilizationId.Value) : null,
            QaTestId = p.QaTestId,
            PrinterName = p.PrinterName,
            Copies = p.Copies,
            CorrelationId = p.CorrelationId
        });

        return Ok(rows);
    }

    private int? GetActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
