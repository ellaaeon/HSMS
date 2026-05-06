namespace HSMS.Persistence.Entities;

public sealed class DoctorRoom
{
    public int DoctorRoomId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string? Room { get; set; }
    public bool IsActive { get; set; }
}
