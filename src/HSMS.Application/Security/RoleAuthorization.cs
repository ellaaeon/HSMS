namespace HSMS.Application.Security;

/// <summary>
/// Central role checks for sensitive operations. Controllers/services call these methods;
/// UI hiding alone is never sufficient.
/// </summary>
public static class RoleAuthorization
{
    public const string AdminRoleName = "Admin";
    public const string StaffRoleName = "Staff";

    /// <summary>Returns null when the caller is allowed; otherwise a machine-readable denial.</summary>
    public static AuthorizationDenied? RequireAdmin(CurrentUser? user)
    {
        if (user is null)
        {
            return new AuthorizationDenied("AUTH_UNAUTHORIZED", "Sign in is required.");
        }

        if (!string.Equals(user.Role, AdminRoleName, StringComparison.Ordinal))
        {
            return new AuthorizationDenied("AUTH_FORBIDDEN", "This operation requires an administrator.");
        }

        return null;
    }

    /// <summary>Any authenticated account (Admin or Staff).</summary>
    public static AuthorizationDenied? RequireAuthenticated(CurrentUser? user)
    {
        if (user is null)
        {
            return new AuthorizationDenied("AUTH_UNAUTHORIZED", "Sign in is required.");
        }

        if (!string.Equals(user.Role, AdminRoleName, StringComparison.Ordinal)
            && !string.Equals(user.Role, StaffRoleName, StringComparison.Ordinal))
        {
            return new AuthorizationDenied("AUTH_FORBIDDEN", "This account role is not permitted to use the system.");
        }

        return null;
    }
}

public readonly record struct AuthorizationDenied(string Code, string Message);
