using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Api.Infrastructure.Auth;
using HSMS.Api.Infrastructure.Files;
using HSMS.Api.Infrastructure.Maintenance;
using HSMS.Api.Infrastructure.Security;
using HSMS.Api.Reporting;
using HSMS.Application.Exports;
using HSMS.Application.Reporting;
using HSMS.Application.Reporting.Builders;
using HSMS.Application.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));

builder.Services.AddDbContext<HsmsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer")));
builder.Services.AddDbContextFactory<HsmsDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServer")));

builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpContextCurrentUserAccessor>();

// Module 1 - Reporting + Printing pipeline.
builder.Services.AddSingleton<IReportRenderEngine, QuestPdfReportRenderEngine>();
builder.Services.AddScoped<IReceiptImageProvider, ApiReceiptImageProvider>();
builder.Services.AddScoped<IReportManager, ReportManager>();

// Module 2 - Receipt derivation pipeline.
builder.Services.AddSingleton<ReceiptDerivationQueue>();
builder.Services.AddScoped<IReceiptDerivationService, ReceiptDerivationService>();
builder.Services.AddHostedService<ReceiptDerivationHostedService>();

// Module 7 - Reusable Excel export service (used by API and Desktop).
builder.Services.AddSingleton<IExcelExportService, ClosedXmlExcelExportService>();

// Module 8 - Operations hardening: scheduled reconciliation + cleanup.
builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection(MaintenanceOptions.SectionName));
builder.Services.AddScoped<IReceiptReconciliationService, ReceiptReconciliationService>();
builder.Services.AddHostedService<MaintenanceHostedService>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await SeedDevelopmentAdminAsync(app.Services);
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();

static async Task SeedDevelopmentAdminAsync(IServiceProvider services)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<HsmsDbContext>();
    try
    {
        if (!await db.Accounts.AnyAsync())
        {
            db.Accounts.Add(new AccountLogin
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
                Role = "Admin",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        if (!await db.SterilizerUnits.AnyAsync())
        {
            db.SterilizerUnits.Add(new SterilizerUnit
            {
                SterilizerNumber = "S1",
                Model = "Dev",
                IsActive = true
            });
            await db.SaveChangesAsync();
        }
    }
    catch
    {
        // Database not reachable or schema not applied yet; ignore for first run.
    }
}
