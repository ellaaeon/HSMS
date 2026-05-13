using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HSMS.Application.Services;
using HSMS.Desktop.Reporting;
using HSMS.Shared.Contracts;
using HSMS.Shared.Contracts.Reporting;

namespace HSMS.Desktop;

public partial class QaTestsWindow : Window
{
    private readonly IHsmsDataService _data;
    private readonly PrintReportCoordinator _printCoordinator;
    private readonly ObservableCollection<QaTestListItemDto> _rows = [];

    public QaTestsWindow(IHsmsDataService data, PrintReportCoordinator printCoordinator)
    {
        _data = data;
        _printCoordinator = printCoordinator;
        InitializeComponent();
        QaGrid.ItemsSource = _rows;
        FromDate.SelectedDate = DateTime.Today.AddDays(-30);
        ToDate.SelectedDate = DateTime.Today;
        EntryTestTypeCombo.SelectedIndex = 0;
        EntryResultCombo.SelectedIndex = 0;
        PreviewKeyDown += OnPreviewKeyDown;
        Loaded += async (_, _) => await LoadAsync();
    }

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F9)
        {
            await PrintSelectedAsync(quick: true);
            e.Handled = true;
        }
    }

    private async System.Threading.Tasks.Task LoadAsync()
    {
        try
        {
            var query = new QaTestQueryDto
            {
                FromUtc = FromDate.SelectedDate?.ToUniversalTime(),
                ToUtc = ToDate.SelectedDate?.AddDays(1).ToUniversalTime(),
                TestType = ((FilterTypeCombo.SelectedItem as ComboBoxItem)?.Content as string) is { } selected && selected != "(all)" ? selected : null,
                PendingApprovalOnly = PendingOnlyCheck.IsChecked == true,
                Take = 300
            };
            var (items, err) = await _data.ListQaTestsAsync(query);
            if (err is not null) throw new InvalidOperationException(err);
            _rows.Clear();
            foreach (var item in items) _rows.Add(item);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void RefreshQa_OnClick(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void SaveQa_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var cycleNo = EntryCycleNoBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(cycleNo))
            {
                MessageBox.Show(this, "Cycle no is required.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            var (cycleItems, err) = await _data.SearchCyclesAsync(cycleNo);
            if (err is not null) throw new InvalidOperationException(err);
            var match = cycleItems.FirstOrDefault(c => string.Equals(c.CycleNo, cycleNo, StringComparison.Ordinal));
            if (match is null)
            {
                MessageBox.Show(this, "Cycle not found. Type the exact cycle number.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var testType = (EntryTestTypeCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Leak";
            var result = (EntryResultCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Pass";
            decimal? measured = null;
            if (decimal.TryParse(EntryMeasuredBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var m))
            {
                measured = m;
            }

            var (qaId, _, saveErr) = await _data.CreateQaTestAsync(
                match.SterilizationId,
                testType,
                DateTime.UtcNow,
                result,
                measured,
                unit: null,
                notes: EntryNotesBox.Text,
                performedBy: EntryPerformedByBox.Text,
                clientMachine: Environment.MachineName);
            if (saveErr is not null)
            {
                MessageBox.Show(this, saveErr, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            EntryNotesBox.Text = string.Empty;
            EntryMeasuredBox.Text = string.Empty;
            await LoadAsync();
            MessageBox.Show(this, $"Saved QA test #{qaId}.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ApproveQa_OnClick(object sender, RoutedEventArgs e)
    {
        if (QaGrid.SelectedItem is not QaTestListItemDto row) return;
        if (row.ApprovedAtUtc is not null)
        {
            MessageBox.Show(this, "This QA test was already approved.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try
        {
            var (rv, err) = await _data.ApproveQaTestAsync(row.QaTestId, row.RowVersion, remarks: null, clientMachine: Environment.MachineName);
            if (err is not null) throw new InvalidOperationException(err);
            row.RowVersion = rv ?? row.RowVersion;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void PrintQa_OnClick(object sender, RoutedEventArgs e)
    {
        await PrintSelectedAsync(quick: false);
    }

    private async System.Threading.Tasks.Task PrintSelectedAsync(bool quick)
    {
        if (QaGrid.SelectedItem is not QaTestListItemDto row)
        {
            return;
        }
        var reportType = string.Equals(row.TestType, "BowieDick", StringComparison.OrdinalIgnoreCase)
            ? ReportType.BowieDick
            : ReportType.LeakTest;
        var request = new ReportRenderRequestDto
        {
            ReportType = reportType,
            QaTestId = row.QaTestId,
            SterilizationId = row.SterilizationId,
            ClientMachine = Environment.MachineName
        };
        try
        {
            if (quick)
            {
                await _printCoordinator.QuickPrintAsync(request, this);
            }
            else
            {
                await _printCoordinator.ShowPreviewAsync(request, this);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
