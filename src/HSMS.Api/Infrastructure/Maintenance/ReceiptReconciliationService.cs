using HSMS.Api.Infrastructure.Files;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HSMS.Api.Infrastructure.Maintenance;

public sealed class ReceiptReconciliationService(
    IDbContextFactory<HsmsDbContext> dbFactory,
    IOptions<StorageOptions> storageOptions,
    ILogger<ReceiptReconciliationService> logger) : IReceiptReconciliationService
{
    public async Task<ReceiptReconciliationResult> ReconcileAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var receipts = await db.CycleReceipts.AsNoTracking()
            .Select(r => new { r.ReceiptId, r.FilePath })
            .ToListAsync(cancellationToken);

        var derived = await db.Set<CycleReceiptAsset>().AsNoTracking()
            .Select(d => new { d.AssetId, d.FilePath })
            .ToListAsync(cancellationToken);

        var missingOriginals = 0;
        var missingDerived = 0;
        var findings = new List<string>();

        foreach (var r in receipts)
        {
            if (!File.Exists(r.FilePath))
            {
                missingOriginals++;
                findings.Add($"Missing original: receipt {r.ReceiptId} -> {r.FilePath}");
            }
        }

        foreach (var d in derived)
        {
            if (!File.Exists(d.FilePath))
            {
                missingDerived++;
                findings.Add($"Missing derived asset: id {d.AssetId} -> {d.FilePath}");
            }
        }

        var orphanCount = await CountOrphansAsync(db, storageOptions.Value.ReceiptsRootPath, cancellationToken);

        var result = new ReceiptReconciliationResult
        {
            TotalReceipts = receipts.Count,
            MissingOriginals = missingOriginals,
            MissingDerivedAssets = missingDerived,
            OrphanFilesOnDisk = orphanCount,
            ReconciledAtUtc = DateTime.UtcNow,
            Findings = findings
        };

        logger.LogInformation(
            "Receipt reconciliation complete. total={Total} missingOriginals={MissingOriginals} missingDerived={MissingDerived} orphans={Orphans}",
            result.TotalReceipts, result.MissingOriginals, result.MissingDerivedAssets, result.OrphanFilesOnDisk);

        return result;
    }

    public async Task<int> CleanupOrphanedDerivedAssetsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var derivedPathsInDb = (await db.Set<CycleReceiptAsset>().AsNoTracking()
                .Select(x => x.FilePath)
                .ToListAsync(cancellationToken))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rootPath = storageOptions.Value.ReceiptsRootPath;
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return 0;
        }

        var deleted = 0;
        foreach (var dir in Directory.EnumerateDirectories(rootPath, "_derived", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (!derivedPathsInDb.Contains(Path.GetFullPath(file)))
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to delete orphan derived asset {Path}", file);
                    }
                }
            }
        }

        logger.LogInformation("Removed {Deleted} orphan derived files from {Root}", deleted, rootPath);
        return deleted;
    }

    private static async Task<int> CountOrphansAsync(HsmsDbContext db, string rootPath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return 0;
        }

        var dbReceipts = await db.CycleReceipts.AsNoTracking().Select(x => x.FilePath).ToListAsync(cancellationToken);
        var dbDerived = await db.Set<CycleReceiptAsset>().AsNoTracking().Select(x => x.FilePath).ToListAsync(cancellationToken);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in dbReceipts) if (!string.IsNullOrWhiteSpace(p)) known.Add(Path.GetFullPath(p));
        foreach (var p in dbDerived) if (!string.IsNullOrWhiteSpace(p)) known.Add(Path.GetFullPath(p));

        var orphanCount = 0;
        foreach (var file in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Skip non-content directories such as _tmp.
            if (file.Contains(Path.DirectorySeparatorChar + "_tmp" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!known.Contains(Path.GetFullPath(file)))
            {
                orphanCount++;
            }
        }

        return orphanCount;
    }
}
