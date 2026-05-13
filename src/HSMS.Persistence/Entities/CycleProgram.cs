namespace HSMS.Persistence.Entities;

public sealed class CycleProgram
{
    public int CycleProgramId { get; set; }
    public string ProgramCode { get; set; } = string.Empty;
    public string ProgramName { get; set; } = string.Empty;

    /// <summary>One of "High temperature" / "Low temperature" or null if applicable to all sterilizer types.</summary>
    public string? SterilizationType { get; set; }
    public decimal? DefaultTemperatureC { get; set; }
    public decimal? DefaultPressure { get; set; }
    public int? DefaultExposureMinutes { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime? DisabledAt { get; set; }
    public int? DisabledBy { get; set; }
}
