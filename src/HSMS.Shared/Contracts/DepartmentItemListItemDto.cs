namespace HSMS.Shared.Contracts;

public sealed class DepartmentItemListItemDto
{
    public int DeptItemId { get; set; }
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int? DefaultPcs { get; set; }
    public int? DefaultQty { get; set; }
}
