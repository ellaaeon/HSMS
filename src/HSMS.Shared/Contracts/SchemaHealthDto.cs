namespace HSMS.Shared.Contracts;

public sealed class SchemaHealthDto
{
    public bool IsOk { get; set; }
    public List<string> MissingItems { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}
