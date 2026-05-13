using HSMS.Application.Reporting.Datasets;
using HSMS.Persistence.Data;
using HSMS.Shared.Contracts.Reporting;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Reporting.Builders;

/// <summary>
/// Shared builder for Leak and Bowie-Dick reports. The render engine flips the title via <see cref="QaTestReportData.TestType"/>.
/// </summary>
public sealed class QaTestDatasetBuilder(string reportType, string expectedTestType) : IReportDatasetBuilder<QaTestReportData>
{
    public string ReportType { get; } = reportType;
    private readonly string _expectedTestType = expectedTestType;

    public async Task<(QaTestReportData? data, ReportValidationResult validation)> BuildAsync(
        HsmsDbContext db,
        ReportRenderRequestDto request,
        CancellationToken cancellationToken)
    {
        var validation = new ReportValidationResult();
        if (request.QaTestId is not int qaId)
        {
            validation.AddError(ReportValidationCodes.QaTestNotFound, "QA test id is required.");
            return (null, validation);
        }

        var qa = await db.QaTests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.QaTestId == qaId, cancellationToken);

        if (qa is null)
        {
            validation.AddError(ReportValidationCodes.QaTestNotFound, $"QA test #{qaId} not found.");
            return (null, validation);
        }

        if (!string.Equals(qa.TestType, _expectedTestType, StringComparison.OrdinalIgnoreCase))
        {
            validation.AddError(ReportValidationCodes.QaTestNotFound,
                $"QA test #{qaId} is a \"{qa.TestType}\" test; cannot render as \"{_expectedTestType}\".");
            return (null, validation);
        }

        var cycle = await db.Sterilizations.AsNoTracking()
            .Where(x => x.SterilizationId == qa.SterilizationId)
            .Select(x => new { x.CycleNo, x.SterilizerId })
            .SingleOrDefaultAsync(cancellationToken);

        if (cycle is null)
        {
            validation.AddError(ReportValidationCodes.CycleNotFound,
                $"Linked cycle #{qa.SterilizationId} not found.");
            return (null, validation);
        }

        var sterilizerNo = await db.SterilizerUnits.AsNoTracking()
            .Where(s => s.SterilizerId == cycle.SterilizerId)
            .Select(s => s.SterilizerNumber)
            .SingleOrDefaultAsync(cancellationToken) ?? cycle.SterilizerId.ToString();

        var data = new QaTestReportData
        {
            TestType = qa.TestType,
            QaTestId = qa.QaTestId,
            CycleNo = cycle.CycleNo,
            SterilizerNo = sterilizerNo,
            TestDateTimeUtc = qa.TestDateTime,
            Result = qa.Result,
            MeasuredValue = qa.MeasuredValue,
            Unit = qa.Unit,
            PerformedBy = qa.PerformedBy,
            Notes = qa.Notes,
            ApprovalStatus = "Pending"
        };

        return (data, validation);
    }
}
