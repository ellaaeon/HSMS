namespace HSMS.Shared.Contracts.Reporting;

/// <summary>
/// Stable identifiers for the four RDLC reports printed from HSMS.
/// String-valued (not an int enum) so the value is stable across DB, audit logs, and print logs.
/// </summary>
public static class ReportType
{
    public const string LoadRecord = "LoadRecord";
    public const string BILogSheet = "BILogSheet";
    public const string LeakTest = "LeakTest";
    public const string BowieDick = "BowieDick";

    public static readonly string[] All = [LoadRecord, BILogSheet, LeakTest, BowieDick];

    public static bool IsKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value) && Array.IndexOf(All, value) >= 0;
}
