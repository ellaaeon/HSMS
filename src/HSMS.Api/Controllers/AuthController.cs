using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HSMS.Application.Audit;
using HSMS.Persistence.Data;
using HSMS.Api.Infrastructure.Auth;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace HSMS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    HsmsDbContext dbContext,
    IOptions<JwtOptions> jwtOptions,
    IAuditService auditService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .SingleOrDefaultAsync(x => x.Username == request.Username, cancellationToken);

        var clientMachine = request.ClientMachine
                            ?? HttpContext.Connection.RemoteIpAddress?.ToString();

        if (account is null || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
        {
            await LoginAuditHelper.LogFailedAttemptAsync(dbContext, auditService, request.Username, clientMachine,
                "invalid_credentials", cancellationToken);
            return Unauthorized(new ApiError { Code = "AUTH_INVALID_CREDENTIALS", Message = "Invalid username or password." });
        }

        if (!account.IsActive)
        {
            await LoginAuditHelper.LogFailedAttemptAsync(dbContext, auditService, request.Username, clientMachine,
                "inactive_account", cancellationToken);
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiError { Code = "AUTH_INACTIVE_ACCOUNT", Message = "Your account is inactive. Please contact administrator." });
        }

        account.LastLoginAt = DateTime.UtcNow;

        var token = CreateToken(account.AccountId, account.Username, account.Role);
        var response = new LoginResponseDto
        {
            AccountId = account.AccountId,
            Username = account.Username,
            Role = account.Role,
            AccessToken = token,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(jwtOptions.Value.ExpiryMinutes),
            Profile = new StaffProfileDto
            {
                FirstName = account.FirstName,
                LastName = account.LastName,
                Email = account.Email,
                Phone = account.Phone,
                Department = account.Department,
                JobTitle = account.JobTitle,
                EmployeeId = account.EmployeeId
            }
        };

        await auditService.AppendAsync(dbContext,
            module: AuditModules.Security,
            entityName: AuditEntities.Account,
            entityId: account.AccountId.ToString(),
            action: AuditActions.LoginSuccess,
            actorAccountId: account.AccountId,
            clientMachine: clientMachine,
            oldValues: null,
            newValues: new { accountId = account.AccountId, username = account.Username, role = account.Role },
            correlationId: Guid.NewGuid(),
            cancellationToken);

        /* One SaveChanges so login timestamp and audit row commit together. */
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(response);
    }

    private string CreateToken(int accountId, string username, string role)
    {
        var jwt = jwtOptions.Value;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, accountId.ToString()),
            new(ClaimTypes.Name, username),
            new(ClaimTypes.Role, role)
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: jwt.Issuer,
            audience: jwt.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(jwt.ExpiryMinutes),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
