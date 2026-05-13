using HSMS.LegacyMigrator.Infrastructure;
using HSMS.LegacyMigrator.Logging;
using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Steps;

internal sealed class DoctorsRoomsStep : IMigrationStep
{
    public string Name => "DoctorsRooms";

    public async Task<StepResult> RunAsync(MigrationContext context, MappingStore mappings, FindingStore findings, CancellationToken ct)
    {
        await using var legacy = context.OpenLegacy();
        await using var hsms = context.OpenHsms();

        string? selectSql = null;
        if (await StepHelpers.TableExistsAsync(legacy, "dbo.tbl_doctors_rooms", ct))
        {
            selectSql = "SELECT CAST(doctor_room_id AS NVARCHAR(64)) AS legacy_id, doctor_name, room FROM dbo.tbl_doctors_rooms";
        }
        else if (await StepHelpers.TableExistsAsync(legacy, "dbo.doctors_rooms", ct))
        {
            selectSql = "SELECT CAST(id AS NVARCHAR(64)) AS legacy_id, doctor AS doctor_name, room FROM dbo.doctors_rooms";
        }

        if (selectSql is null)
        {
            findings.Add(Name, null, FindingSeverity.Warning, "TABLE_NOT_FOUND", "No legacy doctors_rooms table found; skipping.");
            return StepResult.Empty;
        }

        int read = 0, inserted = 0, skipped = 0, errors = 0;
        await using var cmd = new SqlCommand(selectSql, legacy);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            read++;
            var legacyId = reader["legacy_id"]?.ToString() ?? string.Empty;
            var doctor = StepHelpers.AsNullableString(reader["doctor_name"])?.Trim();
            var room = StepHelpers.AsNullableString(reader["room"])?.Trim();
            if (string.IsNullOrWhiteSpace(doctor))
            {
                findings.Add(Name, legacyId, FindingSeverity.Error, "MISSING_DOCTOR", "doctor_name is required.");
                errors++; continue;
            }
            if (mappings.TryGet(Name, legacyId, out _)) { skipped++; continue; }

            try
            {
                int newId = await UpsertAsync(hsms, doctor!, room, context.DryRun, ct);
                await mappings.RecordAsync(Name, legacyId, newId.ToString(), context.DryRun ? "Planned" : "Imported", ct);
                inserted++;
            }
            catch (Exception ex)
            {
                MigrationLog.Error($"[{Name}] legacy={legacyId}: {ex.Message}");
                findings.Add(Name, legacyId, FindingSeverity.Error, "INSERT_FAILED", ex.Message);
                errors++;
            }
        }
        return new StepResult(read, inserted, skipped, errors);
    }

    private static async Task<int> UpsertAsync(SqlConnection hsms, string doctor, string? room, bool dryRun, CancellationToken ct)
    {
        const string findSql = @"SELECT doctor_room_id FROM dbo.tbl_doctors_rooms
                                 WHERE doctor_name = @d AND ISNULL(room, N'') = ISNULL(@r, N'')";
        await using (var find = new SqlCommand(findSql, hsms))
        {
            find.Parameters.AddWithValue("@d", doctor);
            find.Parameters.AddWithValue("@r", (object?)room ?? DBNull.Value);
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is not null and not DBNull) return Convert.ToInt32(existing);
        }
        if (dryRun) return 0;

        const string insertSql = @"INSERT INTO dbo.tbl_doctors_rooms(doctor_name, room)
                                   OUTPUT INSERTED.doctor_room_id
                                   VALUES(@d, @r)";
        await using var ins = new SqlCommand(insertSql, hsms);
        ins.Parameters.AddWithValue("@d", doctor);
        ins.Parameters.AddWithValue("@r", (object?)room ?? DBNull.Value);
        var idObj = await ins.ExecuteScalarAsync(ct);
        return Convert.ToInt32(idObj);
    }
}
