using HSMS.LegacyMigrator.Infrastructure;
using HSMS.LegacyMigrator.Logging;
using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Steps;

/// <summary>
/// Migrates legacy sterilizers into dbo.tbl_sterilizer_no.
/// Legacy expectation (configurable): tbl_sterilizer_no(legacy_id, sterilizer_no, model, manufacturer, purchase_date)
/// or hsms_db.dbo.sterilizers(id, no, model, brand, purchase_date)
/// </summary>
internal sealed class SterilizersStep : IMigrationStep
{
    public string Name => "Sterilizers";

    public async Task<StepResult> RunAsync(MigrationContext context, MappingStore mappings, FindingStore findings, CancellationToken ct)
    {
        await using var legacy = context.OpenLegacy();
        await using var hsms = context.OpenHsms();

        string? selectSql = null;
        if (await StepHelpers.TableExistsAsync(legacy, "dbo.tbl_sterilizer_no", ct))
        {
            selectSql = @"SELECT CAST(sterilizer_id AS NVARCHAR(64)) AS legacy_id,
                                 sterilizer_no, model, manufacturer, purchase_date
                          FROM dbo.tbl_sterilizer_no";
        }
        else if (await StepHelpers.TableExistsAsync(legacy, "dbo.sterilizers", ct))
        {
            selectSql = @"SELECT CAST(id AS NVARCHAR(64)) AS legacy_id,
                                 no AS sterilizer_no, model, brand AS manufacturer, purchase_date
                          FROM dbo.sterilizers";
        }

        if (selectSql is null)
        {
            findings.Add(Name, null, FindingSeverity.Warning, "TABLE_NOT_FOUND", "No legacy sterilizers table found; skipping.");
            return StepResult.Empty;
        }

        int read = 0, inserted = 0, skipped = 0, errors = 0;
        await using var cmd = new SqlCommand(selectSql, legacy);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            read++;
            var legacyId = reader["legacy_id"]?.ToString() ?? string.Empty;
            var no = StepHelpers.AsNullableString(reader["sterilizer_no"])?.Trim();
            if (string.IsNullOrWhiteSpace(no))
            {
                findings.Add(Name, legacyId, FindingSeverity.Error, "MISSING_NUMBER", "sterilizer_no is required.");
                errors++; continue;
            }

            if (mappings.TryGet(Name, legacyId, out _))
            {
                skipped++; continue;
            }

            try
            {
                int newId = await UpsertAsync(hsms, no!,
                    StepHelpers.AsNullableString(reader["model"]),
                    StepHelpers.AsNullableString(reader["manufacturer"]),
                    StepHelpers.AsNullableDate(reader["purchase_date"]),
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

    private static async Task<int> UpsertAsync(SqlConnection hsms, string sterilizerNo, string? model, string? manufacturer, DateTime? purchaseDate, bool dryRun, CancellationToken ct)
    {
        const string findSql = "SELECT sterilizer_id FROM dbo.tbl_sterilizer_no WHERE sterilizer_no = @no";
        await using (var find = new SqlCommand(findSql, hsms))
        {
            find.Parameters.AddWithValue("@no", sterilizerNo);
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is int eid) return eid;
            if (existing is not null and not DBNull) return Convert.ToInt32(existing);
        }

        if (dryRun) return 0;

        const string insertSql = @"INSERT INTO dbo.tbl_sterilizer_no(sterilizer_no, model, manufacturer, purchase_date)
                                   OUTPUT INSERTED.sterilizer_id
                                   VALUES(@no, @model, @manufacturer, @purchase_date)";
        await using var ins = new SqlCommand(insertSql, hsms);
        ins.Parameters.AddWithValue("@no", sterilizerNo);
        ins.Parameters.AddWithValue("@model", (object?)model ?? DBNull.Value);
        ins.Parameters.AddWithValue("@manufacturer", (object?)manufacturer ?? DBNull.Value);
        ins.Parameters.AddWithValue("@purchase_date", (object?)purchaseDate ?? DBNull.Value);
        var idObj = await ins.ExecuteScalarAsync(ct);
        return Convert.ToInt32(idObj);
    }
}
