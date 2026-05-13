using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using HSMS.Application.Exports;
using HSMS.Application.Reporting;
using HSMS.Application.Reporting.Builders;
using HSMS.Application.Security;
using HSMS.Application.Services;
using HSMS.Desktop.Printing;
using HSMS.Desktop.Reporting;
using HSMS.Desktop.Services;
using HSMS.Persistence.Data;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;

namespace HSMS.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private static int _isHandlingFatal;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = WriteCrashLog(e.Exception, "DispatcherUnhandledException");
        MessageBox.Show(
            "Something went wrong.\n\n" +
            "HSMS will keep running, but the last action could not be completed.\n\n" +
            (logPath is null ? "" : $"A crash log was saved to:\n{logPath}\n\n"),
            "HSMS",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // Prevent process termination on finalizer thread.
        var logPath = WriteCrashLog(e.Exception, "UnobservedTaskException");
        try
        {
            Current?.Dispatcher.BeginInvoke(() =>
            {
                MessageBox.Show(
                    "Something went wrong in the background.\n\n" +
                    (logPath is null ? "" : $"A crash log was saved to:\n{logPath}\n\n"),
                    "HSMS",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            });
        }
        catch
        {
            // Ignore UI failures; log already attempted.
        }
        e.SetObserved();
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // This is typically fatal; log and show best-effort message once.
        if (Interlocked.Exchange(ref _isHandlingFatal, 1) != 0)
        {
            return;
        }

        var ex = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception.");
        var logPath = WriteCrashLog(ex, "AppDomainUnhandledException");
        try
        {
            MessageBox.Show(
                "A fatal error occurred and HSMS needs to close.\n\n" +
                (logPath is null ? "" : $"A crash log was saved to:\n{logPath}\n\n"),
                "HSMS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Ignore UI failures.
        }
    }

    private static string? WriteCrashLog(Exception ex, string source)
    {
        try
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HSMS",
                "crash-logs");
            Directory.CreateDirectory(baseDir);

            var file = $"hsms-desktop-crash-{DateTime.Now:yyyyMMdd-HHmmss}-{source}.txt";
            var path = Path.Combine(baseDir, file);

            var sb = new StringBuilder();
            sb.AppendLine($"Timestamp: {DateTime.Now:O}");
            sb.AppendLine($"Source: {source}");
            sb.AppendLine($"Machine: {Environment.MachineName}");
            sb.AppendLine($"OS: {Environment.OSVersion}");
            sb.AppendLine($"Process: {Process.GetCurrentProcess().ProcessName} ({Environment.ProcessId})");
            sb.AppendLine();
            sb.AppendLine(ex.ToString());
            File.WriteAllText(path, sb.ToString());
            return path;
        }
        catch
        {
            return null;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        var connectionString = config.GetConnectionString("SqlServer");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            MessageBox.Show(
                "Database connection string is missing.\n\nSet ConnectionStrings:SqlServer in appsettings.json next to HSMS.Desktop.exe (see README).",
                "HSMS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // Normalize a couple of defaults that commonly break desktop installs.
        // SqlClient defaults Encrypt=true on newer stacks; TrustServerCertificate avoids local self-signed cert failures.
        try
        {
            var csb = new SqlConnectionStringBuilder(connectionString);
            if (!csb.ContainsKey("TrustServerCertificate"))
            {
                csb.TrustServerCertificate = true;
            }
            if (!csb.ContainsKey("Encrypt"))
            {
                csb.Encrypt = true;
            }
            connectionString = csb.ConnectionString;
        }
        catch
        {
            // If parsing fails, keep original connection string and let EF/SqlClient surface an error.
        }

        var services = new ServiceCollection();
        services.AddDbContextFactory<HsmsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<WpfSessionUserAccessor>();
        services.AddSingleton<ICurrentUserAccessor>(sp => sp.GetRequiredService<WpfSessionUserAccessor>());
        services.AddSingleton<HsmsAuthService>();
        services.AddSingleton<IHsmsDataService, HsmsLocalDataService>();

        // Module 1 - Reporting + Printing pipeline (in-process; same engine the API uses).
        services.AddSingleton<IReportRenderEngine, QuestPdfReportRenderEngine>();
        services.AddSingleton<IReceiptImageProvider, LocalDiskReceiptImageProvider>();
        services.AddSingleton<IReportManager, ReportManager>();
        services.AddSingleton<IReportClient, InProcessReportClient>();
        services.AddSingleton<PdfRasterizer>();
        services.AddSingleton<IPrinterService, WindowsPrinterService>();
        services.AddSingleton<IPrintLogClient, InProcessPrintLogClient>();
        services.AddSingleton<IPrintQueueService, PrintQueueService>();
        services.AddSingleton<PrintReportCoordinator>();
        services.AddSingleton<IExcelExportService, ClosedXmlExcelExportService>();
        services.AddSingleton<IExcelWorkbookExportService, ClosedXmlWorkbookExportService>();

        _serviceProvider = services.BuildServiceProvider();

        // Preflight database connectivity so the user gets a clear actionable message instead of a generic login error.
        try
        {
            var dbFactory = _serviceProvider.GetRequiredService<IDbContextFactory<HsmsDbContext>>();
            using var db = dbFactory.CreateDbContext();
            db.Database.OpenConnection();
            db.Database.CloseConnection();
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            var exeConfigPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            MessageBox.Show(
                "HSMS cannot connect to the SQL Server database.\n\n" +
                $"Error: {detail}\n\n" +
                $"Config file (expected):\n{exeConfigPath}\n\n" +
                "Fix the connection string (ConnectionStrings:SqlServer) and try again.",
                "HSMS",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            _serviceProvider.Dispose();
            _serviceProvider = null;
            Shutdown();
            return;
        }

        var auth = _serviceProvider.GetRequiredService<HsmsAuthService>();
        var allowSelfRegistration = config.GetValue("Portal:AllowSelfRegistration", true);
        var accessor = _serviceProvider.GetRequiredService<WpfSessionUserAccessor>();
        var data = _serviceProvider.GetRequiredService<IHsmsDataService>();

        var usePortalNext = false;
        while (true)
        {
            var dto = usePortalNext
                ? AuthenticateViaPortal(auth, allowSelfRegistration)
                : AuthenticateDirectLogin(auth, allowSelfRegistration);

            usePortalNext = false;

            if (dto is null)
            {
                _serviceProvider.Dispose();
                _serviceProvider = null;
                Shutdown();
                return;
            }

            accessor.SetUser(new CurrentUser(dto.AccountId, dto.Username, dto.Role));

            var coordinator = _serviceProvider.GetRequiredService<PrintReportCoordinator>();
            var excelExport = _serviceProvider.GetRequiredService<IExcelExportService>();
            var workbookExport = _serviceProvider.GetRequiredService<IExcelWorkbookExportService>();
            var main = new MainWindow(data, dto, auth, coordinator, excelExport, workbookExport);
            MainWindow = main;
            main.ShowDialog();

            accessor.Clear();

            if (main.ReturnToPortal)
            {
                usePortalNext = true;
                continue;
            }

            break;
        }

        _serviceProvider.Dispose();
        _serviceProvider = null;
        Shutdown();
    }

    /// <summary>First launch: sign-in screen only (no portal step).</summary>
    private static LoginResponseDto? AuthenticateDirectLogin(HsmsAuthService auth, bool allowSelfRegistration)
    {
        var login = new LoginWindow(auth, allowSelfRegistration) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
        if (login.ShowDialog() == true && login.Result is not null)
        {
            return login.Result;
        }

        return null;
    }

    /// <summary>After logout: staff portal (sign in or create account), then login/register flows.</summary>
    private static LoginResponseDto? AuthenticateViaPortal(HsmsAuthService auth, bool allowSelfRegistration)
    {
        while (true)
        {
            var portal = new PortalLandingWindow(allowSelfRegistration) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
            if (portal.ShowDialog() != true)
            {
                return null;
            }

            if (portal.Outcome == PortalOutcome.SignIn)
            {
                var login = new LoginWindow(auth, allowSelfRegistration) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                if (login.ShowDialog() == true && login.Result is not null)
                {
                    return login.Result;
                }

                continue;
            }

            if (portal.Outcome == PortalOutcome.CreateAccount)
            {
                var reg = new CreateAccountWindow(auth) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                if (reg.ShowDialog() == true && reg.RegisteredSession is not null)
                {
                    return reg.RegisteredSession;
                }

                continue;
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
