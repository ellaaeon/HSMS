namespace HSMS.Application.Security;

/// <summary>
/// Authenticated operator context for authorization checks in the application layer.
/// Populated from JWT (API) or session (standalone desktop) — same shape everywhere.
/// </summary>
public sealed record CurrentUser(int AccountId, string Username, string Role);
