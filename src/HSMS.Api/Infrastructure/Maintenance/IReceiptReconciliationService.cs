namespace HSMS.Api.Infrastructure.Maintenance;

public interface IReceiptReconciliationService
{
    /// <summary>
    /// Walks all rows in <c>cycle_receipts</c> + their derived assets, comparing them with files on disk.
    /// Returns counts so the result can be logged to <c>audit_logs</c> for operational visibility.
    /// </summary>
    Task<ReceiptReconciliationResult> ReconcileAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes derived files (preview/thumbnail PNGs) on disk that no longer have a matching row.
    /// Originals are preserved (deletion of originals is an explicit admin operation).
    /// </summary>
    Task<int> CleanupOrphanedDerivedAssetsAsync(CancellationToken cancellationToken);
}

public sealed class ReceiptReconciliationResult
{
    public int TotalReceipts { get; init; }
    public int MissingOriginals { get; init; }
    public int MissingDerivedAssets { get; init; }
    public int OrphanFilesOnDisk { get; init; }
    public DateTime ReconciledAtUtc { get; init; }
    public List<string> Findings { get; init; } = [];
}
