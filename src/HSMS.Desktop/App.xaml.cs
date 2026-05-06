using System.Windows;
using System.Windows.Threading;
using HSMS.Application.Security;
using HSMS.Application.Services;
using HSMS.Desktop.Services;
using HSMS.Persistence.Data;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HSMS.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Something went wrong in the desktop app.\n\n" + e.Exception.ToString(),
            "HSMS",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
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

        var services = new ServiceCollection();
        services.AddDbContextFactory<HsmsDbContext>(options => options.UseSqlServer(connectionString));
        services.AddSingleton<IAuditService, AuditService>();
        services.AddSingleton<WpfSessionUserAccessor>();
        services.AddSingleton<ICurrentUserAccessor>(sp => sp.GetRequiredService<WpfSessionUserAccessor>());
        services.AddSingleton<HsmsAuthService>();
        services.AddSingleton<IHsmsDataService, HsmsLocalDataService>();

        _serviceProvider = services.BuildServiceProvider();

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

            var main = new MainWindow(data, dto, auth);
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
