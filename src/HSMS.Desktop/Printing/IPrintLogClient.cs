using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Desktop.Printing;

/// <summary>
/// Posts to /api/print-logs. The desktop calls this AFTER the spooler accepts the job;
/// retries are independent of the actual print so a logging failure never reprints.
/// </summary>
public interface IPrintLogClient
{
    Task<long> RecordPrintAsync(PrintLogCreateDto request, CancellationToken cancellationToken);
}
