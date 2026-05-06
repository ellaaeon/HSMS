namespace HSMS.Shared.Contracts;

/// <summary>Allowed BI result values (must match WPF Register Load combo).</summary>
public static class BiResultValues
{
    public static readonly string[] All = ["Pending", "Pass", "Fail", "N/A"];

    public static bool IsAllowed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (var a in All)
        {
            if (string.Equals(a, value.Trim(), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
