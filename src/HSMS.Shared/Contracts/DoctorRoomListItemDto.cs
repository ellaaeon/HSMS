namespace HSMS.Shared.Contracts;

public sealed class DoctorRoomListItemDto
{
    public int DoctorRoomId { get; set; }
    public string DoctorName { get; set; } = string.Empty;
    public string? Room { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
