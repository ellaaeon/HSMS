using System.Globalization;
using HSMS.Shared.Contracts;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HSMS.Desktop.Reporting;

public static class SterilizationQaPdfBuilder
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
        string printedBy,
        DateTime printedAtLocal,
        IReadOnlyList<SterilizationQaRecordListItemDto> records,
        Func<long, IReadOnlyList<SterilizationQaTimelineEventDto>> timelineFor,
        Func<long, IReadOnlyList<SterilizationQaAttachmentListItemDto>> evidenceFor)
    {
        EnsureQuestPdf();

        var doc = Document.Create(container =>
        {
            foreach (var r in records)
            {
                container.Page(page =>
                {
                    ApplyA4Page(page);
                    page.Header().Element(c => HospitalHeader(
                        c,
                        "STERILIZATION QA RECORD",
                        $"{r.Category} • Record #{r.RecordId} • {r.Status}"));
                    page.Content().Element(c => RecordBody(c, printedBy, printedAtLocal, r, timelineFor(r.RecordId), evidenceFor(r.RecordId)));
                    page.Footer().Element(c => HospitalFooter(c, $"QA Record #{r.RecordId}"));
                });
            }
        });

        return doc.GeneratePdf();
    }

    private static void ApplyA4Page(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
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
                row.ConstantItem(180).AlignRight().Column(c =>
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

    private static void RecordBody(
        IContainer container,
        string printedBy,
        DateTime printedAtLocal,
        SterilizationQaRecordListItemDto r,
        IReadOnlyList<SterilizationQaTimelineEventDto> timeline,
        IReadOnlyList<SterilizationQaAttachmentListItemDto> evidence)
    {
        container.PaddingVertical(8).Column(col =>
        {
            col.Spacing(8);

            col.Item().Border(1).BorderColor("#CBD5E1").Padding(8).Column(c =>
            {
                c.Item().Row(x =>
                {
                    x.RelativeItem().Component(new KeyValueComponent("Record ID", r.RecordId.ToString(CultureInfo.InvariantCulture)));
                    x.RelativeItem().Component(new KeyValueComponent("Category", r.Category.ToString()));
                    x.RelativeItem().Component(new KeyValueComponent("Status", r.Status.ToString()));
                });
                c.Item().PaddingTop(2).Row(x =>
                {
                    x.RelativeItem().Component(new KeyValueComponent("Cycle no", r.CycleNo ?? "—"));
                    x.RelativeItem().Component(new KeyValueComponent("Sterilizer", r.SterilizerNo ?? "—"));
                    x.RelativeItem().Component(new KeyValueComponent("When (UTC)", r.TestDateTimeUtc.ToString("yyyy-MM-dd HH:mm")));
                });
                c.Item().PaddingTop(2).Row(x =>
                {
                    x.RelativeItem().Component(new KeyValueComponent("Technician", r.Technician ?? "—"));
                    x.RelativeItem().Component(new KeyValueComponent("Department", r.Department ?? "—"));
                    x.RelativeItem().Component(new KeyValueComponent("Result", r.ResultLabel ?? "—"));
                });
                c.Item().PaddingTop(2).Row(x =>
                {
                    x.RelativeItem().Component(new KeyValueComponent("Evidence files", evidence.Count.ToString(CultureInfo.InvariantCulture)));
                    x.RelativeItem().Component(new KeyValueComponent("Legacy", r.IsLegacyQaTest ? "Yes" : "No"));
                    x.RelativeItem().Component(new KeyValueComponent("Printed by", printedBy));
                });
            });

            col.Item().Text("TIMELINE / HISTORY").SemiBold().FontSize(11);
            col.Item().Border(1).BorderColor("#CBD5E1").Padding(8).Column(c =>
            {
                if (timeline.Count == 0)
                {
                    c.Item().Text("—").FontColor("#64748B");
                    return;
                }

                foreach (var e in timeline.Take(16))
                {
                    c.Item().Row(row =>
                    {
                        row.ConstantItem(110).Text(e.EventAtUtc.ToString("yyyy-MM-dd HH:mm")).FontSize(9).FontColor("#475569");
                        row.RelativeItem().Column(cc =>
                        {
                            cc.Item().Text(e.Title).SemiBold();
                            if (!string.IsNullOrWhiteSpace(e.Detail))
                            {
                                cc.Item().Text(e.Detail!).FontSize(9).FontColor("#475569");
                            }
                        });
                    });
                }
            });

            col.Item().Text("EVIDENCE / ATTACHMENTS").SemiBold().FontSize(11);
            col.Item().Border(1).BorderColor("#CBD5E1").Padding(8).Column(c =>
            {
                if (evidence.Count == 0)
                {
                    c.Item().Text("—").FontColor("#64748B");
                    return;
                }

                foreach (var a in evidence.Take(18))
                {
                    c.Item().Row(row =>
                    {
                        row.ConstantItem(110).Text(a.CapturedAtUtc.ToString("yyyy-MM-dd HH:mm")).FontSize(9).FontColor("#475569");
                        row.RelativeItem().Text(a.FileName).FontSize(10);
                        row.ConstantItem(70).AlignRight().Text($"{a.FileSizeBytes / 1024} KB").FontSize(9).FontColor("#475569");
                    });
                }
            });

            col.Item().PaddingTop(18).Row(rw =>
            {
                rw.RelativeItem().Component(new SignatureBoxComponent("Technician"));
                rw.ConstantItem(20);
                rw.RelativeItem().Component(new SignatureBoxComponent("Supervisor / QA"));
            });
        });
    }

    private sealed class KeyValueComponent(string key, string value) : IComponent
    {
        public void Compose(IContainer container)
        {
            container.Column(c =>
            {
                c.Item().Text(key).FontSize(8).FontColor("#64748B");
                c.Item().Text(value).SemiBold().FontSize(10);
            });
        }
    }

    private sealed class SignatureBoxComponent(string label) : IComponent
    {
        public void Compose(IContainer container)
        {
            container.Border(1).BorderColor("#CBD5E1").Padding(8).Height(58).Column(c =>
            {
                c.Item().Text(label).FontSize(9).FontColor("#475569");
                c.Item().AlignBottom().Text(" ").FontSize(10);
            });
        }
    }
}

