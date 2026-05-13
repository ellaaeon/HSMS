using Microsoft.Data.SqlClient;

namespace HSMS.LegacyMigrator.Steps;

internal static class StepHelpers
{
    public static async Task<bool> TableExistsAsync(SqlConnection conn, string tableName, CancellationToken ct)
    {
        const string sql = "SELECT 1 WHERE OBJECT_ID(@t, N'U') IS NOT NULL";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@t", tableName);
        var r = await cmd.ExecuteScalarAsync(ct);
        return r is not null && r is not DBNull;
    }

    public static string? AsNullableString(object value)
        => value is null or DBNull ? null : Convert.ToString(value);

    public static int? AsNullableInt(object value)
        => value is null or DBNull ? null : Convert.ToInt32(value);

    public static DateTime? AsNullableDate(object value)
        => value is null or DBNull ? null : Convert.ToDateTime(value);

    public static decimal? AsNullableDecimal(object value)
        => value is null or DBNull ? null : Convert.ToDecimal(value);
}
