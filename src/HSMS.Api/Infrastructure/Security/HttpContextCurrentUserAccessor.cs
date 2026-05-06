using System.Security.Claims;
using HSMS.Application.Security;
using Microsoft.AspNetCore.Http;

namespace HSMS.Api.Infrastructure.Security;

public sealed class HttpContextCurrentUserAccessor(IHttpContextAccessor httpContextAccessor) : ICurrentUserAccessor
{
    public CurrentUser? GetCurrentUser()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var idValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idValue, out var accountId))
        {
            return null;
        }

        var username = principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(role))
        {
            return null;
        }

        return new CurrentUser(accountId, username, role);
    }
}
