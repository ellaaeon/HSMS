namespace HSMS.Shared.Contracts;

public sealed class SterilizationQaAuditEventDto
{
    public string Action { get; set; } = string.Empty;
    public string? Format { get; set; } // CSV/XLSX/PDF/WPF
    public object? Filter { get; set; }
    public int? RecordCount { get; set; }
    public string? Notes { get; set; }
    public string? ClientMachine { get; set; }
}

