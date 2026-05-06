using HSMS.Application.Security;

namespace HSMS.Desktop.Services;

/// <summary>
/// Holds the signed-in user for the WPF process. Implements <see cref="ICurrentUserAccessor"/> for shared application services.
/// </summary>
public sealed class WpfSessionUserAccessor : ICurrentUserAccessor
{
    private CurrentUser? _user;

    public void SetUser(CurrentUser user) => _user = user;

    public void Clear() => _user = null;

    public CurrentUser? GetCurrentUser() => _user;
}
