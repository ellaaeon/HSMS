using System.Globalization;
using HSMS.Application.Reporting.Datasets;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HSMS.Application.Reporting;

/// <summary>
/// Default render engine. Produces hospital-style A4 PDFs that mirror the legacy RDLC layouts.
/// Replaceable: register a different <see cref="IReportRenderEngine"/> in DI to switch to a true RDLC engine later.
/// </summary>
public sealed class QuestPdfReportRenderEngine : IReportRenderEngine
{
    private static readonly object SettingsLock = new();
    private static bool _settingsApplied;

    public QuestPdfReportRenderEngine()
    {
        lock (SettingsLock)
        {
            if (!_settingsApplied)
            {
                QuestPDF.Settings.License = LicenseType.Community;
                _settingsApplied = true;
            }
        }
    }

    public Task<RenderedReport> RenderLoadRecordAsync(LoadRecordReportData data, CancellationToken cancellationToken)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                ApplyA4Page(page);
                page.Header().Element(c => HospitalHeader(c, "STERILIZATION LOAD RECORD", $"Cycle #{data.Header.CycleNo}"));
                page.Content().Element(c => LoadRecordBody(c, data));
                page.Footer().Element(c => HospitalFooter(c, $"Cycle #{data.Header.CycleNo}"));
            });

            if (data.ReceiptImages.Count > 0)
            {
                container.Page(page =>
                {
                    ApplyA4Page(page);
                    page.Header().Element(c => HospitalHeader(c, "RECEIPT IMAGES", $"Cycle #{data.Header.CycleNo}"));
                    page.Content().Element(c => ReceiptImagesGrid(c, data.ReceiptImages));
                    page.Footer().Element(c => HospitalFooter(c, $"Cycle #{data.Header.CycleNo}"));
                });
            }
        });

        var bytes = doc.GeneratePdf();
        var pageCount = 1 + (data.ReceiptImages.Count > 0 ? 1 : 0);
        return Task.FromResult(new RenderedReport { PdfBytes = bytes, PageCount = pageCount });
    }

    public Task<RenderedReport> RenderBiLogSheetAsync(BiLogSheetReportData data, CancellationToken cancellationToken)
    {
        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                ApplyA4Page(page, landscape: true);
                page.Header().Element(c => HospitalHeader(c,
                    "BI LOG SHEET",
                    $"From {data.FromUtc:yyyy-MM-dd} to {data.ToUtc:yyyy-MM-dd}" +
                    (string.IsNullOrWhiteSpace(data.SterilizationTypeFilter) ? "" : $"  ·  {data.SterilizationTypeFilter}")));
                page.Content().Element(c => BiLogSheetTable(c, data));
                page.Footer().Element(c => HospitalFooter(c, $"BI Log Sheet · {data.Rows.Count} rows"));
            });
        });

        var bytes = doc.GeneratePdf();
        return Task.FromResult(new RenderedReport { PdfBytes = bytes, PageCount = 1 });
    }

    public Task<RenderedReport> RenderQaTestAsync(QaTestReportData data, CancellationToken cancellationToken)
    {
        var title = data.TestType.Equals("Leak", StringComparison.OrdinalIgnoreCase)
            ? "LEAK TEST REPORT"
            : "BOWIE-DICK TEST REPORT";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                ApplyA4Page(page);
                page.Header().Element(c => HospitalHeader(c, title, $"Cycle #{data.CycleNo}"));
                page.Content().Element(c => QaTestBody(c, data));
                page.Footer().Element(c => HospitalFooter(c, $"QA Test #{data.QaTestId}"));
            });
        });

        var bytes = doc.GeneratePdf();
        return Task.FromResult(new RenderedReport { PdfBytes = bytes, PageCount = 1 });
    }

    private static void ApplyA4Page(PageDescriptor page, bool landscape = false)
    {
        page.Size(landscape ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(28);
        page.PageColor(Colors.White);
        page.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(10).FontColor("#0F172A"));
    }

    private static void HospitalHeader(IContainer container, string title, string subtitle)
    {
        container.PaddingBottom(8).BorderBottom(1).BorderColor("#0F172A").Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(c =>
                {
                    c.Item().Text("HOSPITAL STERILIZATION MANAGEMENT SYSTEM").FontSize(8).FontColor("#475569");
                    c.Item().Text(title).FontSize(16).SemiBold();
                    c.Item().Text(subtitle).FontSize(9).FontColor("#475569");
                });
                row.ConstantItem(160).AlignRight().Column(c =>
                {
                    c.Item().Text($"Printed: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8);
                });
            });
        });
    }

    private static void HospitalFooter(IContainer container, string label)
    {
        container.PaddingTop(8).BorderTop(0.5f).BorderColor("#94A3B8").Row(row =>
        {
            row.RelativeItem().Text(label).FontSize(8).FontColor("#475569");
            row.ConstantItem(80).AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(8).FontColor("#475569");
                text.CurrentPageNumber().FontSize(8).FontColor("#475569");
                text.Span(" / ").FontSize(8).FontColor("#475569");
                text.TotalPages().FontSize(8).FontColor("#475569");
            });
        });
    }

    private static void LoadRecordBody(IContainer container, LoadRecordReportData data)
    {
        container.PaddingVertical(8).Column(col =>
        {
            col.Spacing(8);

            col.Item().Border(1).BorderColor("#CBD5E1").Padding(8).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem().Component(new KeyValueComponent("Cycle no", data.Header.CycleNo));
                    r.RelativeItem().Component(new KeyValueComponent("Sterilizer", $"{data.Header.SterilizerNo}  {data.Header.SterilizerModel}"));
                    r.RelativeItem().Component(new KeyValueComponent("Type", data.Header.SterilizationType));
                    r.RelativeItem().Component(new KeyValueComponent("Program", data.Header.CycleProgram));
                });
                c.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Component(new KeyValueComponent("Cycle date", data.Header.CycleDateTimeUtc.ToString("yyyy-MM-dd HH:mm")));
                    r.RelativeItem().Component(new KeyValueComponent("Time in", data.Header.CycleTimeInUtc?.ToString("HH:mm") ?? "—"));
                    r.RelativeItem().Component(new KeyValueComponent("Time out", data.Header.CycleTimeOutUtc?.ToString("HH:mm") ?? "—"));
                    r.RelativeItem().Component(new KeyValueComponent("Status", data.Header.CycleStatus));
                });
                c.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Component(new KeyValueComponent("Operator", data.Header.OperatorName));
                    r.RelativeItem().Component(new KeyValueComponent("Doctor / room", data.Header.DoctorOrRoom ?? "—"));
                    r.RelativeItem().Component(new KeyValueComponent("Implants", data.Header.Implants ? "Yes" : "No"));
                    r.RelativeItem().Component(new KeyValueComponent("BI lot", data.Header.BiLotNo ?? "—"));
                });
                c.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Component(new KeyValueComponent("Temperature", data.Header.TemperatureC?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—"));
                    r.RelativeItem().Component(new KeyValueComponent("Pressure", data.Header.Pressure?.ToString("0.000", CultureInfo.InvariantCulture) ?? "—"));
                    r.RelativeItem().Component(new KeyValueComponent("Exposure (min)", data.Header.ExposureTimeMinutes?.ToString(CultureInfo.InvariantCulture) ?? "—"));
                    r.RelativeItem().Component(new KeyValueComponent("BI result", data.Header.BiResult ?? "—"));
                });
            });

            col.Item().Text("LOAD ITEMS").SemiBold().FontSize(11);
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(2);
                    c.RelativeColumn(2);
                    c.RelativeColumn(3);
                    c.ConstantColumn(40);
                    c.ConstantColumn(40);
                });
                t.Header(h =>
                {
                    h.Cell().Element(HeaderCell).AlignCenter().Text("#");
                    h.Cell().Element(HeaderCell).Text("Department");
                    h.Cell().Element(HeaderCell).Text("Doctor / Room");
                    h.Cell().Element(HeaderCell).Text("Item name");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Pcs");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                });
                foreach (var item in data.Items)
                {
                    t.Cell().Element(BodyCell).AlignCenter().Text(item.LineNo.ToString(CultureInfo.InvariantCulture));
                    t.Cell().Element(BodyCell).Text(item.DepartmentName);
                    t.Cell().Element(BodyCell).Text(item.DoctorOrRoom);
                    t.Cell().Element(BodyCell).Text(item.ItemName);
                    t.Cell().Element(BodyCell).AlignRight().Text(item.Pcs.ToString(CultureInfo.InvariantCulture));
                    t.Cell().Element(BodyCell).AlignRight().Text(item.Qty.ToString(CultureInfo.InvariantCulture));
                }
                t.Cell().ColumnSpan(4).Element(BodyCell).AlignRight().Text("Totals").SemiBold();
                t.Cell().Element(BodyCell).AlignRight().Text(data.Header.TotalPcs.ToString(CultureInfo.InvariantCulture)).SemiBold();
                t.Cell().Element(BodyCell).AlignRight().Text(data.Header.TotalQty.ToString(CultureInfo.InvariantCulture)).SemiBold();
            });

            if (!string.IsNullOrWhiteSpace(data.Header.Notes))
            {
                col.Item().PaddingTop(6).Border(1).BorderColor("#CBD5E1").Padding(8).Column(c =>
                {
                    c.Item().Text("Notes").SemiBold();
                    c.Item().Text(data.Header.Notes);
                });
            }

            col.Item().PaddingTop(20).Row(r =>
            {
                r.RelativeItem().Component(new SignatureBoxComponent("Operator"));
                r.ConstantItem(20);
                r.RelativeItem().Component(new SignatureBoxComponent("Supervisor"));
            });
        });
    }

    private static void BiLogSheetTable(IContainer container, BiLogSheetReportData data)
    {
        container.PaddingVertical(6).Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(2);
                c.RelativeColumn(3);
            });

            t.Header(h =>
            {
                h.Cell().Element(HeaderCell).Text("Date / Time");
                h.Cell().Element(HeaderCell).Text("Cycle");
                h.Cell().Element(HeaderCell).Text("Sterilizer");
                h.Cell().Element(HeaderCell).Text("Type");
                h.Cell().Element(HeaderCell).Text("BI lot");
                h.Cell().Element(HeaderCell).Text("BI in");
                h.Cell().Element(HeaderCell).Text("BI out");
                h.Cell().Element(HeaderCell).Text("Result");
                h.Cell().Element(HeaderCell).Text("Operator");
            });

            foreach (var row in data.Rows)
            {
                t.Cell().Element(BodyCell).Text(row.CycleDateTimeUtc.ToString("yyyy-MM-dd HH:mm"));
                t.Cell().Element(BodyCell).Text(row.CycleNo);
                t.Cell().Element(BodyCell).Text(row.SterilizerNo);
                t.Cell().Element(BodyCell).Text(row.SterilizationType);
                t.Cell().Element(BodyCell).Text(row.BiLotNo ?? "—");
                t.Cell().Element(BodyCell).Text(FormatTimeAndInitials(row.BiTimeInUtc, row.BiTimeInInitials));
                t.Cell().Element(BodyCell).Text(FormatTimeAndInitials(row.BiTimeOutUtc, row.BiTimeOutInitials));
                t.Cell().Element(BodyCell).Text(row.BiResult ?? "—");
                t.Cell().Element(BodyCell).Text(row.OperatorName ?? "—");
            }
        });
    }

    private static void QaTestBody(IContainer container, QaTestReportData data)
    {
        container.PaddingVertical(8).Column(col =>
        {
            col.Spacing(8);
            col.Item().Border(1).BorderColor("#CBD5E1").Padding(8).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem().Component(new KeyValueComponent("Test type", data.TestType));
                    r.RelativeItem().Component(new KeyValueComponent("Cycle", data.CycleNo));
                    r.RelativeItem().Component(new KeyValueComponent("Sterilizer", data.SterilizerNo));
                    r.RelativeItem().Component(new KeyValueComponent("Date", data.TestDateTimeUtc.ToString("yyyy-MM-dd HH:mm")));
                });
                c.Item().PaddingTop(2).Row(r =>
                {
                    r.RelativeItem().Component(new KeyValueComponent("Result", data.Result));
                    r.RelativeItem().Component(new KeyValueComponent("Measured value",
                        data.MeasuredValue is null ? "—" : $"{data.MeasuredValue:0.###}{(string.IsNullOrWhiteSpace(data.Unit) ? "" : " " + data.Unit)}"));
                    r.RelativeItem().Component(new KeyValueComponent("Performed by", data.PerformedBy ?? "—"));
                    r.RelativeItem().Component(new KeyValueComponent("Approval", data.ApprovalStatus));
                });
            });

            col.Item().Border(1).BorderColor("#CBD5E1").Padding(8).Column(c =>
            {
                c.Item().Text("Operator remarks").SemiBold();
                c.Item().PaddingTop(4).MinHeight(40).Text(string.IsNullOrWhiteSpace(data.Notes) ? " " : data.Notes);
            });

            col.Item().Border(1).BorderColor("#CBD5E1").Padding(8).Column(c =>
            {
                c.Item().Text("Supervisor remarks").SemiBold();
                c.Item().PaddingTop(4).MinHeight(40).Text(string.IsNullOrWhiteSpace(data.SupervisorRemarks) ? " " : data.SupervisorRemarks);
            });

            col.Item().PaddingTop(20).Row(r =>
            {
                r.RelativeItem().Component(new SignatureBoxComponent("Operator"));
                r.ConstantItem(20);
                r.RelativeItem().Component(new SignatureBoxComponent("Supervisor"));
            });
        });
    }

    private static void ReceiptImagesGrid(IContainer container, IReadOnlyList<LoadRecordReceiptImage> images)
    {
        container.PaddingVertical(6).Column(col =>
        {
            col.Spacing(8);

            switch (images.Count)
            {
                case 1:
                    col.Item().Element(c => DrawReceipt(c, images[0]));
                    break;
                case 2:
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Element(c => DrawReceipt(c, images[0]));
                        r.ConstantItem(8);
                        r.RelativeItem().Element(c => DrawReceipt(c, images[1]));
                    });
                    break;
                default:
                {
                    var rows = images.Chunk(2).ToList();
                    foreach (var pair in rows)
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Element(c => DrawReceipt(c, pair[0]));
                            r.ConstantItem(8);
                            if (pair.Length > 1)
                            {
                                r.RelativeItem().Element(c => DrawReceipt(c, pair[1]));
                            }
                            else
                            {
                                r.RelativeItem();
                            }
                        });
                    }
                    break;
                }
            }
        });
    }

    private static void DrawReceipt(IContainer container, LoadRecordReceiptImage image)
    {
        container.Border(1).BorderColor("#CBD5E1").Padding(6).Column(c =>
        {
            c.Item().Text(image.FileName).FontSize(9).SemiBold();
            c.Item().Text($"Captured {image.CapturedAtUtc:yyyy-MM-dd HH:mm}").FontSize(8).FontColor("#475569");
            c.Item().PaddingTop(4).MaxHeight(280).Image(image.ImageBytes).FitArea();
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.Background("#1E293B").Padding(4).DefaultTextStyle(t => t.FontColor("#F8FAFC").SemiBold().FontSize(9));

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor("#CBD5E1").Padding(4).DefaultTextStyle(t => t.FontSize(9));

    private static string FormatTimeAndInitials(DateTime? time, string? initials)
    {
        if (time is null) return "—";
        return string.IsNullOrWhiteSpace(initials)
            ? time.Value.ToString("HH:mm")
            : $"{time:HH:mm} ({initials})";
    }
}

internal sealed class KeyValueComponent(string key, string value) : IComponent
{
    public void Compose(IContainer container)
    {
        container.Column(c =>
        {
            c.Item().Text(key).FontSize(8).FontColor("#64748B");
            c.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value).FontSize(10).SemiBold();
        });
    }
}

internal sealed class SignatureBoxComponent(string label) : IComponent
{
    public void Compose(IContainer container)
    {
        container.Column(c =>
        {
            c.Item().PaddingBottom(20).BorderBottom(0.75f).BorderColor("#0F172A");
            c.Item().PaddingTop(2).Text(label).FontSize(9);
        });
    }
}
