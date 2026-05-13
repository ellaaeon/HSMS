using HSMS.Persistence.Data;
using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Application.Reporting;

/// <summary>
/// Reusable dataset-builder pattern: each report has a typed builder that turns a request into a strongly-typed
/// data object plus a validation result. Render engines never touch the database; they only consume datasets.
/// </summary>
/// <typeparam name="TData">Per-report data class (see <c>Datasets/*ReportData.cs</c>).</typeparam>
public interface IReportDatasetBuilder<TData> where TData : class, new()
{
    /// <summary>Stable string id (one of <see cref="ReportType"/>) handled by this builder.</summary>
    string ReportType { get; }

    /// <summary>Run after data load; surfaces missing receipts, missing items, etc.</summary>
    Task<(TData? data, ReportValidationResult validation)> BuildAsync(
        HsmsDbContext db,
        ReportRenderRequestDto request,
        CancellationToken cancellationToken);
}
