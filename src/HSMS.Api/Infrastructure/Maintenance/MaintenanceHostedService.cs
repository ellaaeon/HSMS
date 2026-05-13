using Microsoft.Extensions.Options;

namespace HSMS.Api.Infrastructure.Maintenance;

public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    /// <summary>If false, the hosted scheduler skips reconciliation runs entirely (e.g. for development).</summary>
    public bool ReconciliationEnabled { get; set; } = true;

    /// <summary>If false, derived-asset cleanup never runs.</summary>
    public bool CleanupEnabled { get; set; } = true;

    /// <summary>How often the scheduler wakes up and considers running jobs.</summary>
    public TimeSpan SchedulerInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Minimum time between reconciliation runs.</summary>
    public TimeSpan ReconciliationInterval { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Minimum time between derived-asset cleanups.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromDays(7);
}

/// <summary>
/// Module 8 - daily reconciliation + weekly cleanup driver. Each tick consults a small in-memory
/// schedule; we deliberately keep this in-process and best-effort (no quartz dependency) until ops
/// volumes justify a dedicated job runner.
/// </summary>
public sealed class MaintenanceHostedService(
    IServiceProvider services,
    IOptions<MaintenanceOptions> options,
    ILogger<MaintenanceHostedService> logger) : BackgroundService
{
    private DateTime _lastReconciliationUtc = DateTime.MinValue;
    private DateTime _lastCleanupUtc = DateTime.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Maintenance scheduler starting. interval={Interval}", options.Value.SchedulerInterval);
        var interval = options.Value.SchedulerInterval > TimeSpan.Zero
            ? options.Value.SchedulerInterval
            : TimeSpan.FromHours(1);

        // Light delay to let the API finish booting before we start scanning the FS.
        try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); } catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            try
            {
                if (options.Value.ReconciliationEnabled && now - _lastReconciliationUtc >= options.Value.ReconciliationInterval)
                {
                    await using var scope = services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IReceiptReconciliationService>();
                    var result = await svc.ReconcileAsync(stoppingToken);
                    _lastReconciliationUtc = now;
                    if (result.MissingOriginals > 0 || result.MissingDerivedAssets > 0 || result.OrphanFilesOnDisk > 0)
                    {
                        logger.LogWarning("Reconciliation findings detected: {Count} entries (see findings list).", result.Findings.Count);
                    }
                }

                if (options.Value.CleanupEnabled && now - _lastCleanupUtc >= options.Value.CleanupInterval)
                {
                    await using var scope = services.CreateAsyncScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IReceiptReconciliationService>();
                    var deleted = await svc.CleanupOrphanedDerivedAssetsAsync(stoppingToken);
                    _lastCleanupUtc = now;
                    logger.LogInformation("Cleanup pass deleted {Count} orphan derived files.", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Maintenance pass failed. Will retry on the next interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        logger.LogInformation("Maintenance scheduler stopped.");
    }
}
