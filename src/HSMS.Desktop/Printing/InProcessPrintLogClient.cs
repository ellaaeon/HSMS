using System.Text.Json;
using HSMS.Application.Security;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts.Reporting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Desktop.Printing;

/// <summary>
/// Standalone-deployment implementation: writes a row to <c>print_logs</c> directly via EF Core,
/// applying the same idempotency semantics (correlation_id unique) as the API endpoint.
/// </summary>
public sealed class InProcessPrintLogClient(
    IDbContextFactory<HsmsDbContext> dbFactory,
    IAuditService auditService,
    ICurrentUserAccessor currentUser) : IPrintLogClient
{
    public async Task<long> RecordPrintAsync(PrintLogCreateDto request, CancellationToken cancellationToken)
    {
        if (request.CorrelationId == Guid.Empty)
        {
            throw new InvalidOperationException("Correlation id is required to log a print.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var existing = await db.PrintLogs.AsNoTracking()
            .Where(x => x.CorrelationId == request.CorrelationId)
            .Select(x => (long?)x.PrintLogId)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is long existingId)
        {
            return existingId;
        }

        var entity = new PrintLog
        {
            PrintedAt = DateTime.UtcNow,
            PrintedBy = currentUser.GetCurrentUser()?.AccountId,
            ReportType = request.ReportType,
            SterilizationId = request.SterilizationId,
            QaTestId = request.QaTestId,
            PrinterName = request.PrinterName,
            Copies = request.Copies,
            ParametersJson = request.Parameters is null ? null : JsonSerializer.Serialize(request.Parameters),
            CorrelationId = request.CorrelationId,
            ReportVersion = "questpdf-1",
            StationId = request.ClientMachine ?? Environment.MachineName
        };

        db.PrintLogs.Add(entity);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await auditService.AppendAsync(db,
                module: "Reporting",
                entityName: "print_logs",
                entityId: entity.PrintLogId.ToString(),
                action: "Print",
                actorAccountId: entity.PrintedBy,
                clientMachine: entity.StationId,
                oldValues: null,
                newValues: new { entity.ReportType, entity.Copies, entity.PrinterName, entity.SterilizationId, entity.QaTestId },
                correlationId: request.CorrelationId,
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return entity.PrintLogId;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            await tx.RollbackAsync(cancellationToken);
            // Race - another writer beat us; surface that row's id.
            return await db.PrintLogs.AsNoTracking()
                .Where(x => x.CorrelationId == request.CorrelationId)
                .Select(x => x.PrintLogId)
                .FirstAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
