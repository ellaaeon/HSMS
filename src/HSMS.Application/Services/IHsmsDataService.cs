using HSMS.Shared.Contracts;
using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Application.Services;

/// <summary>
/// In-process data access for the WPF client (standalone). Mirrors the former HTTP client surface.
/// </summary>
public interface IHsmsDataService
{
    /// <summary>Generates the next sequential cycle number (e.g. 00001).</summary>
    Task<(string? cycleNo, string? error)> GetNextCycleNoAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> SearchCyclesAsync(string cycleNo, CancellationToken cancellationToken = default);

    /// <param name="searchQuery">Free text filter. When <paramref name="matchCycleNoOnly"/> is true, matched as cycle number prefix only (registration lookup).</param>
    /// <param name="matchCycleNoOnly">If true, <paramref name="searchQuery"/> applies only to <c>CycleNo</c> (<c>StartsWith</c>). Otherwise matches sterilizer label, operator, status, BI fields, notes, items, department, doctor, etc.</param>
    Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> SearchCyclesFilteredAsync(
        string searchQuery,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default,
        bool matchCycleNoOnly = false);

    Task<(SterilizationDetailsDto? detail, string? error)> GetCycleAsync(int sterilizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists load records filtered by cycle program (e.g., Instruments, Bowie Dick, Warm Up, Leak Test).
    /// Uses sterilization load metadata (tbl_sterilization), not test records.
    /// </summary>
    Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> ListLoadsByCycleProgramAsync(
        string cycleProgramContains,
        string? searchQuery,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists load records that have BI enabled/filled (BI "YES"): <c>bi_lot_no</c> or <c>bi_result</c> is present.
    /// Uses sterilization load metadata (tbl_sterilization), not test records.
    /// </summary>
    Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> ListLoadsWithBiAsync(
        string? searchQuery,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default);

    Task<(SterilizationAnalyticsDto? analytics, string? error)> GetSterilizationAnalyticsAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string? operatorName,
        CancellationToken cancellationToken = default,
        DateTime? compareFromUtc = null,
        DateTime? compareToUtc = null);

    /// <summary>
    /// V2 analytics query with strongly-typed structured filters.
    /// This is additive and coexists with the legacy parameter surface.
    /// </summary>
    Task<(SterilizationAnalyticsDto? analytics, string? error)> GetSterilizationAnalyticsV2Async(
        AnalyticsDashboardQueryDto query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Structured drill-down listing for analytics (no free-text hack).
    /// Returns the same row shape used by the Load Records grid.
    /// </summary>
    Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> AnalyticsDrilldownAsync(
        AnalyticsFilterDto filter,
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AnalyticsPresetListItemDto> items, string? error)> ListAnalyticsPresetsAsync(
        CancellationToken cancellationToken = default);

    Task<(AnalyticsPresetDto? preset, string? error)> GetAnalyticsPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default);

    Task<(AnalyticsPresetDto? preset, string? error)> UpsertAnalyticsPresetAsync(
        int? presetId,
        AnalyticsPresetUpsertDto payload,
        CancellationToken cancellationToken = default);

    Task<string?> DeleteAnalyticsPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default);

    Task<string?> SetDefaultAnalyticsPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default);

    Task<(AnalyticsPresetDto? preset, string? error)> GetDefaultAnalyticsPresetAsync(
        CancellationToken cancellationToken = default);

    Task<(BiAnalyticsDto? analytics, string? error)> GetBiAnalyticsAsync(
        AnalyticsFilterDto filter,
        CancellationToken cancellationToken = default);

    Task<string?> AppendAnalyticsAuditAsync(
        AnalyticsAuditEventDto evt,
        CancellationToken cancellationToken = default);

    Task<(bool ok, string? error)> CreateCycleAsync(SterilizationUpsertDto payload, CancellationToken cancellationToken = default);

    Task<(bool ok, string? error)> UpdateCycleAsync(int sterilizationId, SterilizationUpsertDto payload, CancellationToken cancellationToken = default);

    /// <summary>Updates <c>cycle_time_out</c> only (Load Records grid). Returns new row version on success.</summary>
    Task<(bool ok, string? error, string? newRowVersion)> UpdateSterilizationCycleEndAsync(
        int sterilizationId,
        SterilizationCycleEndPatchDto payload,
        CancellationToken cancellationToken = default);

    /// <summary>Updates <c>cycle_status</c> only (Load Records grid).</summary>
    Task<(bool ok, string? error, string? newRowVersion)> UpdateSterilizationCycleStatusAsync(
        int sterilizationId,
        SterilizationCycleStatusPatchDto payload,
        CancellationToken cancellationToken = default);

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

    Task<(IReadOnlyList<CycleProgramListItemDto> items, string? error)> GetCycleProgramsAsync(CancellationToken cancellationToken = default);

    Task<(CycleProgramListItemDto? item, string? error)> CreateCycleProgramAsync(CycleProgramUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> UpdateCycleProgramAsync(int id, CycleProgramUpsertDto payload, CancellationToken cancellationToken = default);

    Task<string?> DeleteCycleProgramAsync(int id, CancellationToken cancellationToken = default);

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

    Task<(IReadOnlyList<InstrumentCheckListItemDto> items, string? error)> SearchInstrumentChecksAsync(
        string? query,
        int take,
        CancellationToken cancellationToken = default);

    Task<(InstrumentCheckListItemDto? item, string? error)> CreateInstrumentCheckAsync(
        InstrumentCheckCreateDto payload,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<QaTestListItemDto> items, string? error)> ListQaTestsAsync(
        QaTestQueryDto query,
        CancellationToken cancellationToken = default);

    Task<(int qaTestId, string? rowVersion, string? error)> CreateQaTestAsync(
        int sterilizationId,
        string testType,
        DateTime testDateTimeUtc,
        string result,
        decimal? measuredValue,
        string? unit,
        string? notes,
        string? performedBy,
        string? clientMachine,
        CancellationToken cancellationToken = default);

    Task<(string? rowVersion, string? error)> ApproveQaTestAsync(
        int qaTestId,
        string rowVersionBase64,
        string? remarks,
        string? clientMachine,
        CancellationToken cancellationToken = default);

    // Enterprise Sterilization QA Test Records module (new; coexists with legacy qa_tests).
    Task<(IReadOnlyList<SterilizationQaRecordListItemDto> items, string? error)> ListSterilizationQaRecordsAsync(
        SterilizationQaRecordQueryDto query,
        CancellationToken cancellationToken = default);

    Task<(long recordId, string? rowVersion, string? error)> CreateSterilizationQaRecordAsync(
        SterilizationQaRecordCreateDto payload,
        CancellationToken cancellationToken = default);

    Task<(string? rowVersion, string? error)> PatchSterilizationQaStatusAsync(
        long recordId,
        SterilizationQaRecordStatusPatchDto payload,
        CancellationToken cancellationToken = default);

    Task<(SterilizationQaDashboardDto? dashboard, string? error)> GetSterilizationQaDashboardAsync(
        SterilizationQaDashboardQueryDto query,
        CancellationToken cancellationToken = default);

    Task<(SterilizationQaTimelineDto? timeline, string? error)> GetSterilizationQaTimelineAsync(
        long recordId,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SterilizationQaAttachmentListItemDto> items, string? error)> ListSterilizationQaAttachmentsAsync(
        long recordId,
        CancellationToken cancellationToken = default);

    Task<(long attachmentId, string? rowVersion, string? error)> AddSterilizationQaAttachmentAsync(
        long recordId,
        SterilizationQaAttachmentAddDto payload,
        CancellationToken cancellationToken = default);

    Task<string?> AppendSterilizationQaAuditAsync(
        SterilizationQaAuditEventDto evt,
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<SterilizationQaPresetListItemDto> items, string? error)> ListSterilizationQaPresetsAsync(
        CancellationToken cancellationToken = default);

    Task<(SterilizationQaPresetDto? preset, string? error)> GetSterilizationQaPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default);

    Task<(SterilizationQaPresetDto? preset, string? error)> UpsertSterilizationQaPresetAsync(
        int? presetId,
        SterilizationQaPresetUpsertDto payload,
        CancellationToken cancellationToken = default);

    Task<string?> DeleteSterilizationQaPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default);

    Task<string?> SetDefaultSterilizationQaPresetAsync(
        int presetId,
        CancellationToken cancellationToken = default);

    Task<(SterilizationQaPresetDto? preset, string? error)> GetDefaultSterilizationQaPresetAsync(
        CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<PrintLogRowDto> rows, string? error)> ListPrintLogsAsync(
        PrintLogQueryDto query,
        CancellationToken cancellationToken = default);
}
