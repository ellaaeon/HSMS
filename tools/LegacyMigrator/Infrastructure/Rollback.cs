using HSMS.LegacyMigrator.Logging;
using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Infrastructure;

/// <summary>
/// Best-effort rollback strategy: removes inserted target rows for a given run_id by
/// walking migration_mappings and deleting matching new ids per entity (in dependency-safe order).
/// Designed to be safe to re-run; respects FK constraints by deleting children first.
/// Does NOT roll back receipt files on disk - operators should restore the original snapshot
/// for filesystem state (see OPERATIONS_SOP.md).
/// </summary>
internal static class Rollback
{
    private static readonly (string Entity, string Table, string Pk)[] DeletionOrder =
    {
        ("QaTests", "dbo.qa_tests", "qa_test_id"),
        ("CycleReceipts", "dbo.cycle_receipts", "receipt_id"),
        ("Sterilizations", "dbo.tbl_sterilization", "sterilization_id"),
        ("DoctorsRooms", "dbo.tbl_doctors_rooms", "doctor_room_id"),
        ("Departments", "dbo.tbl_departments", "department_id"),
        ("Sterilizers", "dbo.tbl_sterilizer_no", "sterilizer_id"),
    };

    public static async Task RunAsync(MigrationContext context, Guid runId, CancellationToken ct)
    {
        await using var conn = context.OpenHsms();
        foreach (var (entity, table, pk) in DeletionOrder)
        {
            var sql = $@"DELETE t
                         FROM {table} t
                         INNER JOIN dbo.migration_mappings m ON m.entity = @entity
                              AND m.run_id = @runId
                              AND m.new_id IS NOT NULL
                              AND TRY_CAST(m.new_id AS INT) = t.{pk};";
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@entity", entity);
            cmd.Parameters.AddWithValue("@runId", runId);
            int rows = await cmd.ExecuteNonQueryAsync(ct);
            MigrationLog.Info($"Rollback {entity}: deleted {rows} rows from {table}.");
        }

        const string clearMap = "DELETE FROM dbo.migration_mappings WHERE run_id = @runId";
        await using (var del = new SqlCommand(clearMap, conn))
        {
            del.Parameters.AddWithValue("@runId", runId);
            int rows = await del.ExecuteNonQueryAsync(ct);
            MigrationLog.Info($"Rollback: cleared {rows} mapping rows.");
        }

        const string updateRun = @"UPDATE dbo.migration_runs SET status = N'RolledBack', completed_at_utc = SYSUTCDATETIME() WHERE run_id = @runId";
        await using (var upd = new SqlCommand(updateRun, conn))
        {
            upd.Parameters.AddWithValue("@runId", runId);
            await upd.ExecuteNonQueryAsync(ct);
        }
    }
}
