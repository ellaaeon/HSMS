namespace HSMS.Shared.Contracts;

/// <summary>Paper BI log entries using '+' / '-' (positive / negative) reads.</summary>
public static class BiPlusMinusValues
{
    public static readonly string[] Options = ["", "+", "-"];

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim();
        return t is "+" or "-" ? t : null;
    }

    public static bool IsValidOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var t = value.Trim();
        return t is "+" or "-";
    }
}
