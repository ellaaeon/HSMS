using System.ComponentModel;
using System.Globalization;
using HSMS.Shared.Contracts;
using HSMS.Shared.Time;

namespace HSMS.Desktop.Ui;

/// <summary>Load Records grid row with editable cycle end (deployment-zone wall time in the editor, UTC in the model).</summary>
public sealed class LoadRecordGridRow : INotifyPropertyChanged
{
    private DateTime? _cycleTimeOutUtc;
    private string _rowVersion;
    private string _cycleStatus;

    public LoadRecordGridRow(SterilizationSearchItemDto dto)
    {
        SterilizationId = dto.SterilizationId;
        CycleNo = dto.CycleNo;
        SterilizerNo = dto.SterilizerNo;
        _cycleStatus = dto.CycleStatus;
        CycleDateTimeUtc = dto.CycleDateTimeUtc;
        RegisteredAtUtc = dto.RegisteredAtUtc;
        _cycleTimeOutUtc = dto.CycleTimeOutUtc;
        CreatedByAccountId = dto.CreatedByAccountId;
        OperatorName = dto.OperatorName;
        TotalPcs = dto.TotalPcs;
        TotalQty = dto.TotalQty;
        _rowVersion = dto.RowVersion ?? "";
    }

    public int SterilizationId { get; }
    public string CycleNo { get; }
    public string SterilizerNo { get; }
    public string CycleStatus
    {
        get => _cycleStatus;
        private set
        {
            if (string.Equals(_cycleStatus, value, StringComparison.Ordinal))
            {
                return;
            }

            _cycleStatus = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CycleStatus)));
        }
    }
    public DateTime CycleDateTimeUtc { get; }
    public int? CreatedByAccountId { get; }
    public string OperatorName { get; }
    public int TotalPcs { get; }
    public int TotalQty { get; }

    /// <summary>UTC when the load was registered (same as Cycle start in Load Records).</summary>
    public DateTime RegisteredAtUtc { get; }

    /// <summary>Cycle start column: registration time only (not load/cycle datetime).</summary>
    public DateTime CycleStartUtc => RegisteredAtUtc;

    public DateTime? CycleTimeOutUtc
    {
        get => _cycleTimeOutUtc;
        private set
        {
            if (Nullable.Equals(_cycleTimeOutUtc, value))
            {
                return;
            }

            _cycleTimeOutUtc = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CycleTimeOutUtc)));
        }
    }

    public string RowVersion
    {
        get => _rowVersion;
        private set
        {
            if (_rowVersion == value)
            {
                return;
            }

            _rowVersion = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RowVersion)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplySavedCycleEnd(DateTime? utcEnd, string newRowVersionBase64)
    {
        CycleTimeOutUtc = utcEnd;
        RowVersion = newRowVersionBase64;
    }

    public void ApplySavedCycleStatus(string canonicalStatus, string newRowVersionBase64)
    {
        CycleStatus = canonicalStatus;
        RowVersion = newRowVersionBase64;
    }

    /// <summary>Empty clears cycle end. Parses strict <c>HH:mm</c> combined with this row's registration calendar date (deployment zone).</summary>
    public static bool TryParseCycleEndHm(string? hmText, LoadRecordGridRow row, out DateTime? utcEnd, out string? error)
    {
        utcEnd = null;
        error = null;
        var hm = (hmText ?? "").Trim();
        if (hm.Length == 0)
        {
            return true;
        }

        var parts = hm.Split(':');
        if (parts.Length != 2
            || parts[0].Length != 2
            || parts[1].Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || h is < 0 or > 23
            || m is < 0 or > 59)
        {
            error = "Enter a valid 24-hour time (HH:mm), e.g. 14:05.";
            return false;
        }

        var anchorLocal = HsmsDeploymentTimeZone.UtcToDeployment(row.RegisteredAtUtc);
        var wall = DateTime.SpecifyKind(anchorLocal.Date.AddHours(h).AddMinutes(m), DateTimeKind.Unspecified);
        utcEnd = HsmsDeploymentTimeZone.DeploymentWallToUtc(wall);
        return true;
    }

    /// <summary>Hour:minute string in deployment zone for masked editor.</summary>
    public static string FormatCycleEndTimeHm(DateTime? utc)
    {
        return utc is null
            ? ""
            : HsmsDeploymentTimeZone.FormatInDeploymentZone(
                HsmsDeploymentTimeZone.AsUtcKind(utc.Value),
                "HH:mm",
                CultureInfo.InvariantCulture);
    }

}
