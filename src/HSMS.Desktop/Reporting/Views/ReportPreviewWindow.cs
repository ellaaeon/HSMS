using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using HSMS.Desktop.Printing;

namespace HSMS.Desktop.Reporting.Views;

/// <summary>
/// Code-only preview window: shows report metadata, lets the user pick printer + copies, and exposes
/// Print / Export PDF / Open in default viewer. Embedding a PDF render component (PDFium / WebView2)
/// is intentionally out of scope here so the desktop project doesn't take on extra native dependencies.
/// </summary>
public sealed class ReportPreviewWindow : Window
{
    private readonly PrintJob _job;
    private readonly IPrintQueueService _queue;
    private readonly IPrinterService _printerService;

    public ReportPreviewWindow(PrintJob job, IPrintQueueService queue, IPrinterService printerService)
    {
        _job = job;
        _queue = queue;
        _printerService = printerService;

        Title = $"Print preview — {job.Request.ReportType}";
        Width = 580;
        Height = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var grid = new Grid { Margin = new Thickness(20) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = $"Report ready: {_job.Request.ReportType}",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold
        });
        header.Children.Add(new TextBlock
        {
            Text = $"Pages: {_job.PageCount?.ToString() ?? "—"}     Correlation: {_job.CorrelationId:N}",
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 11,
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // Form with printer + copies fields.
        var form = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        form.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRowLabel(form, 0, "Printer");
        var printerCombo = new ComboBox { Margin = new Thickness(0, 4, 0, 8), Padding = new Thickness(8, 4, 8, 4) };
        foreach (var name in _printerService.EnumeratePrinters()) printerCombo.Items.Add(name);
        printerCombo.Text = _job.Request.PrinterName ?? _printerService.GetDefaultPrinterName();
        printerCombo.IsEditable = true;
        Grid.SetColumn(printerCombo, 1); Grid.SetRow(printerCombo, 0);
        form.Children.Add(printerCombo);

        AddRowLabel(form, 1, "Copies");
        var copiesBox = new TextBox
        {
            Text = Math.Max(1, _job.Request.Copies).ToString(),
            Margin = new Thickness(0, 4, 0, 8),
            Padding = new Thickness(8, 4, 8, 4),
            Width = 80,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        Grid.SetColumn(copiesBox, 1); Grid.SetRow(copiesBox, 1);
        form.Children.Add(copiesBox);

        AddRowLabel(form, 2, "Receipts");
        var includeReceipts = new CheckBox
        {
            Content = "Include receipt images on page 2",
            IsChecked = _job.Request.IncludeReceiptImages,
            IsEnabled = false,
            Margin = new Thickness(0, 8, 0, 8)
        };
        Grid.SetColumn(includeReceipts, 1); Grid.SetRow(includeReceipts, 2);
        form.Children.Add(includeReceipts);

        AddRowLabel(form, 3, "Warnings");
        var warningsList = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = System.Windows.Media.Brushes.DarkOrange,
            Margin = new Thickness(0, 8, 0, 0),
            FontSize = 12
        };
        warningsList.Text = "(none)";
        Grid.SetColumn(warningsList, 1); Grid.SetRow(warningsList, 3);
        form.Children.Add(warningsList);

        Grid.SetRow(form, 1);
        grid.Children.Add(form);

        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var openBtn = new Button { Content = "Open in default viewer", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var exportBtn = new Button { Content = "Export PDF…", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0) };
        var printBtn = new Button { Content = "Print", Padding = new Thickness(16, 6, 16, 6), IsDefault = true };

        openBtn.Click += async (_, _) => await OpenInDefaultViewerAsync();
        exportBtn.Click += async (_, _) => await ExportAsync();
        printBtn.Click += async (_, _) =>
        {
            if (!int.TryParse(copiesBox.Text, out var copies) || copies < 1 || copies > 20)
            {
                MessageBox.Show(this, "Copies must be between 1 and 20.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            _job.Request.PrinterName = string.IsNullOrWhiteSpace(printerCombo.Text) ? null : printerCombo.Text;
            _job.Request.Copies = copies;
            DialogResult = true;
            Close();
            await _queue.PrintAsync(_job, CancellationToken.None);
        };

        buttonRow.Children.Add(openBtn);
        buttonRow.Children.Add(exportBtn);
        buttonRow.Children.Add(printBtn);
        Grid.SetRow(buttonRow, 2);
        grid.Children.Add(buttonRow);

        return grid;
    }

    private async Task ExportAsync()
    {
        if (_job.PdfBytes is null) return;
        var dlg = new SaveFileDialog
        {
            Filter = "PDF document (*.pdf)|*.pdf",
            FileName = $"{_job.Request.ReportType}_{_job.CorrelationId:N}.pdf"
        };
        if (dlg.ShowDialog(this) != true) return;
        await File.WriteAllBytesAsync(dlg.FileName, _job.PdfBytes);
        try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { /* best-effort */ }
    }

    private async Task OpenInDefaultViewerAsync()
    {
        if (_job.PdfBytes is null) return;
        var temp = Path.Combine(Path.GetTempPath(), $"hsms_preview_{_job.CorrelationId:N}.pdf");
        await File.WriteAllBytesAsync(temp, _job.PdfBytes);
        try { Process.Start(new ProcessStartInfo(temp) { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open the default PDF viewer:\n\n{ex.Message}", "HSMS",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private static void AddRowLabel(Grid g, int row, string text)
    {
        var lbl = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 6)
        };
        Grid.SetRow(lbl, row);
        Grid.SetColumn(lbl, 0);
        g.Children.Add(lbl);
    }
}
