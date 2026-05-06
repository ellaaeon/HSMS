namespace HSMS.Shared.Contracts;

/// <summary>Editable BI log sheet (QA form) fields saved from the desktop grid.</summary>
public sealed record BiLogSheetUpdatePayload(
    bool? BiDaily,
    string? BiIncubatorTemp,
    bool? BiIncubatorChecked,
    string? BiTimeInInitials,
    string? BiTimeOutInitials,
    string? BiProcessedResult24m,
    int? BiProcessedValue24m,
    string? BiProcessedResult24h,
    int? BiProcessedValue24h,
    string? BiControlResult24m,
    int? BiControlValue24m,
    string? BiControlResult24h,
    int? BiControlValue24h,
    string? Notes,
    DateTime? BiTimeInUtc,
    DateTime? BiTimeOutUtc);
