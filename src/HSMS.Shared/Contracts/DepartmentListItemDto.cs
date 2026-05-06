namespace HSMS.Shared.Contracts;

public sealed class DepartmentListItemDto
{
    public int DepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
