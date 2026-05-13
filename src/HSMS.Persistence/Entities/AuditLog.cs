namespace HSMS.Persistence.Entities;

public sealed class AuditLog
{
    public long AuditId { get; set; }
    public DateTime EventAt { get; set; }
    public int? ActorAccountId { get; set; }
    public string Module { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
    public string? ClientMachine { get; set; }
    public Guid CorrelationId { get; set; }
}

public sealed class PrintLog
{
    public long PrintLogId { get; set; }
    public DateTime PrintedAt { get; set; }
    public int? PrintedBy { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public int? SterilizationId { get; set; }
    public int? QaTestId { get; set; }
    public string? PrinterName { get; set; }
    public int Copies { get; set; } = 1;
    public string? ParametersJson { get; set; }
    public Guid CorrelationId { get; set; }

    /// <summary>Engine version (e.g. <c>questpdf-1</c>, <c>rdlc-1</c>). Set by the renderer at log time.</summary>
    public string? ReportVersion { get; set; }

    /// <summary>Logical workstation identifier (defaults to client machine name).</summary>
    public string? StationId { get; set; }
}
