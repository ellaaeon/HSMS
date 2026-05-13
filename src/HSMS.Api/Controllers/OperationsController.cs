using HSMS.Api.Infrastructure.Maintenance;
using HSMS.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using HSMS.Shared.Contracts;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/operations")]
[Authorize]
public sealed class OperationsController(
    IReceiptReconciliationService reconciliationService,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpPost("reconcile-receipts")]
    public async Task<ActionResult<ReceiptReconciliationResult>> ReconcileReceipts(CancellationToken cancellationToken)
    {
        if (Forbid403() is { } denied) return denied;
        var result = await reconciliationService.ReconcileAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("cleanup-derived")]
    public async Task<ActionResult<object>> CleanupDerivedAssets(CancellationToken cancellationToken)
    {
        if (Forbid403() is { } denied) return denied;
        var deleted = await reconciliationService.CleanupOrphanedDerivedAssetsAsync(cancellationToken);
        return Ok(new { deleted });
    }

    private ActionResult? Forbid403()
    {
        if (RoleAuthorization.RequireAdmin(currentUser.GetCurrentUser()) is { } denied)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new ApiError { Code = denied.Code, Message = denied.Message });
        }
        return null;
    }
}
