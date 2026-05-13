using HSMS.Application.Reporting.Builders;
using HSMS.Application.Reporting.Datasets;
using HSMS.Persistence.Data;
using HSMS.Shared.Contracts.Reporting;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Reporting;

/// <summary>
/// Orchestrates: validate request -> resolve builder -> build dataset -> validate dataset -> render PDF.
/// Holds no per-request state; safe to register as a singleton or scoped.
/// </summary>
public sealed class ReportManager(
    IDbContextFactory<HsmsDbContext> dbContextFactory,
    IReportRenderEngine renderEngine,
    IReceiptImageProvider receiptImageProvider) : IReportManager
{
    public async Task<ReportRenderOutcome> RenderAsync(ReportRenderRequestDto request, CancellationToken cancellationToken)
    {
        var outcome = new ReportRenderOutcome
        {
            ReportType = request.ReportType,
            CorrelationId = request.CorrelationId == Guid.Empty ? Guid.NewGuid() : request.CorrelationId,
            RenderedAtUtc = DateTime.UtcNow
        };

        if (!ReportType.IsKnown(request.ReportType))
        {
            outcome.Validation.AddError(ReportValidationCodes.ReportTypeUnknown,
                $"Unknown report type \"{request.ReportType}\".");
            return outcome;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        switch (request.ReportType)
        {
            case ReportType.LoadRecord:
            {
                var builder = new LoadRecordDatasetBuilder(receiptImageProvider);
                var (data, validation) = await builder.BuildAsync(db, request, cancellationToken);
                outcome.Validation = validation;
                if (validation.HasErrors || data is null) return outcome;
                var rendered = await renderEngine.RenderLoadRecordAsync(data, cancellationToken);
                outcome.PdfBytes = rendered.PdfBytes;
                outcome.PageCount = rendered.PageCount;
                outcome.Success = true;
                return outcome;
            }
            case ReportType.BILogSheet:
            {
                var builder = new BiLogSheetDatasetBuilder();
                var (data, validation) = await builder.BuildAsync(db, request, cancellationToken);
                outcome.Validation = validation;
                if (validation.HasErrors || data is null) return outcome;
                var rendered = await renderEngine.RenderBiLogSheetAsync(data, cancellationToken);
                outcome.PdfBytes = rendered.PdfBytes;
                outcome.PageCount = rendered.PageCount;
                outcome.Success = true;
                return outcome;
            }
            case ReportType.LeakTest:
            case ReportType.BowieDick:
            {
                var expected = request.ReportType == ReportType.LeakTest ? "Leak" : "BowieDick";
                var builder = new QaTestDatasetBuilder(request.ReportType, expected);
                var (data, validation) = await builder.BuildAsync(db, request, cancellationToken);
                outcome.Validation = validation;
                if (validation.HasErrors || data is null) return outcome;
                var rendered = await renderEngine.RenderQaTestAsync(data, cancellationToken);
                outcome.PdfBytes = rendered.PdfBytes;
                outcome.PageCount = rendered.PageCount;
                outcome.Success = true;
                return outcome;
            }
        }

        outcome.Validation.AddError(ReportValidationCodes.ReportTypeUnknown,
            $"No builder is registered for report type \"{request.ReportType}\".");
        return outcome;
    }
}
