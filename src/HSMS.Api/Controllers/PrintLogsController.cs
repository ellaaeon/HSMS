using System.Security.Claims;
using System.Text.Json;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HSMS.Api.Controllers;

public sealed class PrintLogRequestDto
{
    public string ReportType { get; set; } = string.Empty;
    public int? SterilizationId { get; set; }
    public int? QaTestId { get; set; }
    public string? PrinterName { get; set; }
    public int Copies { get; set; } = 1;
    public object? Parameters { get; set; }
    public string? ClientMachine { get; set; }
}

[ApiController]
[Route("api/print-logs")]
[Authorize]
public sealed class PrintLogsController(HsmsDbContext dbContext, IAuditService auditService) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<object>> Create(PrintLogRequestDto request, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid();
        var entity = new PrintLog
        {
            PrintedAt = DateTime.UtcNow,
            PrintedBy = GetActorId(),
            ReportType = request.ReportType,
            SterilizationId = request.SterilizationId,
            QaTestId = request.QaTestId,
            PrinterName = request.PrinterName,
            Copies = request.Copies,
            ParametersJson = request.Parameters is null ? null : JsonSerializer.Serialize(request.Parameters),
            CorrelationId = correlationId
        };

        dbContext.PrintLogs.Add(entity);
        await using (var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);

                await auditService.AppendAsync(dbContext, "Reporting", "print_logs", entity.PrintLogId.ToString(), "Print", GetActorId(),
                    request.ClientMachine, null, new { entity.ReportType, entity.Copies, entity.PrinterName }, correlationId, cancellationToken);

                await dbContext.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return Ok(new { printLogId = entity.PrintLogId });
    }

    private int? GetActorId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }
}
