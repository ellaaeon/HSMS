using HSMS.Persistence.Data;
using HSMS.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Audit;

public static class LoginAuditHelper
{
    public static async Task LogFailedAttemptAsync(
        HsmsDbContext db,
        IAuditService auditService,
        string username,
        string? clientMachine,
        string reason,
        CancellationToken cancellationToken)
    {
        var trimmed = (username ?? string.Empty).Trim();
        var entityKey = trimmed.ToLowerInvariant();
        await auditService.AppendAsync(
            db,
            AuditModules.Security,
            AuditEntities.LoginAttempt,
            entityKey,
            AuditActions.LoginFailed,
            actorAccountId: null,
            clientMachine,
            oldValues: null,
            newValues: new { reason, attemptedUsername = trimmed },
            correlationId: Guid.NewGuid(),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        var since = DateTime.UtcNow.AddMinutes(-15);
        var fails = await db.AuditLogs.CountAsync(
            x => x.Action == AuditActions.LoginFailed
                 && x.EntityName == AuditEntities.LoginAttempt
                 && x.EntityId == entityKey
                 && x.EventAt >= since,
            cancellationToken);

        if (fails is 3 or 10)
        {
            await auditService.AppendAsync(
                db,
                AuditModules.Security,
                entityName: "security",
                entityId: entityKey,
                action: AuditActions.AlertMultipleFailedLogins,
                actorAccountId: null,
                clientMachine,
                oldValues: null,
                newValues: new { attemptedUsername = trimmed, failuresLast15Minutes = fails, windowMinutes = 15 },
                correlationId: Guid.NewGuid(),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
