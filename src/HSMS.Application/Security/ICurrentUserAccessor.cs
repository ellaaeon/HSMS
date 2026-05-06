namespace HSMS.Application.Security;

/// <summary>
/// Abstraction so the same authorization rules run in ASP.NET Core today and in standalone WPF later.
/// </summary>
public interface ICurrentUserAccessor
{
    CurrentUser? GetCurrentUser();
}
