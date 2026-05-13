using System.Text.Json;
using HSMS.Application.Security;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<(IReadOnlyList<AnalyticsPresetListItemDto> items, string? error)> ListAnalyticsPresetsAsync(
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
            var rows = await db.AnalyticsPresets.AsNoTracking()
                .Where(x => x.AccountId == me)
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.Name)
                .Select(x => new AnalyticsPresetListItemDto
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

    public async Task<(AnalyticsPresetDto? preset, string? error)> GetAnalyticsPresetAsync(
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
            var row = await db.AnalyticsPresets.AsNoTracking()
                .Where(x => x.PresetId == presetId && x.AccountId == me)
                .SingleOrDefaultAsync(cancellationToken);

            if (row is null)
            {
                return (null, "Preset not found.");
            }

            return (DeserializePreset(row), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(AnalyticsPresetDto? preset, string? error)> UpsertAnalyticsPresetAsync(
        int? presetId,
        AnalyticsPresetUpsertDto payload,
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

            AnalyticsPreset entity;
            if (presetId is int id)
            {
                entity = await db.AnalyticsPresets
                    .Where(x => x.PresetId == id && x.AccountId == me)
                    .SingleOrDefaultAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Preset not found.");
            }
            else
            {
                entity = new AnalyticsPreset { AccountId = me };
                db.AnalyticsPresets.Add(entity);
            }

            entity.Name = name;
            entity.PresetJson = SerializePresetJson(payload);
            entity.UpdatedAt = DateTime.UtcNow;
            if (entity.CreatedAt == default) entity.CreatedAt = entity.UpdatedAt;

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                if (payload.SetAsDefault)
                {
                    await db.AnalyticsPresets
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

            var saved = await db.AnalyticsPresets.AsNoTracking()
                .Where(x => x.AccountId == me && x.Name == entity.Name)
                .OrderByDescending(x => x.PresetId)
                .FirstAsync(cancellationToken);

            return (DeserializePreset(saved), null);
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

    public async Task<string?> DeleteAnalyticsPresetAsync(
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
            var row = await db.AnalyticsPresets
                .Where(x => x.PresetId == presetId && x.AccountId == me)
                .SingleOrDefaultAsync(cancellationToken);

            if (row is null)
            {
                return "Preset not found.";
            }

            db.AnalyticsPresets.Remove(row);
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<string?> SetDefaultAnalyticsPresetAsync(
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
            var exists = await db.AnalyticsPresets.AsNoTracking()
                .AnyAsync(x => x.PresetId == presetId && x.AccountId == me, cancellationToken);
            if (!exists)
            {
                return "Preset not found.";
            }

            await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await db.AnalyticsPresets
                    .Where(x => x.AccountId == me && x.IsDefault)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, false), cancellationToken);
                await db.AnalyticsPresets
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

    public async Task<(AnalyticsPresetDto? preset, string? error)> GetDefaultAnalyticsPresetAsync(
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
            var row = await db.AnalyticsPresets.AsNoTracking()
                .Where(x => x.AccountId == me && x.IsDefault)
                .OrderByDescending(x => x.UpdatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            return row is null ? (null, null) : (DeserializePreset(row), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private static string SerializePresetJson(AnalyticsPresetUpsertDto payload)
    {
        var doc = new
        {
            query = payload.Query,
            chartPreferences = payload.ChartPreferences,
            breakdowns = payload.Breakdowns
        };
        return JsonSerializer.Serialize(doc);
    }

    private static AnalyticsPresetDto DeserializePreset(AnalyticsPreset row)
    {
        AnalyticsDashboardQueryDto? query = null;
        AnalyticsChartPreferencesDto? chart = null;
        AnalyticsBreakdownsSelectionDto? breakdowns = null;
        try
        {
            using var doc = JsonDocument.Parse(row.PresetJson ?? "{}");
            if (doc.RootElement.TryGetProperty("query", out var q))
            {
                query = q.Deserialize<AnalyticsDashboardQueryDto>();
            }
            if (doc.RootElement.TryGetProperty("chartPreferences", out var c))
            {
                chart = c.Deserialize<AnalyticsChartPreferencesDto>();
            }
            if (doc.RootElement.TryGetProperty("breakdowns", out var b))
            {
                breakdowns = b.Deserialize<AnalyticsBreakdownsSelectionDto>();
            }
        }
        catch
        {
            // Ignore and fall back to defaults.
        }

        return new AnalyticsPresetDto
        {
            PresetId = row.PresetId,
            Name = row.Name,
            IsDefault = row.IsDefault,
            CreatedAtUtc = row.CreatedAt,
            UpdatedAtUtc = row.UpdatedAt,
            Query = query ?? new AnalyticsDashboardQueryDto(),
            ChartPreferences = chart,
            Breakdowns = breakdowns
        };
    }
}

