using System.Globalization;
using HSMS.Shared.Time;

namespace HSMS.Shared.Contracts;

/// <summary>Merges BI incubator date + 24h time text (deployment zone wall clock) with stored UTC instants.</summary>
public static class BiLogSheetTimeEditor
{
    /// <summary>Fills <see cref="BiLogSheetRowDto"/> date/time editor fields from <see cref="BiLogSheetRowDto.BiTimeInUtc"/> / <see cref="BiLogSheetRowDto.BiTimeOutUtc"/>.</summary>
    public static void SyncEditorsFromUtc(BiLogSheetRowDto row)
    {
        if (row.BiTimeInUtc is { } tin)
        {
            var local = HsmsDeploymentTimeZone.UtcToDeployment(tin);
            row.BiTimeInTimeText = local.ToString("HH:mm", CultureInfo.InvariantCulture);
            row.BiTimeInHour = local.ToString("HH", CultureInfo.InvariantCulture);
            row.BiTimeInMinute = local.ToString("mm", CultureInfo.InvariantCulture);
        }
        else
        {
            row.BiTimeInTimeText = "";
            row.BiTimeInHour = "";
            row.BiTimeInMinute = "";
        }

        row.BiTimeInDateDeployment = null;

        if (row.BiTimeOutUtc is { } tout)
        {
            var local = HsmsDeploymentTimeZone.UtcToDeployment(tout);
            row.BiTimeOutTimeText = local.ToString("HH:mm", CultureInfo.InvariantCulture);
            row.BiTimeOutHour = local.ToString("HH", CultureInfo.InvariantCulture);
            row.BiTimeOutMinute = local.ToString("mm", CultureInfo.InvariantCulture);
            row.BiTimeOutDateDeployment = null;
        }
        else
        {
            row.BiTimeOutTimeText = "";
            row.BiTimeOutHour = "";
            row.BiTimeOutMinute = "";
            row.BiTimeOutDateDeployment = null;
        }

        // Time-out is time-only (HH:mm) and is anchored to the cycle date.
    }

    /// <summary>Builds a payload from the row, merging date + time editor fields into UTC using the deployment zone.</summary>
    public static bool TryBuildCommittedPayload(BiLogSheetRowDto row, out BiLogSheetUpdatePayload? payload, out string? error)
    {
        if (!TryMergeUtc(row, out var tin, out var tout, out error))
        {
            payload = null;
            return false;
        }

        payload = new BiLogSheetUpdatePayload(
            row.BiDaily,
            row.BiIncubatorTemp,
            row.BiIncubatorChecked,
            row.BiTimeInInitials,
            row.BiTimeOutInitials,
            string.IsNullOrWhiteSpace(row.BiProcessedResult24m) ? null : row.BiProcessedResult24m.Trim(),
            null,
            string.IsNullOrWhiteSpace(row.BiProcessedResult24h) ? null : row.BiProcessedResult24h.Trim(),
            null,
            string.IsNullOrWhiteSpace(row.BiControlResult24m) ? null : row.BiControlResult24m.Trim(),
            null,
            string.IsNullOrWhiteSpace(row.BiControlResult24h) ? null : row.BiControlResult24h.Trim(),
            null,
            row.Notes,
            tin,
            tout);
        return true;
    }

    private static bool TryMergeUtc(BiLogSheetRowDto row, out DateTime? biTimeInUtc, out DateTime? biTimeOutUtc, out string? error)
    {
        biTimeInUtc = null;
        biTimeOutUtc = null;
        error = null;

        if (!TryMergeBiTimeIn(row, out var tin, out error))
        {
            return false;
        }

        if (!TryMergeBiTimeOut(row, out var tout, out error))
        {
            return false;
        }

        if (tin is { } a && tout is { } b && b < a)
        {
            error = "BI time out cannot be before BI time in.";
            return false;
        }

        biTimeInUtc = tin;
        biTimeOutUtc = tout;
        return true;
    }

