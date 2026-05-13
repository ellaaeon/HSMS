namespace HSMS.Shared.Contracts;

public sealed class SterilizationQaPresetListItemDto
{
    public int PresetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class SterilizationQaPresetDto
{
    public int PresetId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public SterilizationQaRecordQueryDto Query { get; set; } = new();
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class SterilizationQaPresetUpsertDto
{
    public string Name { get; set; } = string.Empty;
    public SterilizationQaRecordQueryDto Query { get; set; } = new();
    public bool SetAsDefault { get; set; }
}

