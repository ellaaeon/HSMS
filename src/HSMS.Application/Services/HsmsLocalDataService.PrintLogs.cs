using HSMS.Shared.Contracts.Reporting;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<(IReadOnlyList<PrintLogRowDto> rows, string? error)> ListPrintLogsAsync(
        PrintLogQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var skip = Math.Max(0, query.Skip);
        var take = Math.Clamp(query.Take, 1, 500);

        var q = db.PrintLogs.AsNoTracking().AsQueryable();
        if (query.FromUtc is { } fromUtc) q = q.Where(x => x.PrintedAt >= fromUtc);
        if (query.ToUtc is { } toUtc) q = q.Where(x => x.PrintedAt <= toUtc);
        if (!string.IsNullOrWhiteSpace(query.ReportType)) q = q.Where(x => x.ReportType == query.ReportType);
        if (query.PrintedByAccountId is int actorId) q = q.Where(x => x.PrintedBy == actorId);
        if (query.SterilizationId is int sId) q = q.Where(x => x.SterilizationId == sId);
        if (query.QaTestId is int qId) q = q.Where(x => x.QaTestId == qId);

        if (!string.IsNullOrWhiteSpace(query.UserSearch))
        {
            var term = query.UserSearch.Trim();
            var matchedAccounts = await db.Accounts.AsNoTracking()
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
            : await db.Accounts.AsNoTracking()
                .Where(a => actorIds.Contains(a.AccountId))
                .ToDictionaryAsync(a => a.AccountId, a => a.Username, cancellationToken);

        var sterIds = page.Where(x => x.SterilizationId.HasValue).Select(x => x.SterilizationId!.Value).Distinct().ToList();
        var cycleNumbers = sterIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Sterilizations.AsNoTracking()
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

        return (rows, null);
    }
}
