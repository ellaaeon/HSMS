using HSMS.Application.Audit;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed class HsmsAuthService(IDbContextFactory<HsmsDbContext> dbFactory, IAuditService auditService)
{
    public async Task<(bool ok, LoginResponseDto? dto, string? error)> LoginAsync(LoginRequestDto request, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var account = await db.Accounts.SingleOrDefaultAsync(x => x.Username == request.Username, cancellationToken);

            if (account is null || !BCrypt.Net.BCrypt.Verify(request.Password, account.PasswordHash))
            {
                await LoginAuditHelper.LogFailedAttemptAsync(db, auditService, request.Username, request.ClientMachine, "invalid_credentials", cancellationToken);
                return (false, null, "Invalid username or password.");
            }

            if (!account.IsActive)
            {
                await LoginAuditHelper.LogFailedAttemptAsync(db, auditService, request.Username, request.ClientMachine, "inactive_account", cancellationToken);
                return (false, null, "Your account is inactive. Please contact administrator.");
            }

            account.LastLoginAt = DateTime.UtcNow;

            await auditService.AppendAsync(
                db,
                module: AuditModules.Security,
                entityName: AuditEntities.Account,
                entityId: account.AccountId.ToString(),
                action: AuditActions.LoginSuccess,
                actorAccountId: account.AccountId,
                clientMachine: request.ClientMachine,
                oldValues: null,
                newValues: new { accountId = account.AccountId, username = account.Username, role = account.Role },
                correlationId: Guid.NewGuid(),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            return (true, ToLoginResponse(account), null);
        }
        catch (SqlException ex) when (ex.Number == 207 && ex.Message.Contains("first_name", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "Database is missing staff profile columns. Run hsms-db/ddl/007_hsms_account_staff_profile.sql.");
        }
    }

    /// <summary>Creates a new Staff account (self-service portal). Does not create Admin accounts.</summary>
    public async Task<(bool ok, LoginResponseDto? dto, string? error)> RegisterStaffAsync(
        StaffRegistrationRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var username = request.Username.Trim();
        var password = request.Password;
        var confirm = request.ConfirmPassword;

        if (username.Length is < 3 or > 64)
        {
            return (false, null, "Username must be between 3 and 64 characters.");
        }

        if (password.Length < 8)
        {
            return (false, null, "Password must be at least 8 characters.");
        }

        if (!string.Equals(password, confirm, StringComparison.Ordinal))
        {
            return (false, null, "Password and confirmation do not match.");
        }

        var first = request.FirstName.Trim();
        var last = request.LastName.Trim();
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
        {
            return (false, null, "First name and last name are required.");
        }

        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@') < 1)
        {
            return (false, null, "A valid email address is required.");
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (await db.Accounts.AnyAsync(x => x.Username == username, cancellationToken))
            {
                return (false, null, "That username is already taken. Choose another or sign in.");
            }

            var entity = new AccountLogin
            {
                Username = username,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                Role = "Staff",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                FirstName = first,
                LastName = last,
                Email = email,
                Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
                Department = string.IsNullOrWhiteSpace(request.Department) ? null : request.Department.Trim(),
                JobTitle = string.IsNullOrWhiteSpace(request.JobTitle) ? null : request.JobTitle.Trim(),
                EmployeeId = string.IsNullOrWhiteSpace(request.EmployeeId) ? null : request.EmployeeId.Trim()
            };

            db.Accounts.Add(entity);
            await db.SaveChangesAsync(cancellationToken);

            await auditService.AppendAsync(
                db,
                module: AuditModules.Security,
                entityName: AuditEntities.Account,
                entityId: entity.AccountId.ToString(),
                action: AuditActions.AccountCreate,
                actorAccountId: entity.AccountId,
                clientMachine: request.ClientMachine,
                oldValues: null,
                newValues: new { accountId = entity.AccountId, username = entity.Username, role = entity.Role },
                correlationId: Guid.NewGuid(),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);

            return (true, ToLoginResponse(entity), null);
        }
        catch (SqlException ex) when (ex.Number == 207 && ex.Message.Contains("first_name", StringComparison.OrdinalIgnoreCase))
        {
            return (false, null, "Database is missing staff profile columns. Run hsms-db/ddl/007_hsms_account_staff_profile.sql.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 })
        {
            return (false, null, "That username is already taken.");
        }
    }

    /// <summary>Updates profile fields for the signed-in account (username and role are not changed here).</summary>
    public async Task<(bool ok, string? error)> UpdateMyProfileAsync(
        int accountId,
        StaffProfileDto profile,
        CancellationToken cancellationToken = default)
    {
        var first = profile.FirstName?.Trim() ?? string.Empty;
        var last = profile.LastName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
        {
            return (false, "First name and last name are required.");
        }

        var email = profile.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email) || email.IndexOf('@') < 1)
        {
            return (false, "A valid email address is required.");
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var account = await db.Accounts.FindAsync(new object[] { accountId }, cancellationToken);
            if (account is null)
            {
                return (false, "Account not found.");
            }

            var oldValues = new
            {
                account.FirstName,
                account.LastName,
                account.Email,
                account.Phone,
                account.Department,
                account.JobTitle,
                account.EmployeeId
            };

            account.FirstName = Clamp(first, 80);
            account.LastName = Clamp(last, 80);
            account.Email = Clamp(email, 128);
            account.Phone = ClampOptional(profile.Phone, 40);
            account.Department = ClampOptional(profile.Department, 128);
            account.JobTitle = ClampOptional(profile.JobTitle, 128);
            account.EmployeeId = ClampOptional(profile.EmployeeId, 32);

            await auditService.AppendAsync(
                db,
                module: AuditModules.Security,
                entityName: AuditEntities.Account,
                entityId: account.AccountId.ToString(),
                action: AuditActions.ProfileUpdate,
                actorAccountId: account.AccountId,
                clientMachine: Environment.MachineName,
                oldValues,
                new
                {
                    account.FirstName,
                    account.LastName,
                    account.Email,
                    account.Phone,
                    account.Department,
                    account.JobTitle,
                    account.EmployeeId
                },
                correlationId: Guid.NewGuid(),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            return (true, null);
        }
        catch (SqlException ex) when (ex.Number == 207 && ex.Message.Contains("first_name", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "Database is missing profile columns. Run hsms-db/ddl/007_hsms_account_staff_profile.sql.");
        }
    }

    private static string Clamp(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static string? ClampOptional(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static LoginResponseDto ToLoginResponse(AccountLogin account) =>
        new()
        {
            AccountId = account.AccountId,
            Username = account.Username,
            Role = account.Role,
            AccessToken = "local",
            ExpiresAtUtc = DateTime.UtcNow.AddHours(12),
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
}
