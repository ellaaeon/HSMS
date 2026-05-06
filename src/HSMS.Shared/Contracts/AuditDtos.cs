namespace HSMS.Shared.Contracts;

public sealed class AuditLogRowDto
{
    public long AuditId { get; set; }
    public DateTime EventAtUtc { get; set; }
    public int? ActorAccountId { get; set; }
    public string? ActorUsername { get; set; }
    public string Module { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? ClientMachine { get; set; }
}

public sealed class AuditLogQueryDto
{
    public DateTime? FromUtc { get; set; }
    public DateTime? ToUtc { get; set; }
    /// <summary>Matches actor username or login-attempt entity id (substring, case-insensitive).</summary>
    public string? UserSearch { get; set; }
    public int? ActorAccountId { get; set; }
    public string? ModuleFilter { get; set; }
    public string? ActionFilter { get; set; }
    public int Take { get; set; } = 250;
}

public sealed class AuditSecurityAlertDto
{
    public long AuditId { get; set; }
    public DateTime EventAtUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public string? ClientMachine { get; set; }
}

public sealed class AccountRecentActivityDto
{
    public int AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Role { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}

public sealed class AuditVolumeRowDto
{
    public int? ActorAccountId { get; set; }
    public string? ActorUsername { get; set; }
    public int UpdateCount { get; set; }
}
