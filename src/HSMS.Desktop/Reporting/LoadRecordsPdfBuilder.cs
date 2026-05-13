using System.Globalization;
using System.IO;
using System.Windows;
using HSMS.Shared.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HSMS.Desktop.Reporting;

public static class LoadRecordsPdfBuilder
{
    private static readonly object SettingsLock = new();
    private static bool _settingsApplied;

    private static void EnsureQuestPdf()
    {
        lock (SettingsLock)
        {
            if (_settingsApplied) return;
            QuestPDF.Settings.License = LicenseType.Community;
            _settingsApplied = true;
        }
    }

    public static byte[] BuildPdfBytes(
        string title,
        string subtitle,
        IReadOnlyList<SterilizationSearchItemDto> rows)
    {
        EnsureQuestPdf();

        var logoBytes = TryLoadLogoBytes();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(24);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontFamily("Segoe UI").FontSize(10).FontColor("#0F172A"));

                page.Header().Element(c => Header(c, logoBytes, title, subtitle));
                page.Content().Element(c => Body(c, rows));
                page.Footer().Element(Footer);
            });
        });

        return doc.GeneratePdf();
    }

    private static void Header(IContainer container, byte[]? logoBytes, string title, string subtitle)
    {
        container.PaddingBottom(10).BorderBottom(1).BorderColor("#0F172A").Row(row =>
        {
            if (logoBytes is not null)
            {
                row.ConstantItem(70).Height(44).AlignMiddle().AlignLeft().Image(logoBytes);
                row.ConstantItem(12);
            }

            row.RelativeItem().Column(col =>
            {
                col.Item().Text("HOSPITAL STERILIZATION MANAGEMENT SYSTEM").FontSize(8).FontColor("#475569");
                col.Item().Text(title).FontSize(16).SemiBold();
                col.Item().Text(subtitle).FontSize(9).FontColor("#475569");
            });

            row.ConstantItem(160).AlignRight().AlignMiddle().Text($"Exported: {DateTime.Now:yyyy-MM-dd HH:mm}").FontSize(8).FontColor("#475569");
        });
    }

    private static void Footer(IContainer container)
    {
        container.PaddingTop(8).BorderTop(0.5f).BorderColor("#94A3B8").Row(row =>
        {
            row.RelativeItem().Text("HSMS").FontSize(8).FontColor("#475569");
            row.ConstantItem(80).AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(8).FontColor("#475569");
                text.CurrentPageNumber().FontSize(8).FontColor("#475569");
                text.Span(" / ").FontSize(8).FontColor("#475569");
                text.TotalPages().FontSize(8).FontColor("#475569");
            });
        });
    }

    private static void Body(IContainer container, IReadOnlyList<SterilizationSearchItemDto> rows)
    {
        container.PaddingVertical(10).Column(col =>
        {
            col.Item().Text($"Rows: {rows.Count.ToString(CultureInfo.InvariantCulture)}").FontSize(9).FontColor("#475569");
            col.Item().PaddingTop(8).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(40);   // ID
                    c.ConstantColumn(70);   // Cycle no
                    c.RelativeColumn(2);    // Program
                    c.ConstantColumn(70);   // Sterilizer
                    c.RelativeColumn(2);    // Operator
                    c.ConstantColumn(70);   // Status
                    c.ConstantColumn(105);  // Registered
                    c.ConstantColumn(40);   // Pcs
                    c.ConstantColumn(40);   // Qty
                });

                t.Header(h =>
                {
                    h.Cell().Element(HeaderCell).AlignCenter().Text("ID");
                    h.Cell().Element(HeaderCell).Text("Cycle");
                    h.Cell().Element(HeaderCell).Text("Program");
                    h.Cell().Element(HeaderCell).Text("Sterilizer");
                    h.Cell().Element(HeaderCell).Text("Operator");
                    h.Cell().Element(HeaderCell).Text("Status");
                    h.Cell().Element(HeaderCell).Text("Registered (UTC)");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Pcs");
                    h.Cell().Element(HeaderCell).AlignRight().Text("Qty");
                });

                foreach (var r in rows)
                {
                    t.Cell().Element(BodyCell).AlignCenter().Text(r.SterilizationId.ToString(CultureInfo.InvariantCulture));
                    t.Cell().Element(BodyCell).Text(r.CycleNo ?? "");
                    t.Cell().Element(BodyCell).Text(r.CycleProgram ?? "");
                    t.Cell().Element(BodyCell).Text(r.SterilizerNo ?? "");
                    t.Cell().Element(BodyCell).Text(r.OperatorName ?? "");
                    t.Cell().Element(BodyCell).Text(r.CycleStatus ?? "");
                    t.Cell().Element(BodyCell).Text(r.RegisteredAtUtc.ToString("yyyy-MM-dd HH:mm"));
                    t.Cell().Element(BodyCell).AlignRight().Text(r.TotalPcs.ToString(CultureInfo.InvariantCulture));
                    t.Cell().Element(BodyCell).AlignRight().Text(r.TotalQty.ToString(CultureInfo.InvariantCulture));
                }
            });
        });
    }

    private static IContainer HeaderCell(IContainer c) =>
        c.BorderBottom(1).BorderColor("#CBD5E1").Background("#F1F5F9").PaddingVertical(6).PaddingHorizontal(6)
            .DefaultTextStyle(t => t.SemiBold().FontSize(9).FontColor("#0F172A"));

    private static IContainer BodyCell(IContainer c) =>
        c.BorderBottom(0.5f).BorderColor("#E2E8F0").PaddingVertical(5).PaddingHorizontal(6)
            .DefaultTextStyle(t => t.FontSize(9).FontColor("#0F172A"));

    private static byte[]? TryLoadLogoBytes()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/logo.png", UriKind.Absolute);
            var info = System.Windows.Application.GetResourceStream(uri);
            if (info?.Stream is null) return null;
            using var ms = new MemoryStream();
            info.Stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }
}

