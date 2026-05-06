namespace HSMS.Persistence.Entities;

/// <summary>
/// Row in tbl_sterilizer_no (master list of physical sterilizers).
/// </summary>
public sealed class SterilizerUnit
{
    public int SterilizerId { get; set; }
    public string SterilizerNumber { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Manufacturer { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public bool IsActive { get; set; } = true;
}
