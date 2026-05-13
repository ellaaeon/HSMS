using HSMS.Application.Audit;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<(bool ok, string? error, string? newRowVersion)> UpdateSterilizationCycleEndAsync(
        int sterilizationId,
        SterilizationCycleEndPatchDto payload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(payload.RowVersion))
        {
            return (false, "Cannot save cycle end: row version is missing. Refresh the list.", null);
        }

        byte[] incomingVersion;
        try
        {
            incomingVersion = Convert.FromBase64String(payload.RowVersion);
        }
        catch (FormatException)
        {
            return (false, "Cannot save cycle end: invalid row version. Refresh the list.", null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var snapshot = await db.Sterilizations.AsNoTracking()
            .Where(x => x.SterilizationId == sterilizationId)
            .Select(x => new { x.CycleTimeOut, x.RowVersion, x.CreatedBy })
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            return (false, "Sterilization cycle not found.", null);
        }

        if (DenyIfNotOwnerOrAdmin(snapshot.CreatedBy) is { } denied)
        {
            return (false, denied, null);
        }

        var newOut = payload.CycleEndUtc;
        if (snapshot.CycleTimeOut == newOut)
        {
            return (true, null, Convert.ToBase64String(snapshot.RowVersion));
        }

        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var affected = await db.Sterilizations
                    .Where(x => x.SterilizationId == sterilizationId && x.RowVersion == incomingVersion)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.CycleTimeOut, newOut)
                        .SetProperty(x => x.UpdatedBy, Actor()), cancellationToken);

                if (affected == 0)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return (false,
                        "Cannot save cycle end: this row changed since the list was loaded. Press Refresh, then try again.",
                        null);
                }

                await auditService.AppendAsync(
                    db,
                    module: AuditModules.Sterilization,
                    entityName: "tbl_sterilization",
                    entityId: sterilizationId.ToString(),
                    action: AuditActions.SterilizationUpdate,
                    actorAccountId: Actor(),
                    clientMachine: payload.ClientMachine,
                    oldValues: new { cycleTimeOut = snapshot.CycleTimeOut },
                    newValues: new { cycleTimeOut = newOut },
                    correlationId: Guid.NewGuid(),
                    cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync(cancellationToken);
                return (false, DescribeSterilizationSaveError(ex), null);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        var meta = await db.Sterilizations.AsNoTracking()
            .Where(x => x.SterilizationId == sterilizationId)
            .Select(x => x.RowVersion)
            .FirstAsync(cancellationToken);

        return (true, null, Convert.ToBase64String(meta));
    }
}
