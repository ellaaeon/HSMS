using ClosedXML.Excel;

namespace HSMS.Application.Exports;

public interface IExcelWorkbookExportService
{
    void WriteWorkbook(Action<IXLWorkbook> buildWorkbook, Stream output);
}

