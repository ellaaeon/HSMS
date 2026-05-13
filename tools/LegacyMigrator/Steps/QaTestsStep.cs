using HSMS.LegacyMigrator.Infrastructure;
using HSMS.LegacyMigrator.Logging;
using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Steps;

internal sealed class QaTestsStep : IMigrationStep
{
    public string Name => "QaTests";

    public async Task<StepResult> RunAsync(MigrationContext context, MappingStore mappings, FindingStore findings, CancellationToken ct)
    {
        await using var legacy = context.OpenLegacy();
        await using var hsms = context.OpenHsms();

        if (!await StepHelpers.TableExistsAsync(legacy, "dbo.qa_tests", ct))
        {
            findings.Add(Name, null, FindingSeverity.Warning, "TABLE_NOT_FOUND", "Legacy qa_tests table not found; skipping.");
            return StepResult.Empty;
        }

        const string selectSql = @"SELECT CAST(qa_test_id AS NVARCHAR(64)) AS legacy_id,
                                        sterilization_id, test_type, test_datetime, result,
                                        measured_value, unit, notes, performed_by
                                 FROM dbo.qa_tests";

        int read = 0, inserted = 0, skipped = 0, errors = 0;
        await using var cmd = new SqlCommand(selectSql, legacy);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            read++;
            var legacyId = reader["legacy_id"]?.ToString() ?? string.Empty;
            var legacyCycleId = StepHelpers.AsNullableInt(reader["sterilization_id"]);
            var testType = StepHelpers.AsNullableString(reader["test_type"]);
            var testDatetime = StepHelpers.AsNullableDate(reader["test_datetime"]);
            var result = StepHelpers.AsNullableString(reader["result"]);

            if (legacyCycleId is null || string.IsNullOrWhiteSpace(testType) || testDatetime is null || string.IsNullOrWhiteSpace(result))
            {
                findings.Add(Name, legacyId, FindingSeverity.Error, "MISSING_FIELDS",
                    "Required: sterilization_id, test_type, test_datetime, result.");
                errors++; continue;
            }

            if (!mappings.TryGet("Sterilizations", legacyCycleId.Value.ToString(), out var newCycleStr)
                || !int.TryParse(newCycleStr, out var newCycleId))
            {
                findings.Add(Name, legacyId, FindingSeverity.Error, "CYCLE_NOT_MAPPED",
                    $"Sterilization legacy id {legacyCycleId} not mapped.");
                errors++; continue;
            }

            if (mappings.TryGet(Name, legacyId, out _)) { skipped++; continue; }

            try
            {
                int newId = await UpsertAsync(hsms, newCycleId, testType!, testDatetime.Value, result!,
                    StepHelpers.AsNullableDecimal(reader["measured_value"]),
                    StepHelpers.AsNullableString(reader["unit"]),
                    StepHelpers.AsNullableString(reader["notes"]),
                    StepHelpers.AsNullableString(reader["performed_by"]),
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

    private static async Task<int> UpsertAsync(SqlConnection hsms, int sterilizationId, string testType, DateTime testDateTime, string result,
        decimal? measured, string? unit, string? notes, string? performedBy, bool dryRun, CancellationToken ct)
    {
        const string findSql = @"SELECT qa_test_id FROM dbo.qa_tests
                                 WHERE sterilization_id = @sid AND test_type = @type AND CAST(test_datetime AS DATE) = CAST(@dt AS DATE)";
        await using (var find = new SqlCommand(findSql, hsms))
        {
            find.Parameters.AddWithValue("@sid", sterilizationId);
            find.Parameters.AddWithValue("@type", testType);
            find.Parameters.AddWithValue("@dt", testDateTime);
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is not null and not DBNull) return Convert.ToInt32(existing);
        }
        if (dryRun) return 0;

        const string insertSql = @"INSERT INTO dbo.qa_tests
            (sterilization_id, test_type, test_datetime, result, measured_value, unit, notes, performed_by)
            OUTPUT INSERTED.qa_test_id
            VALUES(@sid, @type, @dt, @result, @measured, @unit, @notes, @by)";
        await using var ins = new SqlCommand(insertSql, hsms);
        ins.Parameters.AddWithValue("@sid", sterilizationId);
        ins.Parameters.AddWithValue("@type", testType);
        ins.Parameters.AddWithValue("@dt", testDateTime);
        ins.Parameters.AddWithValue("@result", result);
        ins.Parameters.AddWithValue("@measured", (object?)measured ?? DBNull.Value);
        ins.Parameters.AddWithValue("@unit", (object?)unit ?? DBNull.Value);
        ins.Parameters.AddWithValue("@notes", (object?)notes ?? DBNull.Value);
        ins.Parameters.AddWithValue("@by", (object?)performedBy ?? DBNull.Value);
        var idObj = await ins.ExecuteScalarAsync(ct);
        return Convert.ToInt32(idObj);
    }
}
