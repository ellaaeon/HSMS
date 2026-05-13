namespace HSMS.Persistence.Entities;

public sealed class AnalyticsPreset
{
    public int PresetId { get; set; }
    public int AccountId { get; set; }

    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }

    /// <summary>Serialized JSON (filter + breakdowns + chart preferences).</summary>
    public string PresetJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

