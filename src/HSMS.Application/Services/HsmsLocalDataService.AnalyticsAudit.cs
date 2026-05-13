using HSMS.Application.Audit;
using HSMS.Application.Security;
using HSMS.Shared.Contracts;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<string?> AppendAnalyticsAuditAsync(
        AnalyticsAuditEventDto evt,
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } denied)
        {
            return denied.Message;
        }

        if (evt is null || string.IsNullOrWhiteSpace(evt.Action))
        {
            return "Audit action is required.";
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            await auditService.AppendAsync(
                db,
                module: AuditModules.Analytics,
                entityName: "analytics",
                entityId: evt.ReportType ?? "dashboard",
                action: evt.Action.Trim(),
                actorAccountId: Actor(),
                clientMachine: evt.ClientMachine,
                oldValues: null,
                newValues: new
                {
                    evt.Format,
                    evt.ReportType,
                    evt.Filter,
                    evt.Notes
                },
                correlationId: Guid.NewGuid(),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}

