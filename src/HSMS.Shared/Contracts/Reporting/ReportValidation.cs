namespace HSMS.Shared.Contracts.Reporting;

/// <summary>
/// Outcome of validating a report request before render. Errors block render; warnings surface in the UI.
/// </summary>
public sealed class ReportValidationResult
{
    public List<ReportWarningDto> Errors { get; } = [];
    public List<ReportWarningDto> Warnings { get; } = [];

    public bool HasErrors => Errors.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;

    public ReportValidationResult AddError(string code, string message)
    {
        Errors.Add(new ReportWarningDto { Code = code, Message = message });
        return this;
    }

    public ReportValidationResult AddWarning(string code, string message)
    {
        Warnings.Add(new ReportWarningDto { Code = code, Message = message });
        return this;
    }

    public string FirstErrorMessage() => Errors.Count == 0 ? string.Empty : Errors[0].Message;
}

public static class ReportValidationCodes
{
    public const string CycleNotFound = "CYCLE_NOT_FOUND";
    public const string CycleNoItems = "CYCLE_NO_ITEMS";
    public const string QaTestNotFound = "QA_TEST_NOT_FOUND";
    public const string MissingDateRange = "MISSING_DATE_RANGE";
    public const string MissingReceipt = "MISSING_RECEIPT";
    public const string ReceiptUnsupportedFormat = "RECEIPT_UNSUPPORTED_FORMAT";
    public const string ReportTypeUnknown = "REPORT_TYPE_UNKNOWN";
    public const string CycleNotCompleted = "CYCLE_NOT_COMPLETED";
}
