namespace HSMS.Shared.Contracts;

public sealed class SterilizationBiResultPatchDto
{
    public string RowVersion { get; set; } = string.Empty;
    public string? BiResult { get; set; }
    public string? ClientMachine { get; set; }
}
