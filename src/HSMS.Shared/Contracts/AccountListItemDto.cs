namespace HSMS.Shared.Contracts;

public sealed class AccountListItemDto
{
    public int AccountId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Most recent audit event for this account (profile, sign-in, registration, etc.).</summary>
    public DateTime? LatestActivityAtUtc { get; set; }

    public string? LatestActivitySummary { get; set; }

    /// <summary>Preformatted for grids when <see cref="LatestActivityAtUtc"/> is null.</summary>
    public string LatestActivityDisplay { get; set; } = "—";
}