    /// <summary>BI time in uses the same calendar day as <see cref="BiLogSheetRowDto.CycleDateTimeUtc"/> (Date column); only HH:mm is edited.</summary>
    private static bool TryMergeBiTimeIn(BiLogSheetRowDto row, out DateTime? utc, out string? error)
    {
        utc = null;
        error = null;
        var t = BuildTimeText(row.BiTimeInHour, row.BiTimeInMinute, row.BiTimeInTimeText);
        if (string.IsNullOrEmpty(t))
        {
            return true;
        }

        var calendarDay = (row.BiTimeInDateDeployment ?? HsmsDeploymentTimeZone.UtcToDeployment(row.CycleDateTimeUtc).Date).Date;
        return TryParsePair(DateTime.SpecifyKind(calendarDay, DateTimeKind.Unspecified), t, "BI time in", out utc, out error);
    }

    /// <summary>BI time out uses the same calendar day as <see cref="BiLogSheetRowDto.CycleDateTimeUtc"/> (Date column); only HH:mm is edited.</summary>
    private static bool TryMergeBiTimeOut(BiLogSheetRowDto row, out DateTime? utc, out string? error)
    {
        utc = null;
        error = null;
        var t = BuildTimeText(row.BiTimeOutHour, row.BiTimeOutMinute, row.BiTimeOutTimeText);
        if (string.IsNullOrEmpty(t))
        {
            return true;
        }

        var calendarDay = (row.BiTimeOutDateDeployment ?? HsmsDeploymentTimeZone.UtcToDeployment(row.CycleDateTimeUtc).Date).Date;
        return TryParsePair(DateTime.SpecifyKind(calendarDay, DateTimeKind.Unspecified), t, "BI time out", out utc, out error);
    }

    /// <summary>Prefer explicit HH:mm text (masked editor); legacy hour/minute dropdowns remain as fallback.</summary>
    private static string BuildTimeText(string? hour, string? minute, string? fallbackTimeText)
    {
        var fb = (fallbackTimeText ?? "").Trim();
        if (!string.IsNullOrEmpty(fb))
        {
            return fb;
        }

        var h = (hour ?? "").Trim();
        var m = (minute ?? "").Trim();
        if (!string.IsNullOrEmpty(h) && !string.IsNullOrEmpty(m))
        {
            return $"{h}:{m}";
        }

        return "";
    }

    private static bool TryParsePair(DateTime? date, string? timeText, string label, out DateTime? utc, out string? error)
    {
        utc = null;
        error = null;
        var t = (timeText ?? "").Trim();
        if (date is null && string.IsNullOrEmpty(t))
        {
            return true;
        }

        if (date is null)
        {
            error = $"{label}: enter a calendar date, or clear the time field.";
            return false;
        }

        if (string.IsNullOrEmpty(t))
        {
            error = $"{label}: enter time as HH:mm (24-hour), or clear the date.";
            return false;
        }

        if (!TryParseTimeOfDay(t, out var tod, out var parseErr))
        {
            error = $"{label}: {parseErr}";
            return false;
        }

        if (tod < TimeSpan.Zero || tod >= TimeSpan.FromHours(24))
        {
            error = $"{label}: time must be between 00:00 and 23:59.";
            return false;
        }

        var wall = DateTime.SpecifyKind(date.Value.Date + tod, DateTimeKind.Unspecified);
        utc = HsmsDeploymentTimeZone.DeploymentWallToUtc(wall);
        return true;
    }

    private static bool TryParseTimeOfDay(string text, out TimeSpan timeOfDay, out string error)
    {
        timeOfDay = default;
        var parts = text.Split(':');
        if (parts.Length != 2)
        {
            error = "Use HH:mm in 24-hour format (e.g. 09:05 or 14:30).";
            return false;
        }

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m))
        {
            error = "Hours and minutes must be numbers.";
            return false;
        }

        if (h is < 0 or > 23 || m is < 0 or > 59)
        {
            error = "Hours must be 0–23 and minutes 0–59.";
            return false;
        }

        timeOfDay = new TimeSpan(h, m, 0);
        error = "";
        return true;
    }
}
