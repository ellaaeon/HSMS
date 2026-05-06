using HSMS.Persistence.Data;
using HSMS.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/health")]
[Authorize]
public sealed class HealthController(HsmsDbContext dbContext) : ControllerBase
{
    [HttpGet("schema")]
    public async Task<ActionResult<SchemaHealthDto>> Schema(CancellationToken cancellationToken)
    {
        var checks = new (string Table, string Column, string Label)[]
        {
            ("tbl_sterilizer_no", "manufacturer", "tbl_sterilizer_no.manufacturer"),
            ("tbl_sterilizer_no", "purchase_date", "tbl_sterilizer_no.purchase_date"),
            ("tbl_departments", "department_code", "tbl_departments.department_code")
        };

        var missing = new List<string>();
        foreach (var (table, column, label) in checks)
        {
            if (!await ColumnExistsAsync(table, column, cancellationToken))
            {
                missing.Add(label);
            }
        }

        if (missing.Count == 0)
        {
            return Ok(new SchemaHealthDto
            {
                IsOk = true,
                Message = "Schema is up to date."
            });
        }

        return Ok(new SchemaHealthDto
        {
            IsOk = false,
            MissingItems = missing,
            Message = "Schema migration required. Run latest ddl scripts."
        });
    }

    private async Task<bool> ColumnExistsAsync(string tableName, string columnName, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT CASE WHEN EXISTS (
                       SELECT 1
                       FROM INFORMATION_SCHEMA.COLUMNS
                       WHERE TABLE_SCHEMA = 'dbo'
                         AND TABLE_NAME = '{tableName}'
                         AND COLUMN_NAME = '{columnName}'
                   ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END
                   """;

        var result = await dbContext.Database.SqlQueryRaw<bool>(sql).SingleAsync(cancellationToken);
        return result;
    }
}
