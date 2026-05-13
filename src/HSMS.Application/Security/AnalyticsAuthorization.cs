namespace HSMS.Application.Security;

/// <summary>
/// Analytics permissions scaffold. Today HSMS is role-based; we map roles to permissions here so
/// UI hiding is consistent with service-level enforcement.
/// </summary>
public static class AnalyticsAuthorization
{
    public static AuthorizationDenied? RequireView(CurrentUser? user) =>
        RoleAuthorization.RequireAuthenticated(user);

    public static AuthorizationDenied? RequireExport(CurrentUser? user) =>
        RoleAuthorization.RequireAuthenticated(user);

    public static AuthorizationDenied? RequirePrint(CurrentUser? user) =>
        RoleAuthorization.RequireAuthenticated(user);

    public static AuthorizationDenied? RequireViewBi(CurrentUser? user) =>
        RoleAuthorization.RequireAuthenticated(user);

    public static AuthorizationDenied? RequireViewQa(CurrentUser? user) =>
        RoleAuthorization.RequireAuthenticated(user);
}

