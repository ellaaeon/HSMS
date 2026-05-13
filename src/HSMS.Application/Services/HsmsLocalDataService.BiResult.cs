using HSMS.Application.Audit;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    /// <summary>
    /// Updates BI result using a single SQL <c>UPDATE ... WHERE rowversion = @rv</c> via <see cref="RelationalQueryableExtensions.ExecuteUpdateAsync{TSource}"/>.
    /// Avoids tracked-entity + audit SaveChanges interactions that can surface false <see cref="DbUpdateConcurrencyException"/>s with rowversion.
    /// </summary>
    public async Task<(bool ok, string? error, string? newRowVersion, DateTime? biResultUpdatedAtUtc)> UpdateSterilizationBiResultAsync(
        int sterilizationId,
        string rowVersionBase64,
        string? biResult,
        string? clientMachine,
        CancellationToken cancellationToken = default)
    {
        var trimmed = string.IsNullOrWhiteSpace(biResult) ? null : biResult.Trim();
        if (trimmed is null || !BiResultValues.IsAllowed(trimmed))
        {
            return (false, "Cannot save: invalid input — BI result must be Pending, Pass, Fail, or N/A.", null, null);
        }

        if (string.IsNullOrWhiteSpace(rowVersionBase64))
        {
            return (false, "Cannot save: row version is missing. Press Go to refresh the BI log.", null, null);
        }

        byte[] incomingVersion;
        try
        {
            incomingVersion = Convert.FromBase64String(rowVersionBase64);
        }
        catch (FormatException)
        {
            return (false, "Cannot save: invalid row version. Press Go to refresh the BI log.", null, null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var ownerMeta = await db.Sterilizations.AsNoTracking()
            .Where(x => x.SterilizationId == sterilizationId)
            .Select(x => new { x.CreatedBy })
            .SingleOrDefaultAsync(cancellationToken);
        if (ownerMeta is null)
        {
            return (false, "Sterilization cycle not found.", null, null);
        }
        if (DenyIfNotOwnerOrAdmin(ownerMeta.CreatedBy) is { } denied)
        {
            return (false, denied, null, null);
        }

        var previousBi = await db.Sterilizations.AsNoTracking()
            .Where(x => x.SterilizationId == sterilizationId)
            .Select(x => x.BiResult)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(previousBi, trimmed, StringComparison.Ordinal))
        {
            var unchanged = await db.Sterilizations.AsNoTracking()
                .Where(x => x.SterilizationId == sterilizationId)
                .Select(x => new { x.RowVersion, x.BiResultUpdatedAt })
                .FirstAsync(cancellationToken);
            return (true, null, Convert.ToBase64String(unchanged.RowVersion), unchanged.BiResultUpdatedAt);
        }

        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var affected = await db.Sterilizations
                    .Where(x => x.SterilizationId == sterilizationId && x.RowVersion == incomingVersion)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.BiResult, trimmed)
                        .SetProperty(x => x.BiResultUpdatedAt, DateTime.UtcNow)
                        .SetProperty(x => x.UpdatedBy, Actor()), cancellationToken);

                if (affected == 0)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return (false,
                        "Cannot save BI result: the row changed in the database since the grid was loaded. Press Go to refresh, then try again.",
                        null, null);
                }

                await auditService.AppendAsync(
                    db,
                    module: AuditModules.Sterilization,
                    entityName: "tbl_sterilization",
                    entityId: sterilizationId.ToString(),
                    action: AuditActions.SterilizationUpdate,
                    actorAccountId: Actor(),
                    clientMachine: clientMachine,
                    oldValues: new { biResult = previousBi },
                    newValues: new { biResult = trimmed },
                    correlationId: Guid.NewGuid(),
                    cancellationToken);

                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync(cancellationToken);
                return (false, DescribeSterilizationSaveError(ex), null, null);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        var meta = await db.Sterilizations.AsNoTracking()
            .Where(x => x.SterilizationId == sterilizationId)
            .Select(x => new { x.RowVersion, x.BiResultUpdatedAt })
            .FirstAsync(cancellationToken);

        return (true, null, Convert.ToBase64String(meta.RowVersion), meta.BiResultUpdatedAt);
    }
}
