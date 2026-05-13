using HSMS.LegacyMigrator.Infrastructure;
using HSMS.LegacyMigrator.Logging;
using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Steps;

internal sealed class DepartmentsStep : IMigrationStep
{
    public string Name => "Departments";

    public async Task<StepResult> RunAsync(MigrationContext context, MappingStore mappings, FindingStore findings, CancellationToken ct)
    {
        await using var legacy = context.OpenLegacy();
        await using var hsms = context.OpenHsms();

        string? selectSql = null;
        if (await StepHelpers.TableExistsAsync(legacy, "dbo.tbl_departments", ct))
        {
            selectSql = "SELECT CAST(department_id AS NVARCHAR(64)) AS legacy_id, department_code, department_name FROM dbo.tbl_departments";
        }
        else if (await StepHelpers.TableExistsAsync(legacy, "dbo.departments", ct))
        {
            selectSql = "SELECT CAST(id AS NVARCHAR(64)) AS legacy_id, code AS department_code, name AS department_name FROM dbo.departments";
        }

        if (selectSql is null)
        {
            findings.Add(Name, null, FindingSeverity.Warning, "TABLE_NOT_FOUND", "No legacy departments table found; skipping.");
            return StepResult.Empty;
        }

        int read = 0, inserted = 0, skipped = 0, errors = 0;
        await using var cmd = new SqlCommand(selectSql, legacy);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            read++;
            var legacyId = reader["legacy_id"]?.ToString() ?? string.Empty;
            var code = StepHelpers.AsNullableString(reader["department_code"])?.Trim();
            var name = StepHelpers.AsNullableString(reader["department_name"])?.Trim();
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                findings.Add(Name, legacyId, FindingSeverity.Error, "MISSING_FIELDS", "department_code and department_name are required.");
                errors++; continue;
            }
            if (mappings.TryGet(Name, legacyId, out _)) { skipped++; continue; }

            try
            {
                int newId = await UpsertAsync(hsms, code!, name!, context.DryRun, ct);
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

    private static async Task<int> UpsertAsync(SqlConnection hsms, string code, string name, bool dryRun, CancellationToken ct)
    {
        const string findSql = "SELECT department_id FROM dbo.tbl_departments WHERE department_code = @code OR department_name = @name";
        await using (var find = new SqlCommand(findSql, hsms))
        {
            find.Parameters.AddWithValue("@code", code);
            find.Parameters.AddWithValue("@name", name);
            var existing = await find.ExecuteScalarAsync(ct);
            if (existing is not null and not DBNull) return Convert.ToInt32(existing);
        }
        if (dryRun) return 0;

        const string insertSql = @"INSERT INTO dbo.tbl_departments(department_code, department_name)
                                   OUTPUT INSERTED.department_id
                                   VALUES(@code, @name)";
        await using var ins = new SqlCommand(insertSql, hsms);
        ins.Parameters.AddWithValue("@code", code);
        ins.Parameters.AddWithValue("@name", name);
        var idObj = await ins.ExecuteScalarAsync(ct);
        return Convert.ToInt32(idObj);
    }
}
