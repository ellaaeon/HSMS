using HSMS.LegacyMigrator.Infrastructure;
using HSMS.LegacyMigrator.Logging;
using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Steps;

/// <summary>
/// Migrates legacy sterilization cycles into dbo.tbl_sterilization (depends on Sterilizers + DoctorsRooms steps).
/// </summary>
internal sealed class SterilizationsStep : IMigrationStep
{
    public string Name => "Sterilizations";

    public async Task<StepResult> RunAsync(MigrationContext context, MappingStore mappings, FindingStore findings, CancellationToken ct)
    {
        await using var legacy = context.OpenLegacy();
        await using var hsms = context.OpenHsms();

        string? selectSql = null;
        if (await StepHelpers.TableExistsAsync(legacy, "dbo.tbl_sterilization", ct))
        {
            selectSql = @"SELECT CAST(sterilization_id AS NVARCHAR(64)) AS legacy_id,
                                 cycle_no, sterilizer_id, sterilization_type, cycle_program,
                                 cycle_datetime, operator_name, temperature_c, pressure,
                                 bi_result, cycle_status, doctor_room_id, implants, notes
                          FROM dbo.tbl_sterilization";
        }
        else if (await StepHelpers.TableExistsAsync(legacy, "dbo.sterilizations", ct))
        {
            selectSql = @"SELECT CAST(id AS NVARCHAR(64)) AS legacy_id,
                                 cycle_no, sterilizer_id, type AS sterilization_type, program AS cycle_program,
                                 cycle_datetime, operator_name, temp_c AS temperature_c, pressure,
                                 bi_result, status AS cycle_status, doctor_room_id, implants, notes
                          FROM dbo.sterilizations";
        }

        if (selectSql is null)
        {
            findings.Add(Name, null, FindingSeverity.Warning, "TABLE_NOT_FOUND", "No legacy sterilization table found; skipping.");
            return StepResult.Empty;
        }

        int read = 0, inserted = 0, skipped = 0, errors = 0;
        await using var cmd = new SqlCommand(selectSql, legacy);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            read++;
            var legacyId = reader["legacy_id"]?.ToString() ?? string.Empty;
            var cycleNo = StepHelpers.AsNullableString(reader["cycle_no"])?.Trim();
            var legacySterilizerId = StepHelpers.AsNullableInt(reader["sterilizer_id"]);
            var sterilizationType = StepHelpers.AsNullableString(reader["sterilization_type"]);
            var cycleStatus = StepHelpers.AsNullableString(reader["cycle_status"]);
            var cycleDateTime = StepHelpers.AsNullableDate(reader["cycle_datetime"]);
            var operatorName = StepHelpers.AsNullableString(reader["operator_name"]);

            if (string.IsNullOrWhiteSpace(cycleNo) || legacySterilizerId is null || cycleDateTime is null
                || string.IsNullOrWhiteSpace(sterilizationType) || string.IsNullOrWhiteSpace(cycleStatus)
                || string.IsNullOrWhiteSpace(operatorName))
            {
                findings.Add(Name, legacyId, FindingSeverity.Error, "MISSING_FIELDS",
                    "Required: cycle_no, sterilizer_id, sterilization_type, cycle_datetime, operator_name, cycle_status.");
                errors++; continue;
            }

            if (!mappings.TryGet("Sterilizers", legacySterilizerId.Value.ToString(), out var newSterilizerIdStr)
                || !int.TryParse(newSterilizerIdStr, out var newSterilizerId))
            {
                findings.Add(Name, legacyId, FindingSeverity.Error, "STERILIZER_NOT_MAPPED",
                    $"Sterilizer legacy id {legacySterilizerId} not mapped. Run Sterilizers step first.");
                errors++; continue;
            }

            int? newDoctorRoomId = null;
            var legacyDoctorRoomId = StepHelpers.AsNullableInt(reader["doctor_room_id"]);
            if (legacyDoctorRoomId is not null
                && mappings.TryGet("DoctorsRooms", legacyDoctorRoomId.Value.ToString(), out var drStr)
                && int.TryParse(drStr, out var drId))
            {
                newDoctorRoomId = drId;
            }

            if (mappings.TryGet(Name, legacyId, out _)) { skipped++; continue; }

            try
            {
                int newId = await UpsertAsync(hsms, cycleNo!, newSterilizerId, sterilizationType!,
                    StepHelpers.AsNullableString(reader["cycle_program"]),
                    cycleDateTime.Value, operatorName!,
                    StepHelpers.AsNullableDecimal(reader["temperature_c"]),
                    StepHelpers.AsNullableDecimal(reader["pressure"]),
                    StepHelpers.AsNullableString(reader["bi_result"]),
                    NormalizeStatus(cycleStatus!),
                    newDoctorRoomId,
                    Convert.ToBoolean(reader["implants"] is DBNull ? false : reader["implants"]),
                    StepHelpers.AsNullableString(reader["notes"]),
                    context.DryRun, ct);
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

    private static string NormalizeStatus(string status) => status.Trim() switch
    {
        var s when s.Equals("draft", StringComparison.OrdinalIgnoreCase) => "Draft",
        var s when s.Equals("completed", StringComparison.OrdinalIgnoreCase) => "Completed",
        var s when s.Equals("voided", StringComparison.OrdinalIgnoreCase)
                  || s.Equals("void", StringComparison.OrdinalIgnoreCase)
                  || s.Equals("cancelled", StringComparison.OrdinalIgnoreCase) => "Voided",
        _ => "Completed",
    };

    private static async Task<int> UpsertAsync(SqlConnection hsms, string cycleNo, int sterilizerId, string sterilizationType,
        string? cycleProgram, DateTime cycleDateTime, string operatorName, decimal? temperatureC, decimal? pressure,
        string? biResult, string cycleStatus, int? doctorRoomId, bool implants, string? notes, bool dryRun, CancellationToken ct)
    {
        const string findSql = "SELECT sterilization_id FROM dbo.tbl_sterilization WHERE cycle_no = @cycleNo";
        await using (var find = new SqlCommand(findSql, hsms))
        {
            find.Parameters.AddWithValue("@cycleNo", cycleNo);
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is not null and not DBNull) return Convert.ToInt32(existing);
        }
        if (dryRun) return 0;

        const string insertSql = @"INSERT INTO dbo.tbl_sterilization
            (cycle_no, sterilizer_id, sterilization_type, cycle_program, cycle_datetime, operator_name,
             temperature_c, pressure, bi_result, cycle_status, doctor_room_id, implants, notes)
            OUTPUT INSERTED.sterilization_id
            VALUES(@cycleNo, @sterilizerId, @type, @program, @datetime, @operator,
                   @temp, @pressure, @bi, @status, @doctorRoom, @implants, @notes)";
        await using var ins = new SqlCommand(insertSql, hsms);
        ins.Parameters.AddWithValue("@cycleNo", cycleNo);
        ins.Parameters.AddWithValue("@sterilizerId", sterilizerId);
        ins.Parameters.AddWithValue("@type", sterilizationType);
        ins.Parameters.AddWithValue("@program", (object?)cycleProgram ?? DBNull.Value);
        ins.Parameters.AddWithValue("@datetime", cycleDateTime);
        ins.Parameters.AddWithValue("@operator", operatorName);
        ins.Parameters.AddWithValue("@temp", (object?)temperatureC ?? DBNull.Value);
        ins.Parameters.AddWithValue("@pressure", (object?)pressure ?? DBNull.Value);
        ins.Parameters.AddWithValue("@bi", (object?)biResult ?? DBNull.Value);
        ins.Parameters.AddWithValue("@status", cycleStatus);
        ins.Parameters.AddWithValue("@doctorRoom", (object?)doctorRoomId ?? DBNull.Value);
        ins.Parameters.AddWithValue("@implants", implants);
        ins.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        var idObj = await ins.ExecuteScalarAsync(ct);
        return Convert.ToInt32(idObj);
    }
}
