using HSMS.Shared.Time;

namespace HSMS.Shared.Contracts;

public static class BiLogSheetUpdateValidator
{
    public const int MaxIncubatorTemp = 48;
    public const int MaxInitials = 32;
    public const int MaxNotes = 4000;
    public const int MaxBiPlusMinusValue = 9999;

    public static BiLogSheetUpdatePayload Normalize(BiLogSheetUpdatePayload p) =>
        p with
        {
            BiIncubatorTemp = string.IsNullOrWhiteSpace(p.BiIncubatorTemp) ? null : p.BiIncubatorTemp.Trim(),
            BiTimeInInitials = string.IsNullOrWhiteSpace(p.BiTimeInInitials) ? null : p.BiTimeInInitials.Trim(),
            BiTimeOutInitials = string.IsNullOrWhiteSpace(p.BiTimeOutInitials) ? null : p.BiTimeOutInitials.Trim(),
            BiProcessedResult24m = BiPlusMinusValues.Normalize(p.BiProcessedResult24m),
            BiProcessedResult24h = BiPlusMinusValues.Normalize(p.BiProcessedResult24h),
            BiControlResult24m = BiPlusMinusValues.Normalize(p.BiControlResult24m),
            BiControlResult24h = BiPlusMinusValues.Normalize(p.BiControlResult24h),
            Notes = string.IsNullOrWhiteSpace(p.Notes) ? null : p.Notes.Trim(),
            BiTimeInUtc = p.BiTimeInUtc is { } ti ? HsmsDeploymentTimeZone.AsUtcKind(ti) : null,
            BiTimeOutUtc = p.BiTimeOutUtc is { } to ? HsmsDeploymentTimeZone.AsUtcKind(to) : null
        };

    public static string? Validate(BiLogSheetUpdatePayload p)
    {
        if (p.BiIncubatorTemp is not null && p.BiIncubatorTemp.Length > MaxIncubatorTemp)
        {
            return $"Cannot save — incubator/temp text is too long (max {MaxIncubatorTemp} characters).";
        }

        if (p.BiTimeInInitials is not null && p.BiTimeInInitials.Length > MaxInitials)
        {
            return $"Cannot save — time-in initials are too long (max {MaxInitials} characters).";
        }

        if (p.BiTimeOutInitials is not null && p.BiTimeOutInitials.Length > MaxInitials)
        {
            return $"Cannot save — time-out initials are too long (max {MaxInitials} characters).";
        }

        if (!BiPlusMinusValues.IsValidOrNull(p.BiProcessedResult24m))
        {
            return "Cannot save — BI processed (24 min) must be blank, +, or -.";
        }

        if (!BiPlusMinusValues.IsValidOrNull(p.BiProcessedResult24h))
        {
            return "Cannot save — BI processed (24 hr) must be blank, +, or -.";
        }

        if (!BiPlusMinusValues.IsValidOrNull(p.BiControlResult24m))
        {
            return "Cannot save — BI control (24 min) must be blank, +, or -.";
        }

        if (!BiPlusMinusValues.IsValidOrNull(p.BiControlResult24h))
        {
            return "Cannot save — BI control (24 hr) must be blank, +, or -.";
        }

        if (p.BiProcessedValue24m is { } pv24m && (pv24m < 0 || pv24m > MaxBiPlusMinusValue))
        {
            return $"Cannot save — BI processed (24 min) number must be between 0 and {MaxBiPlusMinusValue}.";
        }

        if (p.BiProcessedValue24h is { } pv24h && (pv24h < 0 || pv24h > MaxBiPlusMinusValue))
        {
            return $"Cannot save — BI processed (24 hr) number must be between 0 and {MaxBiPlusMinusValue}.";
        }

        if (p.BiControlValue24m is { } cv24m && (cv24m < 0 || cv24m > MaxBiPlusMinusValue))
        {
            return $"Cannot save — BI control (24 min) number must be between 0 and {MaxBiPlusMinusValue}.";
        }

        if (p.BiControlValue24h is { } cv24h && (cv24h < 0 || cv24h > MaxBiPlusMinusValue))
        {
            return $"Cannot save — BI control (24 hr) number must be between 0 and {MaxBiPlusMinusValue}.";
        }

        if (p.Notes is not null && p.Notes.Length > MaxNotes)
        {
            return $"Cannot save — comments are too long (max {MaxNotes} characters).";
        }

        if (p.BiTimeInUtc is { } tin && (tin.Year < 1990 || tin.Year > 2130))
        {
            return "Cannot save — BI time in is outside a supported date range.";
        }

        if (p.BiTimeOutUtc is { } tout && (tout.Year < 1990 || tout.Year > 2130))
        {
            return "Cannot save — BI time out is outside a supported date range.";
        }

        if (p.BiTimeInUtc is { } a && p.BiTimeOutUtc is { } b && b < a)
        {
            return "Cannot save — BI time out cannot be before BI time in.";
        }

        return null;
    }
}
