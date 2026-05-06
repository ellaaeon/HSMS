using System.Collections.Generic;
using System.Globalization;
using HSMS.Shared.Time;

namespace HSMS.Shared.Contracts;

/// <summary>One row for the steam sterilizer periodic QA BI log sheet (paper form layout).</summary>
public sealed class BiLogSheetRowDto
{
    public int SterilizationId { get; set; }
    /// <summary>RowVersion for optimistic concurrency when updating BI log fields from the grid.</summary>
    public string RowVersion { get; set; } = string.Empty;

    public DateTime CycleDateTimeUtc { get; set; }
    public string SterilizerNo { get; set; } = string.Empty;
    public string CycleNo { get; set; } = string.Empty;
    public string? BiLotNo { get; set; }
    public bool? BiDaily { get; set; }
    public bool Implants { get; set; }
    public int? LoadQty { get; set; }
    public int? ExposureTimeMinutes { get; set; }
    public decimal? TemperatureC { get; set; }
    public string? BiIncubatorTemp { get; set; }
    public bool? BiIncubatorChecked { get; set; }
    public DateTime? BiTimeInUtc { get; set; }
    /// <summary>Legacy field; time-in is HH:mm only and uses the cycle date. Cleared when syncing from UTC.</summary>
    public DateTime? BiTimeInDateDeployment { get; set; }
    /// <summary>24-hour time text HH:mm for BI time-in, deployment zone.</summary>
    public string BiTimeInTimeText { get; set; } = "";
    /// <summary>UI editor field: hours (00-23) for time in.</summary>
    public string BiTimeInHour { get; set; } = "";
    /// <summary>UI editor field: minutes (00-59) for time in.</summary>
    public string BiTimeInMinute { get; set; } = "";
    public string? BiTimeInInitials { get; set; }
    public DateTime? BiTimeOutUtc { get; set; }
    /// <summary>Legacy field; time-out is HH:mm only and uses the cycle date. Cleared when syncing from UTC.</summary>
    public DateTime? BiTimeOutDateDeployment { get; set; }
    public string BiTimeOutTimeText { get; set; } = "";
    /// <summary>UI editor field: hours (00-23) for time out.</summary>
    public string BiTimeOutHour { get; set; } = "";
    /// <summary>UI editor field: minutes (00-59) for time out.</summary>
    public string BiTimeOutMinute { get; set; } = "";
    public string? BiTimeOutInitials { get; set; }
    /// <summary>Empty string, "+", or "-" for grid binding.</summary>
    public string BiProcessedResult24m { get; set; } = "";
    public int? BiProcessedValue24m { get; set; }

