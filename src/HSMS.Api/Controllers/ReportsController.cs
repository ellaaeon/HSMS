using System.Text.Json;
using HSMS.Application.Reporting;
using HSMS.Shared.Contracts;
using HSMS.Shared.Contracts.Reporting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMS.Api.Controllers;

/// <summary>
/// Centralized reporting endpoint. Two flavors:
/// - <c>POST /api/reports/render</c>: returns JSON envelope with PDF Base64 (preview-mode in the desktop client).
/// - <c>POST /api/reports/render/pdf</c>: returns the raw PDF stream so callers (e.g. browsers, third-party automation) can stream-print it.
/// </summary>
[ApiController]
[Route("api/reports")]
[Authorize]
public sealed class ReportsController(IReportManager reportManager) : ControllerBase
{
    [HttpPost("render")]
    public async Task<ActionResult<ReportRenderResponseDto>> Render(
        [FromBody] ReportRenderRequestDto request,
        CancellationToken cancellationToken)
    {
        var outcome = await reportManager.RenderAsync(request, cancellationToken);
        if (!outcome.Success)
        {
            return BadRequest(new ApiError
            {
                Code = "REPORT_VALIDATION_FAILED",
                Message = outcome.Validation.FirstErrorMessage(),
                Details = new
                {
                    errors = outcome.Validation.Errors,
                    warnings = outcome.Validation.Warnings
                }
            });
        }

        return Ok(new ReportRenderResponseDto
        {
            ReportType = outcome.ReportType,
            CorrelationId = outcome.CorrelationId,
            PdfBase64 = Convert.ToBase64String(outcome.PdfBytes),
            PageCount = outcome.PageCount,
            PdfSizeBytes = outcome.PdfBytes.LongLength,
            RenderedAtUtc = outcome.RenderedAtUtc,
            Warnings = outcome.Validation.Warnings.ToList()
        });
    }

    [HttpPost("render/pdf")]
    public async Task<IActionResult> RenderPdf(
        [FromBody] ReportRenderRequestDto request,
        CancellationToken cancellationToken)
    {
        var outcome = await reportManager.RenderAsync(request, cancellationToken);
        if (!outcome.Success)
        {
            return BadRequest(new ApiError
            {
                Code = "REPORT_VALIDATION_FAILED",
                Message = outcome.Validation.FirstErrorMessage(),
                Details = new
                {
                    errors = outcome.Validation.Errors,
                    warnings = outcome.Validation.Warnings
                }
            });
        }

        // Surface metadata via headers so streaming clients don't need to parse the body.
        Response.Headers[ReportResponseHeaders.CorrelationId] = outcome.CorrelationId.ToString();
        Response.Headers[ReportResponseHeaders.PageCount] = outcome.PageCount.ToString();
        if (outcome.Validation.Warnings.Count > 0)
        {
            Response.Headers[ReportResponseHeaders.Warnings] = JsonSerializer.Serialize(outcome.Validation.Warnings);
        }

        var fileName = $"{outcome.ReportType}_{outcome.CorrelationId:N}.pdf";
        return File(outcome.PdfBytes, "application/pdf", fileName);
    }
}
