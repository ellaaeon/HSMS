using HSMS.Persistence.Data;

namespace HSMS.Persistence.Services;

/// <summary>
/// Stages an audit row on the supplied context. Caller commits in the same transaction as business changes.
/// </summary>
public interface IAuditService
{
    Task AppendAsync(
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
        CancellationToken cancellationToken);
}
