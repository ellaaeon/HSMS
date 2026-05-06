using System.Text.Json;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;

namespace HSMS.Persistence.Services;

public sealed class AuditService : IAuditService
{
    public Task AppendAsync(
        HsmsDbContext dbContext,
        string module,
        string entityName,
        string entityId,
        string action,
        int? actorAccountId,
        string? clientMachine,
        object? oldValues,
        object? newValues,
        Guid correlationId,
        CancellationToken cancellationToken)
    {
        var log = new AuditLog
        {
            EventAt = DateTime.UtcNow,
            Module = module,
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            ActorAccountId = actorAccountId,
            ClientMachine = clientMachine,
            CorrelationId = correlationId,
            OldValuesJson = oldValues is null ? null : JsonSerializer.Serialize(oldValues),
            NewValuesJson = newValues is null ? null : JsonSerializer.Serialize(newValues)
        };

        dbContext.AuditLogs.Add(log);
        return Task.CompletedTask;
    }
}
