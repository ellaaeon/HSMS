using HSMS.Application.Reporting.Datasets;
using HSMS.Application.Services;
using HSMS.Persistence.Data;
using HSMS.Shared.Contracts.Reporting;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Reporting.Builders;

public sealed class BiLogSheetDatasetBuilder : IReportDatasetBuilder<BiLogSheetReportData>
{
    public string ReportType => Shared.Contracts.Reporting.ReportType.BILogSheet;

    public async Task<(BiLogSheetReportData? data, ReportValidationResult validation)> BuildAsync(
        HsmsDbContext db,
        ReportRenderRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = new ReportValidationResult();
        if (request.FromUtc is null || request.ToUtc is null)
        {
            validation.AddError(ReportValidationCodes.MissingDateRange,
                "From and To dates are required for the BI log sheet.");
            return (null, validation);
        }

        var fromUtc = request.FromUtc.Value;
        var toUtc = request.ToUtc.Value;

        var query = SterilizationBiLogSheetFilter.WhereUsesBiologicalIndicator(db.Sterilizations.AsNoTracking())
            .Where(x => x.CycleDateTime >= fromUtc && x.CycleDateTime <= toUtc);

        if (!string.IsNullOrWhiteSpace(request.SterilizationTypeFilter))
        {
            query = query.Where(x => x.SterilizationType == request.SterilizationTypeFilter);
        }

        var raw = await query
            .OrderBy(x => x.CycleDateTime)
            .Select(x => new
            {
                x.CycleDateTime,
                x.CycleNo,
                x.SterilizerId,
                x.SterilizationType,
                x.BiLotNo,
                x.BiTimeIn,
                x.BiTimeOut,
                x.BiTimeInInitials,
                x.BiTimeOutInitials,
                x.BiResult,
                x.BiIncubatorTemp,
                x.BiProcessedResult24m,
                x.BiProcessedResult24h,
                x.BiControlResult24m,
                x.BiControlResult24h,
                x.OperatorName,
                x.Notes
            })
            .ToListAsync(cancellationToken);

        var sterilizerIds = raw.Select(r => r.SterilizerId).Distinct().ToList();
        var sterilizerLabels = sterilizerIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SterilizerUnits.AsNoTracking()
                .Where(s => sterilizerIds.Contains(s.SterilizerId))
                .ToDictionaryAsync(s => s.SterilizerId, s => s.SterilizerNumber, cancellationToken);

        var data = new BiLogSheetReportData
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            SterilizationTypeFilter = request.SterilizationTypeFilter,
            Rows = [.. raw.Select(r => new BiLogSheetReportRow
            {
                CycleDateTimeUtc = r.CycleDateTime,
                CycleNo = r.CycleNo,
                SterilizerNo = sterilizerLabels.GetValueOrDefault(r.SterilizerId) ?? r.SterilizerId.ToString(),
                SterilizationType = r.SterilizationType,
                BiLotNo = r.BiLotNo,
                BiTimeInUtc = r.BiTimeIn,
                BiTimeOutUtc = r.BiTimeOut,
                BiTimeInInitials = r.BiTimeInInitials,
                BiTimeOutInitials = r.BiTimeOutInitials,
                BiResult = r.BiResult,
                BiIncubatorTemp = r.BiIncubatorTemp,
                BiProcessedResult24m = r.BiProcessedResult24m,
                BiProcessedResult24h = r.BiProcessedResult24h,
                BiControlResult24m = r.BiControlResult24m,
                BiControlResult24h = r.BiControlResult24h,
                OperatorName = r.OperatorName,
                Notes = r.Notes
            })]
        };

        if (data.Rows.Count == 0)
        {
            validation.AddWarning("BI_NO_ROWS",
                "No BI rows found for the selected range. The report will print only the header.");
        }

        return (data, validation);
    }
}
