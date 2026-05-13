using HSMS.Application.Reporting;
using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Desktop.Printing;

/// <summary>
/// Standalone-WPF implementation of <see cref="IReportClient"/> that calls the report manager in-process.
/// </summary>
public sealed class InProcessReportClient(IReportManager reportManager) : IReportClient
{
    public async Task<RenderClientResult> RenderAsync(ReportRenderRequestDto request, CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await reportManager.RenderAsync(request, cancellationToken);
            return new RenderClientResult
            {
                Success = outcome.Success,
                PdfBytes = outcome.PdfBytes,
                PageCount = outcome.PageCount,
                ErrorMessage = outcome.Success ? null : outcome.Validation.FirstErrorMessage(),
                Warnings = outcome.Validation.Warnings.ToList()
            };
        }
        catch (Exception ex)
        {
            return new RenderClientResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
