using HSMS.Shared.Contracts;

namespace HSMS.Application.Services;

/// <summary>
/// In-process data access for the WPF client (standalone). Mirrors the former HTTP client surface.
/// </summary>
public interface IHsmsDataService
{
    /// <summary>Generates the next sequential cycle number (e.g. 00001).</summary>
    Task<(string? cycleNo, string? error)> GetNextCycleNoAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> SearchCyclesAsync(string cycleNo, CancellationToken cancellationToken = default);

    Task<(SterilizationDetailsDto? detail, string? error)> GetCycleAsync(int sterilizationId, CancellationToken cancellationToken = default);

    Task<(bool ok, string? error)> CreateCycleAsync(SterilizationUpsertDto payload, CancellationToken cancellationToken = default);

    Task<(bool ok, string? error)> UpdateCycleAsync(int sterilizationId, SterilizationUpsertDto payload, CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SterilizerListItemDto> items, string? error)> GetSterilizersAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<DepartmentListItemDto> items, string? error)> GetDepartmentsAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<DoctorRoomListItemDto> items, string? error)> GetDoctorRoomsAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<DepartmentItemListItemDto> items, string? error)> GetDepartmentItemsAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AccountListItemDto> items, string? error)> GetAccountsAsync(CancellationToken cancellationToken = default);

    Task<(SchemaHealthDto? health, string? error)> GetSchemaHealthAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<BiLogSheetRowDto> rows, string? error)> GetBiLogSheetAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string? sterilizationType,
        string? cycleNo,
        CancellationToken cancellationToken = default);

    /// <summary>Updates BI result only (BI log sheet grid). Sets <c>bi_result_updated_at</c> when the value changes.</summary>
    Task<(bool ok, string? error, string? newRowVersion, DateTime? biResultUpdatedAtUtc)> UpdateSterilizationBiResultAsync(
        int sterilizationId,
        string rowVersionBase64,
        string? biResult,
        string? clientMachine,
        CancellationToken cancellationToken = default);

    /// <summary>Updates BI log sheet QA fields (paper form). Sets <c>bi_result_updated_at</c> when any field changes.</summary>
    Task<(bool ok, string? error, string? newRowVersion, DateTime? biResultUpdatedAtUtc)> UpdateSterilizationBiLogSheetAsync(
        int sterilizationId,
        string rowVersionBase64,
        BiLogSheetUpdatePayload payload,
        string? clientMachine,
        CancellationToken cancellationToken = default);

    Task<(SterilizerListItemDto? item, string? error)> CreateSterilizerAsync(SterilizerUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> UpdateSterilizerAsync(int id, SterilizerUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> DeleteSterilizerAsync(int id, CancellationToken cancellationToken = default);

    Task<(DepartmentListItemDto? item, string? error)> CreateDepartmentAsync(DepartmentUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> UpdateDepartmentAsync(int id, DepartmentUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> DeleteDepartmentAsync(int id, CancellationToken cancellationToken = default);

    Task<(DoctorRoomListItemDto? item, string? error)> CreateDoctorRoomAsync(DoctorRoomUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> UpdateDoctorRoomAsync(int id, DoctorRoomUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> DeleteDoctorRoomAsync(int id, CancellationToken cancellationToken = default);

    Task<(DepartmentItemListItemDto? item, string? error)> CreateDepartmentItemAsync(DepartmentItemUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> UpdateDepartmentItemAsync(int id, DepartmentItemUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> DeleteDepartmentItemAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Admin-only: security and compliance activity from append-only audit_logs.</summary>
    Task<(IReadOnlyList<AuditLogRowDto> rows, string? error)> GetAuditLogsAsync(
        AuditLogQueryDto query,
        CancellationToken cancellationToken = default);

    /// <summary>Admin-only: recent security alerts (e.g. repeated failed logins).</summary>
    Task<(IReadOnlyList<AuditSecurityAlertDto> rows, string? error)> GetAuditSecurityAlertsAsync(
        int take,
        CancellationToken cancellationToken = default);

    /// <summary>Admin-only: accounts with recent sign-in (approximate &quot;active&quot; visibility).</summary>
    Task<(IReadOnlyList<AccountRecentActivityDto> rows, string? error)> GetRecentlyActiveAccountsAsync(
        int withinHours,
        CancellationToken cancellationToken = default);

    /// <summary>Admin-only: high-volume sterilization updates in the last hour (possible bulk edit).</summary>
    Task<(IReadOnlyList<AuditVolumeRowDto> rows, string? error)> GetSterilizationUpdateVolumeAsync(
        int withinHours,
        int minUpdates,
        CancellationToken cancellationToken = default);
}
