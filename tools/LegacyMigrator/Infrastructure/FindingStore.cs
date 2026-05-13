using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Infrastructure;

internal enum FindingSeverity
{
    Info,
    Warning,
    Error,
}

internal sealed record Finding(string Entity, string? LegacyId, FindingSeverity Severity, string Code, string Message);

internal sealed class FindingStore(MigrationContext context)
{
    private readonly List<Finding> _buffer = [];

    public IReadOnlyList<Finding> All => _buffer;

    public int CountBy(FindingSeverity severity) => _buffer.Count(f => f.Severity == severity);

    public void Add(string entity, string? legacyId, FindingSeverity severity, string code, string message)
    {
        _buffer.Add(new Finding(entity, legacyId, severity, code, message));
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (context.DryRun || _buffer.Count == 0) return;
        await using var conn = context.OpenHsms();
        const string sql = @"INSERT INTO dbo.migration_findings(run_id, entity, legacy_id, severity, code, message)
VALUES(@runId, @entity, @legacyId, @severity, @code, @message)";
        foreach (var f in _buffer)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@runId", context.RunId);
            cmd.Parameters.AddWithValue("@entity", f.Entity);
            cmd.Parameters.AddWithValue("@legacyId", (object?)f.LegacyId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@severity", f.Severity.ToString());
            cmd.Parameters.AddWithValue("@code", f.Code);
            cmd.Parameters.AddWithValue("@message", f.Message);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
