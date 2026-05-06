namespace HSMS.Api.Infrastructure.Files;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";
    public string ReceiptsRootPath { get; set; } = @"D:\HSMS\Receipts";
    public int MaxUploadBytes { get; set; } = 50 * 1024 * 1024;

    /// <summary>
    /// Same role as legacy <c>appSettings/DownloadFolderPath</c> in hsms.exe.config (exports, downloads, user file pickers).
    /// </summary>
    public string DownloadFolderPath { get; set; } = string.Empty;
}
