using HSMS.Application.Audit;
using HSMS.Persistence.Entities;
using HSMS.Shared.Contracts;

namespace HSMS.Application.Services;

/// <summary>Builds admin account list rows with audit-derived created-at fallback and latest activity.</summary>
public static class AccountListMapper
{
    /// <summary>Rows with invalid <see cref="AccountLogin.CreatedAt"/> (e.g. EF default) use first <see cref="AuditActions.AccountCreate"/> time when present.</summary>
    private const int MinPlausibleCreatedYear = 2002;

    public static IReadOnlyList<AccountListItemDto> Map(
        IReadOnlyList<AccountLogin> accounts,
        IReadOnlyList<(string EntityId, DateTime EventAt, string Action, long AuditId)> auditRows)
    {
        var latestByEntity = new Dictionary<string, (DateTime EventAt, string Action, long AuditId)>(StringComparer.Ordinal);
        var firstCreateByEntity = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        foreach (var row in auditRows)
        {
            if (!latestByEntity.TryGetValue(row.EntityId, out var cur) ||
                row.EventAt > cur.EventAt ||
                (row.EventAt == cur.EventAt && row.AuditId > cur.AuditId))
            {
                latestByEntity[row.EntityId] = (row.EventAt, row.Action, row.AuditId);
            }

            if (row.Action == AuditActions.AccountCreate)
            {
                if (!firstCreateByEntity.TryGetValue(row.EntityId, out var first) || row.EventAt < first)
                {
                    firstCreateByEntity[row.EntityId] = row.EventAt;
                }
            }
        }

        var list = new List<AccountListItemDto>(accounts.Count);
        foreach (var x in accounts)
        {
            var key = x.AccountId.ToString();
            var createdUtc = x.CreatedAt.Year >= MinPlausibleCreatedYear
                ? x.CreatedAt
                : (firstCreateByEntity.TryGetValue(key, out var fromAudit) ? fromAudit : x.CreatedAt);

            latestByEntity.TryGetValue(key, out var last);
            var hasLast = latestByEntity.ContainsKey(key);
            var summary = hasLast ? FormatAction(last.Action) : null;
            var activityDisplay = hasLast
                ? $"{summary} · {last.EventAt:yyyy-MM-dd HH:mm} UTC"
                : "—";

            list.Add(new AccountListItemDto
            {
                AccountId = x.AccountId,
                Username = x.Username,
                Role = x.Role,
                FullName = string.IsNullOrWhiteSpace(x.FirstName) && string.IsNullOrWhiteSpace(x.LastName)
                    ? x.Username
                    : $"{x.FirstName} {x.LastName}".Trim(),
                CreatedAtUtc = createdUtc,
                IsActive = x.IsActive,
                LatestActivityAtUtc = hasLast ? last.EventAt : null,
                LatestActivitySummary = summary,
                LatestActivityDisplay = activityDisplay
            });
        }

        return list;
    }

    private static string FormatAction(string action) => action switch
    {
        AuditActions.ProfileUpdate => "Profile updated",
        AuditActions.LoginSuccess => "Signed in",
        AuditActions.AccountCreate => "Account created",
        AuditActions.LoginFailed => "Failed sign-in (audit)",
        AuditActions.LoginInactive => "Inactive sign-in attempt",
        _ => action.Replace(".", " ", StringComparison.Ordinal)
    };
}
