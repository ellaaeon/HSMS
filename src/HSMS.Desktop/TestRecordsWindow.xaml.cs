using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HSMS.Application.Services;
using HSMS.Application.Exports;
using HSMS.Desktop.Reporting;
using HSMS.Desktop.TestRecords.ViewModels;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop;

public partial class TestRecordsWindow : UserControl
{
    private readonly TestRecordsViewModel _vm;

    public TestRecordsWindow(
        IHsmsDataService data,
        HSMS.Application.Exports.IExcelWorkbookExportService? workbookExport,
        PrintReportCoordinator? printCoordinator)
    {
        _vm = new TestRecordsViewModel(data, workbookExport, printCoordinator);
        InitializeComponent();
        DataContext = _vm;
        RecordsGrid.SelectionChanged += RecordsGrid_OnSelectionChanged;
    }

    private void NavList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox list) return;
        if (list.SelectedItem is not ListBoxItem item) return;
        _vm.ActiveCategory = CategoryFromTag(item.Tag);
    }

    private static SterilizationQaCategory CategoryFromTag(object? tag)
    {
        var t = tag as string ?? tag?.ToString();
        return t switch
        {
            "Dashboard" => SterilizationQaCategory.Dashboard,
            "BowieDick" => SterilizationQaCategory.BowieDick,
            "LeakTest" => SterilizationQaCategory.LeakTest,
            "WarmUpTest" => SterilizationQaCategory.WarmUpTest,
            "InstrumentTests" => SterilizationQaCategory.InstrumentTests,
            "Bi" => SterilizationQaCategory.BiologicalIndicator,
            "PPM" => SterilizationQaCategory.Ppm,
            "Maintenance" => SterilizationQaCategory.MaintenanceCalibration,
            "Incidents" => SterilizationQaCategory.FailedIncident,
            "Archive" => SterilizationQaCategory.Archived,
            _ => SterilizationQaCategory.Dashboard
        };
    }

    private void StatusCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        var raw = (cb.SelectedItem as ComboBoxItem)?.Content as string;
        if (string.IsNullOrWhiteSpace(raw) || raw == "(all)")
        {
            _vm.StatusFilter = null;
            return;
        }
        _vm.StatusFilter = Enum.TryParse<SterilizationQaWorkflowStatus>(raw, ignoreCase: true, out var s) ? s : null;
    }

    private void RecordsGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            var selected = RecordsGrid.SelectedItems.OfType<SterilizationQaRecordListItemDto>().ToList();
            _vm.SetBulkSelection(selected);
        }
        catch
        {
            _vm.SetBulkSelection([]);
        }
    }

}

