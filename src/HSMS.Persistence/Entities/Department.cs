namespace HSMS.Persistence.Entities;

public sealed class Department
{
    public int DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime? DisabledAt { get; set; }
    public int? DisabledBy { get; set; }
}