    public string BiProcessedResult24h { get; set; } = "";
    public int? BiProcessedValue24h { get; set; }
    public string BiControlResult24m { get; set; } = "";
    public int? BiControlValue24m { get; set; }
    public string BiControlResult24h { get; set; } = "";
    public int? BiControlValue24h { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string? Notes { get; set; }
    /// <summary>UTC when any BI log QA field (including legacy <c>bi_result</c>) was last changed.</summary>
    public DateTime? BiResultUpdatedAtUtc { get; set; }

    public string SterilizerCycleNoDisplay => $"{SterilizerNo} / {CycleNo}";

    public string ExposureTimeTempDisplay
    {
        get
        {
            var parts = new List<string>(2);
            if (ExposureTimeMinutes.HasValue)
            {
                parts.Add($"{ExposureTimeMinutes} min");
            }

            if (TemperatureC.HasValue)
            {
                parts.Add($"{TemperatureC.Value.ToString("0.##", CultureInfo.InvariantCulture)} °C");
            }

            return parts.Count == 0 ? "" : string.Join(" / ", parts);
        }
    }

    public string BiProcessed24mDisplay => PlusMinusOnly(BiProcessedResult24m);
    public string BiProcessed24hDisplay => PlusMinusOnly(BiProcessedResult24h);
    public string BiControl24mDisplay => PlusMinusOnly(BiControlResult24m);
    public string BiControl24hDisplay => PlusMinusOnly(BiControlResult24h);

    /// <summary>One-line summary for merged grid column (time + initials).</summary>
    public string BiTimeInCellSummary => JoinTimeParts(BiTimeInTimeText, BiTimeInInitials);

    /// <summary>Time segment for read-only cell template (placeholder when empty).</summary>
    public string BiTimeInDisplayTime => string.IsNullOrWhiteSpace(BiTimeInTimeText) ? "__:__" : BiTimeInTimeText.Trim();

    /// <summary>Initials segment for read-only cell template (placeholder when empty).</summary>
    public string BiTimeInDisplayInitials => string.IsNullOrWhiteSpace(BiTimeInInitials) ? "__" : BiTimeInInitials!.Trim();

    /// <summary>Time-out segment for read-only template (placeholder when empty).</summary>
    public string BiTimeOutDisplayTime => string.IsNullOrWhiteSpace(BiTimeOutTimeText) ? "__:__" : BiTimeOutTimeText.Trim();

    /// <summary>Time-out initials segment for read-only template (placeholder when empty).</summary>
    public string BiTimeOutDisplayInitials => string.IsNullOrWhiteSpace(BiTimeOutInitials) ? "__" : BiTimeOutInitials!.Trim();

    /// <summary>Calendar day shown for time-out (defaults to cycle day in deployment zone).</summary>
    public string BiTimeOutDateDisplayForCell
    {
        get
        {
            var day = BiTimeOutDateDeployment?.Date ?? HsmsDeploymentTimeZone.UtcToDeployment(CycleDateTimeUtc).Date;
            return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>True when the editor date differs from the cycle calendar day (deployment zone).</summary>
    public bool BiTimeOutDateOverridesCycleDay
    {
        get
        {
            if (BiTimeOutDateDeployment is not { } d)
            {
                return false;
            }

            var cycleDay = HsmsDeploymentTimeZone.UtcToDeployment(CycleDateTimeUtc).Date;
            return d.Date != cycleDay;
        }
    }

    /// <summary>
    /// One-line summary for merged grid column. Shows an explicit date only when it differs from the cycle calendar day.
    /// </summary>
    public string BiTimeOutCellSummary
    {
        get
        {
            var parts = new List<string>(4);
            if (BiTimeOutDateDeployment is { } d)
            {
                var cycleDay = HsmsDeploymentTimeZone.UtcToDeployment(CycleDateTimeUtc).Date;
                if (d.Date != cycleDay)
                {
                    parts.Add(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
            }

            var time = (BiTimeOutTimeText ?? "").Trim();
            if (!string.IsNullOrEmpty(time))
            {
                parts.Add(time);
            }

            var initials = (BiTimeOutInitials ?? "").Trim();
            if (!string.IsNullOrEmpty(initials))
            {
                parts.Add(initials);
            }

            return parts.Count == 0 ? "" : string.Join(" · ", parts);
        }
    }

    private static string JoinTimeParts(string? timeText, string? initials)
    {
        var t = (timeText ?? "").Trim();
        var i = (initials ?? "").Trim();
        if (string.IsNullOrEmpty(t) && string.IsNullOrEmpty(i))
        {
            return "";
        }

        if (string.IsNullOrEmpty(i))
        {
            return t;
        }

        if (string.IsNullOrEmpty(t))
        {
            return i;
        }

        return $"{t} · {i}";
    }

    private static string PlusMinusWithValue(string? sign, int? value)
    {
        var s = string.IsNullOrWhiteSpace(sign) ? "" : sign.Trim();
        if (value is null)
        {
            return s;
        }

        return string.IsNullOrEmpty(s) ? value.Value.ToString(CultureInfo.InvariantCulture) : s + value.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string PlusMinusOnly(string? sign)
    {
        var s = string.IsNullOrWhiteSpace(sign) ? "" : sign.Trim();
        return s is "+" or "-" ? s : "";
    }
}
