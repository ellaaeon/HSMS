using HSMS.Application.Security;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Shared.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<(IReadOnlyList<InstrumentCheckListItemDto> items, string? error)> SearchInstrumentChecksAsync(
        string? query,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } deniedAuth)
        {
            return (Array.Empty<InstrumentCheckListItemDto>(), deniedAuth.Message);
        }

        var q = (query ?? "").Trim();
        if (take <= 0) take = 200;
        if (take > 2000) take = 2000;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var src = db.InstrumentChecks.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(q))
            {
                src = src.Where(x =>
                    x.ItemName.Contains(q) ||
                    (x.SerialReference != null && x.SerialReference.Contains(q)) ||
                    x.CheckedByName.Contains(q) ||
                    (x.WitnessByName != null && x.WitnessByName.Contains(q)) ||
                    (x.Remarks != null && x.Remarks.Contains(q)));
            }

            var rows = await (from c in src.OrderByDescending(x => x.CheckedAtUtc).Take(take)
                              join a in db.Accounts.AsNoTracking() on c.WitnessApprovedBy equals a.AccountId into ap
                              from a in ap.DefaultIfEmpty()
                              select new InstrumentCheckListItemDto
                              {
                                  InstrumentCheckId = c.InstrumentCheckId,
                                  CheckedAtUtc = c.CheckedAtUtc,
                                  ItemName = c.ItemName,
                                  SerialReference = c.SerialReference,
                                  CheckedByName = c.CheckedByName,
                                  WitnessByName = c.WitnessByName,
                                  Remarks = c.Remarks,
                                  WitnessApprovedAtUtc = c.WitnessApprovedAt,
                                  WitnessApprovedBy = c.WitnessApprovedBy,
                                  WitnessApprovedByUsername = a != null ? a.Username : null,
                                  AttachmentCount = db.InstrumentCheckAttachments.AsNoTracking()
                                      .Count(x => x.InstrumentCheckId == c.InstrumentCheckId)
                              })
                .ToListAsync(cancellationToken);

            return (rows, null);
        }
        catch (SqlException ex) when (ex.Number == 208)
        {
            // Invalid object name (table missing)
            return (Array.Empty<InstrumentCheckListItemDto>(),
                "Instrument Checks table is missing. Run hsms-db/ddl/016_hsms_instrument_checks.sql on the HSMS database.");
        }
        catch (Exception ex)
        {
            return (Array.Empty<InstrumentCheckListItemDto>(), ex.Message);
        }
    }

    public async Task<(InstrumentCheckListItemDto? item, string? error)> CreateInstrumentCheckAsync(
        InstrumentCheckCreateDto payload,
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } deniedAuth)
        {
            return (null, deniedAuth.Message);
        }

        var itemName = (payload.ItemName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return (null, "Item name is required.");
        }

        var checkedBy = (payload.CheckedByName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(checkedBy))
        {
            return (null, "Checked by name is required.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = new InstrumentCheck
        {
            CheckedAtUtc = DateTime.UtcNow,
            ItemName = itemName,
            SerialReference = string.IsNullOrWhiteSpace(payload.SerialReference) ? null : payload.SerialReference.Trim(),
            CheckedByName = checkedBy,
            WitnessByName = string.IsNullOrWhiteSpace(payload.WitnessByName) ? null : payload.WitnessByName.Trim(),
            Remarks = string.IsNullOrWhiteSpace(payload.Remarks) ? null : payload.Remarks.Trim(),
            CreatedBy = Actor()
        };

        try
        {
            db.InstrumentChecks.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && sqlEx.Number == 208)
        {
            return (null, "Instrument Checks table is missing. Run hsms-db/ddl/016_hsms_instrument_checks.sql on the HSMS database.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx)
        {
            return (null, sqlEx.Message);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }

        return (new InstrumentCheckListItemDto
        {
            InstrumentCheckId = entity.InstrumentCheckId,
            CheckedAtUtc = entity.CheckedAtUtc,
            ItemName = entity.ItemName,
            SerialReference = entity.SerialReference,
            CheckedByName = entity.CheckedByName,
            WitnessByName = entity.WitnessByName,
            Remarks = entity.Remarks
        }, null);
    }
}

