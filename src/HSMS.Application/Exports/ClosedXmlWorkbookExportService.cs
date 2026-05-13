using ClosedXML.Excel;

namespace HSMS.Application.Exports;

public sealed class ClosedXmlWorkbookExportService : IExcelWorkbookExportService
{
    public void WriteWorkbook(Action<IXLWorkbook> buildWorkbook, Stream output)
    {
        using var wb = new XLWorkbook();
        buildWorkbook(wb);
        wb.SaveAs(output);
    }
}

