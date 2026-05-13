using System.Text.Json;
using HSMS.Application.Security;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<(IReadOnlyList<SterilizationQaPresetListItemDto> items, string? error)> ListSterilizationQaPresetsAsync(
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } denied)
        {
            return ([], denied.Message);
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var me = Actor() ?? throw new InvalidOperationException("Authenticated user id is missing.");
            var rows = await db.SterilizationQaPresets.AsNoTracking()
                .Where(x => x.AccountId == me)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Name)
                .Select(x => new SterilizationQaPresetListItemDto
                {
                    PresetId = x.PresetId,
                    Name = x.Name,
                    IsDefault = x.IsDefault,
                    CreatedAtUtc = x.CreatedAt,
                    UpdatedAtUtc = x.UpdatedAt
                })
                .ToListAsync(cancellationToken);

            return (rows, null);
        }
        catch (Exception ex)
        {
            return ([], ex.Message);
        }
    }

    public async Task<(SterilizationQaPresetDto? preset, string? error)> GetSterilizationQaPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } denied)
        {
            return (null, denied.Message);
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var me = Actor() ?? throw new InvalidOperationException("Authenticated user id is missing.");
            var row = await db.SterilizationQaPresets.AsNoTracking()
                .Where(x => x.PresetId == presetId && x.AccountId == me)
                .SingleOrDefaultAsync(cancellationToken);

            if (row is null) return (null, "Preset not found.");
            return (DeserializeQaPreset(row), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(SterilizationQaPresetDto? preset, string? error)> UpsertSterilizationQaPresetAsync(
        int? presetId,
        SterilizationQaPresetUpsertDto payload,
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } denied)
        {
            return (null, denied.Message);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Name))
        {
            return (null, "Preset name is required.");
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var me = Actor() ?? throw new InvalidOperationException("Authenticated user id is missing.");
            var name = payload.Name.Trim();
            if (name.Length > 128) name = name[..128];

            SterilizationQaPreset entity;
            if (presetId is int id)
            {
                entity = await db.SterilizationQaPresets
                    .Where(x => x.PresetId == id && x.AccountId == me)
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Preset not found.");
            }
            else
            {
                entity = new SterilizationQaPreset { AccountId = me };
                db.SterilizationQaPresets.Add(entity);
            }

            entity.Name = name;
            entity.PresetJson = SerializeQaPresetJson(payload);
            entity.UpdatedAt = DateTime.UtcNow;
            if (entity.CreatedAt == default) entity.CreatedAt = entity.UpdatedAt;

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (payload.SetAsDefault)
                {
                    await db.SterilizationQaPresets
                        .Where(x => x.AccountId == me && x.IsDefault)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, false), cancellationToken);
                    entity.IsDefault = true;
                }

                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }

            var saved = await db.SterilizationQaPresets.AsNoTracking()
                .Where(x => x.AccountId == me && x.Name == entity.Name)
                .OrderByDescending(x => x.PresetId)
                .FirstAsync(cancellationToken);

            return (DeserializeQaPreset(saved), null);
        }
        catch (DbUpdateException ex)
        {
            return (null, $"Could not save preset: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<string?> DeleteSterilizationQaPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } denied)
        {
            return denied.Message;
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var me = Actor() ?? throw new InvalidOperationException("Authenticated user id is missing.");
            var row = await db.SterilizationQaPresets
                .Where(x => x.PresetId == presetId && x.AccountId == me)
                .SingleOrDefaultAsync(cancellationToken);
            if (row is null) return "Preset not found.";

            db.SterilizationQaPresets.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<string?> SetDefaultSterilizationQaPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } denied)
        {
            return denied.Message;
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var me = Actor() ?? throw new InvalidOperationException("Authenticated user id is missing.");
            var exists = await db.SterilizationQaPresets.AsNoTracking()
                .AnyAsync(x => x.PresetId == presetId && x.AccountId == me, cancellationToken);
            if (!exists) return "Preset not found.";

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await db.SterilizationQaPresets
                    .Where(x => x.AccountId == me && x.IsDefault)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, false), cancellationToken);
                await db.SterilizationQaPresets
                    .Where(x => x.AccountId == me && x.PresetId == presetId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, true), cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }

            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<(SterilizationQaPresetDto? preset, string? error)> GetDefaultSterilizationQaPresetAsync(
        CancellationToken cancellationToken = default)
    {
        if (RoleAuthorization.RequireAuthenticated(User()) is { } denied)
        {
            return (null, denied.Message);
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var me = Actor() ?? throw new InvalidOperationException("Authenticated user id is missing.");
            var row = await db.SterilizationQaPresets.AsNoTracking()
                .Where(x => x.AccountId == me && x.IsDefault)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            return row is null ? (null, null) : (DeserializeQaPreset(row), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static string SerializeQaPresetJson(SterilizationQaPresetUpsertDto payload)
    {
        var doc = new
        {
            query = payload.Query
        };
        return JsonSerializer.Serialize(doc);
    }

    private static SterilizationQaPresetDto DeserializeQaPreset(SterilizationQaPreset row)
    {
        var q = new SterilizationQaRecordQueryDto();
        try
        {
            using var doc = JsonDocument.Parse(row.PresetJson ?? "{}");
            if (doc.RootElement.TryGetProperty("query", out var qEl))
            {
                var parsed = qEl.Deserialize<SterilizationQaRecordQueryDto>();
                if (parsed is not null) q = parsed;
            }
        }
        catch
        {
            q = new SterilizationQaRecordQueryDto();
        }

        return new SterilizationQaPresetDto
        {
            PresetId = row.PresetId,
            Name = row.Name,
            IsDefault = row.IsDefault,
            Query = q,
            CreatedAtUtc = row.CreatedAt,
            UpdatedAtUtc = row.UpdatedAt
        };
    }
}

