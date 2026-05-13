using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Infrastructure;

/// <summary>
/// Central idempotency layer: maps legacy entity IDs to new HSMS IDs.
/// All mapping reads/writes go through this class so re-runs become no-ops.
/// </summary>
internal sealed class MappingStore(MigrationContext context)
{
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task LoadAsync(CancellationToken ct = default)
    {
        await using var conn = context.OpenHsms();
        const string sql = "SELECT entity, legacy_id, new_id FROM dbo.migration_mappings WHERE new_id IS NOT NULL";
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var entity = reader.GetString(0);
            var legacyId = reader.GetString(1);
            var newId = reader.IsDBNull(2) ? null : reader.GetString(2);
            if (newId is null) continue;
            if (!_cache.TryGetValue(entity, out var bucket))
            {
                bucket = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _cache[entity] = bucket;
            }
            bucket[legacyId] = newId;
        }
    }

    public bool TryGet(string entity, string legacyId, out string newId)
    {
        if (_cache.TryGetValue(entity, out var bucket) && bucket.TryGetValue(legacyId, out var v))
        {
            newId = v;
            return true;
        }
        newId = string.Empty;
        return false;
    }

    public async Task RecordAsync(string entity, string legacyId, string? newId, string action, CancellationToken ct = default)
    {
        if (!_cache.TryGetValue(entity, out var bucket))
        {
            bucket = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _cache[entity] = bucket;
        }
        if (newId is not null) bucket[legacyId] = newId;

        if (context.DryRun) return;

        await using var conn = context.OpenHsms();
        const string sql = @"
MERGE dbo.migration_mappings AS t
USING (SELECT @entity AS entity, @legacyId AS legacy_id) AS s
ON (t.entity = s.entity AND t.legacy_id = s.legacy_id)
WHEN MATCHED THEN UPDATE SET new_id = @newId, action = @action, run_id = @runId
WHEN NOT MATCHED THEN INSERT(run_id, entity, legacy_id, new_id, action) VALUES(@runId, @entity, @legacyId, @newId, @action);";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@runId", context.RunId);
        cmd.Parameters.AddWithValue("@entity", entity);
        cmd.Parameters.AddWithValue("@legacyId", legacyId);
        cmd.Parameters.AddWithValue("@newId", (object?)newId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@action", action);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
