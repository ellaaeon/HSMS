namespace HSMS.Shared.Contracts;

public sealed class SterilizerListItemDto
{
    public int SterilizerId { get; set; }
    public string SterilizerNo { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Manufacturer { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public bool IsActive { get; set; }
}
