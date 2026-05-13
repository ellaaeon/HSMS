using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Api.Infrastructure.Files;

/// <summary>
/// Background worker that drains the receipt-derivation queue. Also recovers in-flight jobs after a restart
/// by sweeping any rows whose state is Pending or Running on startup.
/// </summary>
public sealed class ReceiptDerivationHostedService(
    ReceiptDerivationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<ReceiptDerivationHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await SweepPendingOnStartupAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Receipt derivation startup sweep failed - continuing with live queue only.");
        }

        await foreach (var receiptId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessOneAsync(receiptId, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to derive receipt assets for receipt {ReceiptId}", receiptId);
            }
        }
    }

    private async Task SweepPendingOnStartupAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HsmsDbContext>();

        var pending = await db.CycleReceiptDerivationStates.AsNoTracking()
            .Where(x => x.State == CycleReceiptDerivationStates.Pending
                     || x.State == CycleReceiptDerivationStates.Running)
            .Select(x => x.ReceiptId)
            .ToListAsync(cancellationToken);

        foreach (var id in pending)
        {
            await queue.EnqueueAsync(id, cancellationToken);
        }
    }

    private async Task ProcessOneAsync(int receiptId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var derivation = scope.ServiceProvider.GetRequiredService<IReceiptDerivationService>();
        var result = await derivation.DeriveAsync(receiptId, cancellationToken);

        if (!result.Success && !result.NotApplicable)
        {
            logger.LogWarning("Receipt derivation failed for {ReceiptId}: {Error}", receiptId, result.Error);
        }
    }
}
