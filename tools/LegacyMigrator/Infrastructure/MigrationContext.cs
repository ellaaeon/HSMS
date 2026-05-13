using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Infrastructure;

internal sealed class MigrationContext
{
    public Guid RunId { get; } = Guid.NewGuid();
    public required string LegacyConnectionString { get; init; }
    public required string HsmsConnectionString { get; init; }
    public required bool DryRun { get; init; }
    public required int BatchSize { get; init; }
    public required string LogsDirectory { get; init; }
    public required string ReportsDirectory { get; init; }

    public SqlConnection OpenLegacy()
    {
        var conn = new SqlConnection(LegacyConnectionString);
        conn.Open();
        return conn;
    }

    public SqlConnection OpenHsms()
    {
        var conn = new SqlConnection(HsmsConnectionString);
        conn.Open();
        return conn;
    }
}
