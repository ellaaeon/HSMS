namespace HSMS.Persistence.Entities;

public sealed class DepartmentItem
{
    public int DeptItemId { get; set; }
    public int DepartmentId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int? DefaultPcs { get; set; }
    public int? DefaultQty { get; set; }
    public bool IsActive { get; set; }
    public DateTime? DisabledAt { get; set; }
    public int? DisabledBy { get; set; }
}
