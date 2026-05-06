namespace HSMS.Shared.Contracts;

public sealed class SterilizerUpsertDto
{
    public string SterilizerNo { get; set; } = string.Empty;
    public string? Model { get; set; }
    public string? Manufacturer { get; set; }
    public DateTime? PurchaseDate { get; set; }
}

public sealed class DepartmentUpsertDto
{
    public string DepartmentName { get; set; } = string.Empty;
}

public sealed class DoctorRoomUpsertDto
{
    public string DoctorName { get; set; } = string.Empty;
    public string? Room { get; set; }
}

public sealed class DepartmentItemUpsertDto
{
    public int DepartmentId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public int? DefaultPcs { get; set; }
    public int? DefaultQty { get; set; }
}
