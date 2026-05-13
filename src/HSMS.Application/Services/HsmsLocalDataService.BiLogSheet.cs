using HSMS.Application.Audit;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    /// <summary>Updates BI log sheet QA fields (paper form) using <see cref="ExecuteUpdateAsync"/> and rowversion concurrency.</summary>
    public async Task<(bool ok, string? error, string? newRowVersion, DateTime? biResultUpdatedAtUtc)> UpdateSterilizationBiLogSheetAsync(
        int sterilizationId,
        string rowVersionBase64,
        BiLogSheetUpdatePayload payload,
        string? clientMachine,
        CancellationToken cancellationToken = default)
    {
        var normalized = BiLogSheetUpdateValidator.Normalize(payload);
        if (BiLogSheetUpdateValidator.Validate(normalized) is { } validationError)
        {
            return (false, validationError, null, null);
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

        var prev = await db.Sterilizations.AsNoTracking()
            .Where(x => x.SterilizationId == sterilizationId)
            .Select(x => new
            {
                x.CreatedBy,
                x.BiDaily,
                x.BiIncubatorTemp,
                x.BiIncubatorChecked,
                x.BiTimeIn,
                x.BiTimeOut,
                x.BiTimeInInitials,
                x.BiTimeOutInitials,
                x.BiProcessedResult24m,
                x.BiProcessedValue24m,
                x.BiProcessedResult24h,
                x.BiProcessedValue24h,
                x.BiControlResult24m,
                x.BiControlValue24m,
                x.BiControlResult24h,
                x.BiControlValue24h,
                x.Notes,
                x.RowVersion,
                x.BiResultUpdatedAt
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (prev is null)
        {
            return (false, "Sterilization cycle not found.", null, null);
        }

        if (DenyIfNotOwnerOrAdmin(prev.CreatedBy) is { } denied)
        {
            return (false, denied, null, null);
        }

        if (!prev.RowVersion.SequenceEqual(incomingVersion))
        {
            return (false,
                "Cannot save: the row changed in the database since the grid was loaded. Press Go to refresh, then try again.",
                null, null);
        }

        var prevPayload = BiLogSheetUpdateValidator.Normalize(new BiLogSheetUpdatePayload(
            prev.BiDaily,
            prev.BiIncubatorTemp,
            prev.BiIncubatorChecked,
            prev.BiTimeInInitials,
            prev.BiTimeOutInitials,
            prev.BiProcessedResult24m,
            prev.BiProcessedValue24m,
            prev.BiProcessedResult24h,
            prev.BiProcessedValue24h,
            prev.BiControlResult24m,
            prev.BiControlValue24m,
            prev.BiControlResult24h,
            prev.BiControlValue24h,
            prev.Notes,
            prev.BiTimeIn,
            prev.BiTimeOut));

        if (prevPayload == normalized)
        {
            return (true, null, Convert.ToBase64String(prev.RowVersion), prev.BiResultUpdatedAt);
        }

        var now = DateTime.UtcNow;

        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                var affected = await db.Sterilizations
                    .Where(x => x.SterilizationId == sterilizationId && x.RowVersion == incomingVersion)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(x => x.BiDaily, normalized.BiDaily)
                        .SetProperty(x => x.BiIncubatorTemp, normalized.BiIncubatorTemp)
                        .SetProperty(x => x.BiIncubatorChecked, normalized.BiIncubatorChecked)
                        .SetProperty(x => x.BiTimeIn, normalized.BiTimeInUtc)
                        .SetProperty(x => x.BiTimeOut, normalized.BiTimeOutUtc)
                        .SetProperty(x => x.BiTimeInInitials, normalized.BiTimeInInitials)
                        .SetProperty(x => x.BiTimeOutInitials, normalized.BiTimeOutInitials)
                        .SetProperty(x => x.BiProcessedResult24m, normalized.BiProcessedResult24m)
                        .SetProperty(x => x.BiProcessedValue24m, normalized.BiProcessedValue24m)
                        .SetProperty(x => x.BiProcessedResult24h, normalized.BiProcessedResult24h)
                        .SetProperty(x => x.BiProcessedValue24h, normalized.BiProcessedValue24h)
                        .SetProperty(x => x.BiControlResult24m, normalized.BiControlResult24m)
                        .SetProperty(x => x.BiControlValue24m, normalized.BiControlValue24m)
                        .SetProperty(x => x.BiControlResult24h, normalized.BiControlResult24h)
                        .SetProperty(x => x.BiControlValue24h, normalized.BiControlValue24h)
                        .SetProperty(x => x.Notes, normalized.Notes)
                        .SetProperty(x => x.BiResultUpdatedAt, now)
                        .SetProperty(x => x.UpdatedBy, Actor()), cancellationToken);

                if (affected == 0)
                {
                    await tx.RollbackAsync(cancellationToken);
                    return (false,
                        "Cannot save: the row changed in the database since the grid was loaded. Press Go to refresh, then try again.",
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
                    oldValues: new
                    {
                        prev.BiDaily,
                        prev.BiIncubatorTemp,
                        prev.BiIncubatorChecked,
                        biTimeIn = prev.BiTimeIn,
                        biTimeOut = prev.BiTimeOut,
                        prev.BiTimeInInitials,
                        prev.BiTimeOutInitials,
                        prev.BiProcessedResult24m,
                        prev.BiProcessedValue24m,
                        prev.BiProcessedResult24h,
                        prev.BiProcessedValue24h,
                        prev.BiControlResult24m,
                        prev.BiControlValue24m,
                        prev.BiControlResult24h,
                        prev.BiControlValue24h,
                        notes = AuditTextPreview(prev.Notes, 200)
                    },
                    newValues: new
                    {
                        normalized.BiDaily,
                        normalized.BiIncubatorTemp,
                        normalized.BiIncubatorChecked,
                        biTimeIn = normalized.BiTimeInUtc,
                        biTimeOut = normalized.BiTimeOutUtc,
                        normalized.BiTimeInInitials,
                        normalized.BiTimeOutInitials,
                        normalized.BiProcessedResult24m,
                        normalized.BiProcessedValue24m,
                        normalized.BiProcessedResult24h,
                        normalized.BiProcessedValue24h,
                        normalized.BiControlResult24m,
                        normalized.BiControlValue24m,
                        normalized.BiControlResult24h,
                        normalized.BiControlValue24h,
                        notes = AuditTextPreview(normalized.Notes, 200)
                    },
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
