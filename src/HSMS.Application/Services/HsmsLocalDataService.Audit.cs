using HSMS.Application.Audit;
using HSMS.Persistence.Data;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<(IReadOnlyList<AuditLogRowDto> rows, string? error)> GetAuditLogsAsync(
        AuditLogQueryDto query,
        CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } err)
        {
            return ([], err);
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var take = Math.Clamp(query.Take, 1, 500);

            var q =
                from a in db.AuditLogs.AsNoTracking()
                join u in db.Accounts.AsNoTracking() on a.ActorAccountId equals u.AccountId into uj
                from u in uj.DefaultIfEmpty()
                select new { a, Username = u != null ? u.Username : (string?)null };

            if (query.FromUtc is { } from)
            {
                q = q.Where(x => x.a.EventAt >= from);
            }

            if (query.ToUtc is { } to)
            {
                q = q.Where(x => x.a.EventAt <= to);
            }

            if (query.ActorAccountId is { } aid)
            {
                q = q.Where(x => x.a.ActorAccountId == aid);
            }

            if (!string.IsNullOrWhiteSpace(query.ModuleFilter))
            {
                var m = query.ModuleFilter.Trim();
                q = q.Where(x => x.a.Module.Contains(m));
            }

            if (!string.IsNullOrWhiteSpace(query.ActionFilter))
            {
                var ac = query.ActionFilter.Trim();
                q = q.Where(x => x.a.Action.Contains(ac));
            }

            if (!string.IsNullOrWhiteSpace(query.UserSearch))
            {
                var s = query.UserSearch.Trim();
                var lower = s.ToLowerInvariant();
                q = q.Where(x =>
                    (x.Username != null && x.Username.ToLower().Contains(lower))
                    || x.a.EntityId.ToLower().Contains(lower));
            }

            var rows = await q
                .OrderByDescending(x => x.a.EventAt)
                .Take(take)
                .Select(x => new AuditLogRowDto
                {
                    AuditId = x.a.AuditId,
                    EventAtUtc = x.a.EventAt,
                    ActorAccountId = x.a.ActorAccountId,
                    ActorUsername = x.Username,
                    Module = x.a.Module,
                    EntityName = x.a.EntityName,
                    EntityId = x.a.EntityId,
                    Action = x.a.Action,
                    OldValuesJson = x.a.OldValuesJson,
                    NewValuesJson = x.a.NewValuesJson,
                    ClientMachine = x.a.ClientMachine
                })
                .ToListAsync(cancellationToken);

            return (rows, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    public async Task<(IReadOnlyList<AuditSecurityAlertDto> rows, string? error)> GetAuditSecurityAlertsAsync(
        int take,
        CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } err)
        {
            return ([], err);
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var t = Math.Clamp(take, 1, 200);
            var rows = await db.AuditLogs.AsNoTracking()
                .Where(x => x.Module == AuditModules.Security &&
                            (x.Action == AuditActions.AlertMultipleFailedLogins ||
                             x.Action.Contains("Alert")))
                .OrderByDescending(x => x.EventAt)
                .Take(t)
                .Select(x => new AuditSecurityAlertDto
                {
                    AuditId = x.AuditId,
                    EventAtUtc = x.EventAt,
                    Action = x.Action,
                    DetailsJson = x.NewValuesJson ?? x.OldValuesJson,
                    ClientMachine = x.ClientMachine
                })
                .ToListAsync(cancellationToken);

            return (rows, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    public async Task<(IReadOnlyList<AccountRecentActivityDto> rows, string? error)> GetRecentlyActiveAccountsAsync(
        int withinHours,
        CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } err)
        {
            return ([], err);
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var since = DateTime.UtcNow.AddHours(-Math.Clamp(withinHours, 1, 168));
            var rows = await db.Accounts.AsNoTracking()
                .Where(x => x.LastLoginAt >= since)
                .OrderByDescending(x => x.LastLoginAt)
                .Select(x => new AccountRecentActivityDto
                {
                    AccountId = x.AccountId,
                    Username = x.Username,
                    Role = x.Role,
                    LastLoginAtUtc = x.LastLoginAt
                })
                .ToListAsync(cancellationToken);

            return (rows, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    public async Task<(IReadOnlyList<AuditVolumeRowDto> rows, string? error)> GetSterilizationUpdateVolumeAsync(
        int withinHours,
        int minUpdates,
        CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } err)
        {
            return ([], err);
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var since = DateTime.UtcNow.AddHours(-Math.Clamp(withinHours, 1, 72));
            var min = Math.Max(minUpdates, 5);

            var grouped = await db.AuditLogs.AsNoTracking()
                .Where(x => x.Action == AuditActions.SterilizationUpdate && x.EventAt >= since && x.ActorAccountId != null)
                .GroupBy(x => x.ActorAccountId!.Value)
                .Where(g => g.Count() >= min)
                .Select(g => new { ActorId = g.Key, Cnt = g.Count() })
                .ToListAsync(cancellationToken);

            if (grouped.Count == 0)
            {
                return ([], null);
            }

            var ids = grouped.Select(x => x.ActorId).ToList();
            var names = await db.Accounts.AsNoTracking()
                .Where(x => ids.Contains(x.AccountId))
                .Select(x => new { x.AccountId, x.Username })
                .ToDictionaryAsync(x => x.AccountId, x => x.Username, cancellationToken);

            var rows = grouped
                .Select(g => new AuditVolumeRowDto
                {
                    ActorAccountId = g.ActorId,
                    ActorUsername = names.GetValueOrDefault(g.ActorId),
                    UpdateCount = g.Cnt
                })
                .OrderByDescending(x => x.UpdateCount)
                .ToList();

            return (rows, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }
}
