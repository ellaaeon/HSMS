using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using HSMS.Application.Exports;
using HSMS.Application.Services;
using HSMS.Desktop.Controls;
using HSMS.Desktop.Models;
using HSMS.Desktop.Reporting;
using HSMS.Desktop.Ui;
using HSMS.Shared.Contracts;
using HSMS.Shared.Contracts.Reporting;
using HSMS.Shared.Time;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WPF;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using SkiaSharp;
using QColors = QuestPDF.Helpers.Colors;
using QPageSizes = QuestPDF.Helpers.PageSizes;
using QContainer = QuestPDF.Infrastructure.IContainer;

namespace HSMS.Desktop;

public partial class MainWindow : Window
{
    private static string? _biLogStickyInitials;
    /// <summary>Click position normalized to <see cref="DataGridCell"/> size (display mode); used after swap to edit template.</summary>
    private double? _biLogMergeNx;
    private double? _biLogMergeNy;
    private BiLogSheetUpdatePayload? _biLogEditSnapshot;
    private readonly SemaphoreSlim _biLogResultSaveMutex = new(1, 1);
    private readonly DispatcherTimer _biComplianceTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private readonly HashSet<string> _biCompliancePopupKeys = [];

    private static bool _schemaWarningShown;
    private const string TemperatureModeHigh = "High temperature";
    private const string TemperatureModeLow = "Low temperature";
    private static readonly string[] CycleProgramChoices = ["Instruments", "Bowie Dick", "Leak test", "Warm up"];
    private static readonly string[] BiLogTemperatureFilters = ["(All)", TemperatureModeHigh, TemperatureModeLow];
    private static readonly string[] HourChoices = ["(Any)", "00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23"];

    private readonly ObservableCollection<CycleItemRow> _items = [];
    private readonly ObservableCollection<LoadRecordGridRow> _loadRecords = [];
    private readonly ObservableCollection<AnalyticsOperatorSummaryRowDto> _analyticsOperators = [];
    private readonly ObservableCollection<AnalyticsSterilizerSummaryRowDto> _analyticsSterilizers = [];
    private readonly ObservableCollection<AnalyticsTripleBarRow> _analyticsOperatorBars = [];
    private readonly ObservableCollection<AnalyticsTripleBarRow> _analyticsSterilizerBars = [];
    private readonly ObservableCollection<AnalyticsPlainRow> _analyticsPlainOperators = [];
    private readonly ObservableCollection<AnalyticsPlainRow> _analyticsPlainSterilizers = [];
    private bool _suppressAnalyticsUserSelectionChanged;
    private SterilizationAnalyticsDto? _analyticsLastResult;
    private BiAnalyticsDto? _analyticsLastBiResult;
    private AnalyticsFilterDto _analyticsAppliedFilter = new();
    private readonly ObservableCollection<AnalyticsPresetListItemDto> _analyticsPresets = [];
    private int? _selectedAnalyticsPresetId;
    private DateTime _analyticsLastFromLocal;
    private DateTime _analyticsLastToLocal;
    private string[] _chartOperatorLabels = [];
    private string[] _chartSterilizerLabels = [];
    private int[] _chartSterilizerIds = [];
    private AnalyticsDaySummaryRowDto[] _chartDayPoints = [];
    private BiAnalyticsDayTrendRowDto[] _chartBiDayPoints = [];
    private string[] _chartOperatorQtySnapshotLabels = [];

    /// <summary>Matches <see cref="ApplyAnalyticsChartsDashboard"/> pie slice order (Draft, Completed, Voided).</summary>
    private static readonly string[] AnalyticsPieStatusSearch =
    [
        LoadRecordCycleStatuses.Draft,
        LoadRecordCycleStatuses.Completed,
        LoadRecordCycleStatuses.Voided
    ];
    private readonly ObservableCollection<InstrumentCheckRow> _instrumentChecks = [];
    private ICollectionView? _instrumentChecksView;
    private readonly ObservableCollection<SterilizationSearchItemDto> _homeRecentCycles = [];
    private readonly ObservableCollection<BiLogSheetRowDto> _biRows = [];
    private readonly ObservableCollection<BiComplianceAlertRow> _biComplianceAlerts = [];
    public ObservableCollection<string> ItemDescriptionOptions { get; } = [];
    private readonly List<DepartmentItemListItemDto> _departmentItems = [];
    private readonly List<DoctorRoomListItemDto> _doctorRooms = [];
    private readonly IHsmsDataService _data;
    private readonly HsmsAuthService _auth;
    private readonly LoginResponseDto _session;
    private readonly string _signedInUsername;
    private readonly PrintReportCoordinator? _printCoordinator;
    private readonly IExcelExportService? _excelExport;
    private readonly IExcelWorkbookExportService? _workbookExport;
    private int? _currentSterilizationId;
    private string? _currentRowVersion;
    /// <summary>Pressure is not edited on Register load; on update we send this so the DB value is not cleared.</summary>
    private decimal? _loadedCyclePressure;

    /// <summary>Set when user confirms log out to return to the staff portal (sign-in / create account).</summary>
    public bool ReturnToPortal { get; private set; }

    // Kept for backward-compat; current analytics dashboard uses LiveCharts controls directly.
    public ObservableCollection<AnalyticsTripleBarRow> AnalyticsOperatorBars => _analyticsOperatorBars;
    public ObservableCollection<AnalyticsTripleBarRow> AnalyticsSterilizerBars => _analyticsSterilizerBars;

    private sealed class AnalyticsPlainRow
    {
        public int Rank { get; init; }
        public string Name { get; init; } = string.Empty;
        public int Loads { get; init; }
        public int Pcs { get; init; }
        public int Qty { get; init; }
    }
    public ObservableCollection<string> DepartmentOptions { get; } = [];
    public ObservableCollection<string> DoctorRoomOptions { get; } = [];
    public ObservableCollection<BiComplianceAlertRow> BiComplianceAlerts => _biComplianceAlerts;

    public sealed class BiComplianceAlertRow
    {
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
    }

    /// <summary>Lines to save with the cycle (item + pcs + qty + optional department/doctor per row).</summary>
    public ObservableCollection<RegisterPendingItemLine> RegisterPendingItems { get; } = [];

    /// <summary>Load Records status editor (Draft / Completed / Voided).</summary>
    public IReadOnlyList<string> LoadRecordsCycleStatusChoices { get; } = LoadRecordCycleStatuses.All;

    private string CurrentStaffInitials()
    {
        // Prefer 2–4 character initials derived from profile, else fall back to username.
        var first = (_session.Profile?.FirstName ?? "").Trim();
        var last = (_session.Profile?.LastName ?? "").Trim();
        var initials = $"{(first.Length > 0 ? first[0].ToString() : "")}{(last.Length > 0 ? last[0].ToString() : "")}".ToUpperInvariant();
        initials = initials.Trim();
        if (initials.Length is >= 2 and <= 4) return initials;

        var u = (_signedInUsername ?? "").Trim();
        if (u.Length is >= 2 and <= 4) return u.ToUpperInvariant();
        if (u.Length > 4) return u[..4].ToUpperInvariant();
        return string.IsNullOrWhiteSpace(u) ? "NA" : u.ToUpperInvariant();
    }

    private BiLogSheetUpdatePayload BiLogStampTimeInitials(BiLogSheetRowDto row, BiLogSheetUpdatePayload afterRaw, BiLogSheetUpdatePayload? before)
    {
        var initials = CurrentStaffInitials();

        var timeInChanged = before is not null && before.BiTimeInUtc != afterRaw.BiTimeInUtc;
        var timeOutChanged = before is not null && before.BiTimeOutUtc != afterRaw.BiTimeOutUtc;

        var tinInit = afterRaw.BiTimeInUtc is null ? null : afterRaw.BiTimeInInitials;
        var toutInit = afterRaw.BiTimeOutUtc is null ? null : afterRaw.BiTimeOutInitials;

        if (timeInChanged)
        {
            // Only auto-stamp when initials are empty; don't overwrite user-entered initials.
            if (afterRaw.BiTimeInUtc is null)
            {
                tinInit = null;
            }
            else if (string.IsNullOrWhiteSpace(tinInit))
            {
                tinInit = initials;
            }
            row.BiTimeInInitials = tinInit;
        }

        if (timeOutChanged)
        {
            // Only auto-stamp when initials are empty; don't overwrite user-entered initials.
            if (afterRaw.BiTimeOutUtc is null)
            {
                toutInit = null;
            }
            else if (string.IsNullOrWhiteSpace(toutInit))
            {
                toutInit = initials;
            }
            row.BiTimeOutInitials = toutInit;
        }

        return afterRaw with
        {
            BiTimeInInitials = tinInit,
            BiTimeOutInitials = toutInit
        };
    }

    private enum BiQuickRange
    {
        Custom = 0,
        ThisWeek = 1,
        ThisMonth = 2,
        ThisYear = 3
    }

    private static (DateTime fromLocalDate, DateTime toLocalDate) GetLocalRange(BiQuickRange range, DateTime nowLocal)
    {
        var today = nowLocal.Date;
        return range switch
        {
            BiQuickRange.ThisWeek => (StartOfWeekMonday(today), StartOfWeekMonday(today).AddDays(6)),
            BiQuickRange.ThisMonth => (new DateTime(today.Year, today.Month, 1), new DateTime(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)),
            BiQuickRange.ThisYear => (new DateTime(today.Year, 1, 1), new DateTime(today.Year, 12, 31)),
            _ => (today, today)
        };
    }

    private static DateTime StartOfWeekMonday(DateTime localDate)
    {
        var day = (int)localDate.DayOfWeek; // Sunday=0
        var offset = day == 0 ? 6 : day - 1; // Monday=0
        return localDate.AddDays(-offset).Date;
    }

    private string DescribeBiQuickRange()
    {
        if (BiQuickRangeCombo?.SelectedItem is not ComboBoxItem item) return "";
        var key = item.Tag?.ToString() ?? "";
        return key switch
        {
            "this_week" => "This week",
            "this_month" => "This month",
            "this_year" => "This year",
            _ => ""
        };
    }

    public MainWindow(IHsmsDataService dataService, LoginResponseDto session, HsmsAuthService authService)
        : this(dataService, session, authService, null)
    {
    }

    public MainWindow(
        IHsmsDataService dataService,
        LoginResponseDto session,
        HsmsAuthService authService,
        PrintReportCoordinator? printCoordinator)
        : this(dataService, session, authService, printCoordinator, null)
    {
    }

    public MainWindow(
        IHsmsDataService dataService,
        LoginResponseDto session,
        HsmsAuthService authService,
        PrintReportCoordinator? printCoordinator,
        IExcelExportService? excelExportService,
        IExcelWorkbookExportService? workbookExportService = null)
    {
        _data = dataService;
        _auth = authService;
        _session = session;
        _signedInUsername = session.Username;
        _printCoordinator = printCoordinator;
        _excelExport = excelExportService;
        _workbookExport = workbookExportService;
        InitializeComponent();
        DataContext = this;

        // LiveCharts controls can handle mouse wheel internally; forward it so the page can scroll naturally.
        HookAnalyticsMouseWheelForwarding();

        MainContentHostGrid.AddHandler(
            FrameworkElement.RequestBringIntoViewEvent,
            new RequestBringIntoViewEventHandler(MainContent_OnRequestBringIntoView));
        Title = $"HSMS — {session.Username}";
        var display = $"{session.Profile?.FirstName} {session.Profile?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(display))
        {
            display = session.Username;
        }

        SetAccountHeader(display);

        if (!string.Equals(session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            AdminPanelSeparator.Visibility = Visibility.Collapsed;
            AdminPanelButton.Visibility = Visibility.Collapsed;
        }

        // Load Records is read-only list; no item editing here.

        CycleProgramCombo.Items.Clear();
        foreach (var c in CycleProgramChoices)
        {
            CycleProgramCombo.Items.Add(c);
        }

        CycleProgramCombo.SelectedIndex = 0;
        CycleEntryDatePicker.SelectedDate = HsmsDeploymentTimeZone.NowInDeploymentZone().Date;
        SterTempHighRadio.IsChecked = true;
        BiIndicatorYesRadio.IsChecked = true;
        BiIndicatorYesRadio.Checked += (_, _) => SyncBiIndicatorUi();
        BiIndicatorNoRadio.Checked += (_, _) => SyncBiIndicatorUi();

        // Common quick-pick values (still editable).
        RegisterPcsCombo.ItemsSource = Enumerable.Range(1, 20).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
        RegisterQtyCombo.ItemsSource = Enumerable.Range(1, 20).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
        RegisterPcsCombo.Text = "1";
        RegisterQtyCombo.Text = "1";

        SetStyleNav(HomeButton);
        HeaderTitleText.Text = "Home";
        ShowHome();
        SetStatus("Home — summary of recent cycles. F5 refreshes this page.");

        Loaded += MainWindow_OnLoaded;
        PreviewKeyDown += MainWindow_PreviewKeyDown;

        CycleNoTextBox.KeyDown += async (_, ev) =>
        {
            if (ev.Key != Key.Enter) return;
            ev.Handled = true;
            await SafeRunAsync(HandleCycleNoEnterAsync);
        };

        LoadRecordsGrid.ItemsSource = _loadRecords;
        LoadRecordsGrid.BeginningEdit += LoadRecordsGrid_OnBeginningEdit;
        LoadRecordsGrid.PreparingCellForEdit += LoadRecordsGrid_OnPreparingCellForEdit;
        LoadRecordsGrid.CellEditEnding += LoadRecordsGrid_OnCellEditEnding;
        LoadRecordsGrid.PreviewKeyDown += LoadRecordsGrid_OnPreviewKeyDown;
        TestRecordsHost.Content = new TestRecordsWindow(_data, _workbookExport, _printCoordinator);
        HomeRecentCyclesGrid.ItemsSource = _homeRecentCycles;
        LoadRecordsSearchButton.Click += async (_, _) => await SafeRunAsync(() => RefreshLoadRecordsAsync(searchAction: true));
        LoadRecordsRefreshButton.Click += async (_, _) => await SafeRunAsync(() => RefreshLoadRecordsAsync(searchAction: false));

        SaveButton.Click += async (_, _) => await SafeRunAsync(SaveCycleAsync);
        RefreshButton.Click += async (_, _) => await SafeRunAsync(RefreshCycleAsync);
        PrintButton.Click += async (_, _) => await SafeRunAsync(PrintCurrentScreenAsync);

        AnalyticsRefreshButton.Click += async (_, _) => await SafeRunAsync(RefreshAnalyticsAsync);
        AnalyticsPlainTextToggle.Checked += (_, _) => ApplyAnalyticsPlainTextMode(isPlainText: true);
        AnalyticsPlainTextToggle.Unchecked += (_, _) => ApplyAnalyticsPlainTextMode(isPlainText: false);
        AnalyticsScopeCombo.SelectionChanged += async (_, _) =>
        {
            ApplyAnalyticsScopeUi();
            await SafeRunAsync(EnsureAnalyticsUserListAsync);
        };
        AnalyticsUserCombo.SelectionChanged += async (_, _) =>
        {
            // When admin picks an individual user, refresh immediately.
            if (_suppressAnalyticsUserSelectionChanged)
            {
                return;
            }

            if (AnalyticsScopePanel.Visibility == Visibility.Visible
                && ((AnalyticsScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "")
                .Contains("Individual", StringComparison.OrdinalIgnoreCase))
            {
                var picked = AnalyticsUserCombo.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(picked) || picked.StartsWith("("))
                {
                    return;
                }

                await SafeRunAsync(RefreshAnalyticsAsync);
            }
        };

        AnalyticsPlainTopOperatorsGrid.ItemsSource = _analyticsPlainOperators;
        AnalyticsPlainTopSterilizersGrid.ItemsSource = _analyticsPlainSterilizers;
        SetupAnalyticsBreakdownUi();
        AnalyticsExportCsvButton.Click += (_, _) => ExportAnalyticsCsv();
        AnalyticsExportXlsxButton.Click += async (_, _) => await SafeRunAsync(ExportAnalyticsXlsxAsync);
        AnalyticsExportPdfButton.Click += async (_, _) => await SafeRunAsync(ExportAnalyticsPdfAsync);
        AnalyticsPrintButton.Click += async (_, _) => await SafeRunAsync(PrintAnalyticsAsync);
        AnalyticsFiltersApplyButton.Click += async (_, _) => await SafeRunAsync(ApplyAnalyticsFiltersAsync);
        AnalyticsFiltersResetButton.Click += async (_, _) => await SafeRunAsync(ResetAnalyticsFiltersAsync);
        SetupAnalyticsFilterControls();

        AnalyticsPresetsCombo.ItemsSource = _analyticsPresets;
        AnalyticsPresetsCombo.DisplayMemberPath = nameof(AnalyticsPresetListItemDto.Name);
        AnalyticsPresetsCombo.SelectionChanged += async (_, _) => await SafeRunAsync(LoadSelectedAnalyticsPresetAsync);
        AnalyticsPresetSaveButton.Click += async (_, _) => await SafeRunAsync(SaveAnalyticsPresetAsync);
        AnalyticsPresetRenameButton.Click += async (_, _) => await SafeRunAsync(RenameAnalyticsPresetAsync);
        AnalyticsPresetDeleteButton.Click += async (_, _) => await SafeRunAsync(DeleteAnalyticsPresetAsync);
        AnalyticsPresetSetDefaultButton.Click += async (_, _) => await SafeRunAsync(SetDefaultAnalyticsPresetAsync);
        AnalyticsBreakdownCombo.SelectionChanged += (_, _) =>
        {
            ApplyAnalyticsBreakdownView();
        };
        AnalyticsBreakdownGrid.MouseDoubleClick += AnalyticsBreakdownGrid_OnMouseDoubleClick;
        AnalyticsPlainTopOperatorsGrid.MouseDoubleClick += AnalyticsPlainTopOperatorsGrid_OnMouseDoubleClick;
        AnalyticsPlainTopSterilizersGrid.MouseDoubleClick += AnalyticsPlainTopSterilizersGrid_OnMouseDoubleClick;
        AnalyticsPlainBreakdownCombo.SelectionChanged += (_, _) => ApplyAnalyticsBreakdownView();
        AnalyticsPlainBreakdownGrid.MouseDoubleClick += AnalyticsBreakdownGrid_OnMouseDoubleClick;
        HookAnalyticsChartPointDrilldown();

        InstrumentsCheckGrid.ItemsSource = _instrumentChecks;
        _instrumentChecksView = CollectionViewSource.GetDefaultView(InstrumentsCheckGrid.ItemsSource);
        if (_instrumentChecksView is not null)
        {
            _instrumentChecksView.Filter = InstrumentsCheckFilter;
        }

        InstrumentsCheckSearchBox.TextChanged += (_, _) => _instrumentChecksView?.Refresh();
        InstrumentsCheckAddButton.Click += async (_, _) => await SafeRunAsync(AddInstrumentCheckRowAsync);
        InstrumentsCheckExportPdfButton.Click += async (_, _) => await SafeRunAsync(ExportInstrumentsCheckPdfAsync);
        // Instruments Check: match Register Load quick-entry behavior (Enter moves forward, arrows navigate choices).
        QuickEntryNavigationBehavior.SetIsEnabled(InstrumentsCheckSearchBox, true);
        QuickEntryNavigationBehavior.SetIsEnabled(InstrumentsCheckItemCombo, true);
        QuickEntryNavigationBehavior.SetIsEnabled(InstrumentsCheckSerialTextBox, true);
        InstrumentsCheckCheckedByTextBox.KeyDown += MoveFocusOnEnter;
        QuickEntryNavigationBehavior.SetIsEnabled(InstrumentsCheckWitnessByTextBox, true);
        QuickEntryNavigationBehavior.SetIsEnabled(InstrumentsCheckRemarksTextBox, true);
        InstrumentsCheckAddButton.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            InstrumentsCheckAddButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };

        SterTempHighRadio.KeyDown += MoveFocusOnEnter;
        SterTempLowRadio.KeyDown += MoveFocusOnEnter;
        CycleEntryDatePicker.KeyDown += MoveFocusOnEnter;
        CycleProgramCombo.KeyDown += MoveFocusOnEnter;
        SterilizerCombo.KeyDown += MoveFocusOnEnter;
        BiIndicatorYesRadio.KeyDown += MoveFocusOnEnter;
        BiIndicatorNoRadio.KeyDown += MoveFocusOnEnter;
        ImplantsYesRadio.KeyDown += MoveFocusOnEnter;
        ImplantsNoRadio.KeyDown += MoveFocusOnEnter;
        // Register Load quick-entry navigation (Enter/Left/Right) + editable ComboBox auto-match/commit.
        QuickEntryNavigationBehavior.SetIsEnabled(OperatorTextBox, true);
        QuickEntryNavigationBehavior.SetIsEnabled(TemperatureTextBox, true);
        BiResultCombo.KeyDown += MoveFocusOnEnter;
        QuickEntryNavigationBehavior.SetIsEnabled(BiLotNoTextBox, true);
        CycleStatusCombo.KeyDown += MoveFocusOnEnter;
        QuickEntryNavigationBehavior.SetIsEnabled(DoctorRoomCombo, true);
        QuickEntryNavigationBehavior.SetIsEnabled(DepartmentCombo, true);
        QuickEntryNavigationBehavior.SetIsEnabled(NotesTextBox, true);
        QuickEntryNavigationBehavior.SetIsEnabled(RegisterItemCombo, true);
        QuickEntryNavigationBehavior.SetIsEnabled(RegisterPcsCombo, true);
        QuickEntryNavigationBehavior.SetIsEnabled(RegisterQtyCombo, true);
        RegisterItemCombo.SelectionChanged += (_, _) => ApplySelectedItemDefaults();
        RegisterItemCombo.LostKeyboardFocus += (_, _) => ApplySelectedItemDefaults();
        DepartmentCombo.LostKeyboardFocus += (_, _) => ApplySelectedItemDefaults();
        DoctorRoomCombo.SelectionChanged += (_, _) => SyncDepartmentFromDoctorSelection();
        DoctorRoomCombo.LostKeyboardFocus += (_, _) => SyncDepartmentFromDoctorSelection();
        // no item grid in Load Records

        BiLogGrid.ItemsSource = _biRows;
        _biRows.CollectionChanged += (_, _) => UpdateBiLogGridHeight();
        foreach (var t in BiLogTemperatureFilters)
        {
            BiTypeCombo.Items.Add(t);
        }

        // Quick ranges for professional reporting.
        BiQuickRangeCombo.Items.Add(new ComboBoxItem { Content = "Custom", Tag = "custom" });
        BiQuickRangeCombo.Items.Add(new ComboBoxItem { Content = "This week", Tag = "this_week" });
        BiQuickRangeCombo.Items.Add(new ComboBoxItem { Content = "This month", Tag = "this_month" });
        BiQuickRangeCombo.Items.Add(new ComboBoxItem { Content = "This year", Tag = "this_year" });
        BiQuickRangeCombo.SelectedIndex = 0;
        BiQuickRangeCombo.SelectionChanged += async (_, _) =>
        {
            if (BiQuickRangeCombo.SelectedItem is not ComboBoxItem it) return;
            var tag = (it.Tag?.ToString() ?? "custom").Trim();
            if (tag == "custom")
            {
                return;
            }

            var nowLocal = HsmsDeploymentTimeZone.UtcToDeployment(DateTime.UtcNow);
            var range = tag switch
            {
                "this_week" => BiQuickRange.ThisWeek,
                "this_month" => BiQuickRange.ThisMonth,
                "this_year" => BiQuickRange.ThisYear,
                _ => BiQuickRange.Custom
            };
            var (from, to) = GetLocalRange(range, nowLocal);

            BiFromDate.SelectedDate = from;
            BiToDate.SelectedDate = to;
            BiFromHourCombo.SelectedIndex = 0;
            BiToHourCombo.SelectedIndex = 0;

            await SafeRunAsync(LoadBiLogSheetAsync);
        };

        BiFromHourCombo.Items.Clear();
        BiToHourCombo.Items.Clear();
        foreach (var h in HourChoices)
        {
            BiFromHourCombo.Items.Add(h);
            BiToHourCombo.Items.Add(h);
        }

        BiFromHourCombo.SelectedIndex = 0;
        BiToHourCombo.SelectedIndex = 0;

        BiTypeCombo.SelectedIndex = 0;
        SyncBiIndicatorUi();
        BiGoButton.Click += async (_, _) => await SafeRunAsync(LoadBiLogSheetAsync);
        BiClearButton.Click += async (_, _) =>
        {
            BiCycleSearchText.Text = "";
            BiFromDate.SelectedDate = null;
            BiToDate.SelectedDate = null;
            BiFromHourCombo.SelectedIndex = 0;
            BiToHourCombo.SelectedIndex = 0;
            BiTypeCombo.SelectedIndex = 0;
            BiQuickRangeCombo.SelectedIndex = 0;
            await SafeRunAsync(LoadBiLogSheetAsync);
        };
        BiPrintButton.Click += async (_, _) => await SafeRunAsync(PrintBiLogSheetAsync);
        BiPreviewButton.Click += async (_, _) => await SafeRunAsync(PreviewBiLogSheetAsync);
        BiExportPdfButton.Click += async (_, _) => await SafeRunAsync(ExportBiLogSheetPdfAsync);
        _biComplianceTimer.Tick += (_, _) =>
        {
            if (BiLogSheetsView.Visibility == Visibility.Visible)
            {
                RefreshBiComplianceAlerts(showDuePopups: true);
            }
        };
        _biComplianceTimer.Start();
    }

    private void HookAnalyticsMouseWheelForwarding()
    {
        // Analytics is now text-only; no chart controls to forward.
        return;
    }

    private void UpdateBiLogGridHeight()
    {
        if (BiLogGrid is null)
        {
            return;
        }

        // BI sheet lives in a *-height row with its own scrollbar; avoid a fixed Height so layout stays viewport-fit.
        BiLogGrid.ClearValue(FrameworkElement.HeightProperty);
    }

    private void NavCollapseButton_OnClick(object sender, RoutedEventArgs e)
    {
        BiCloseOpenTooltips();
        Keyboard.ClearFocus();
        NavMenuColumnDefinition.MinWidth = 0;
        NavMenuColumnDefinition.Width = new GridLength(0);
        NavMenuGapColumnDefinition.Width = new GridLength(0);
        NavMenuBorder.Visibility = Visibility.Collapsed;
        NavExpandButton.Visibility = Visibility.Visible;
    }

    private void NavExpandButton_OnClick(object sender, RoutedEventArgs e)
    {
        BiCloseOpenTooltips();
        Keyboard.ClearFocus();
        NavMenuColumnDefinition.MinWidth = 200;
        NavMenuColumnDefinition.Width = new GridLength(220);
        NavMenuGapColumnDefinition.Width = new GridLength(12);
        NavMenuBorder.Visibility = Visibility.Visible;
        NavExpandButton.Visibility = Visibility.Collapsed;
    }

    private void CollapseNavMenuIfVisible()
    {
        if (NavMenuBorder.Visibility != Visibility.Visible)
        {
            return;
        }

        BiCloseOpenTooltips();
        Keyboard.ClearFocus();
        NavMenuColumnDefinition.MinWidth = 0;
        NavMenuColumnDefinition.Width = new GridLength(0);
        NavMenuGapColumnDefinition.Width = new GridLength(0);
        NavMenuBorder.Visibility = Visibility.Collapsed;
        NavExpandButton.Visibility = Visibility.Visible;
    }

    private void BiCloseOpenTooltips()
    {
        // WPF tooltips can remain on-screen when the layout jumps (e.g. collapsing the sidebar).
        // We (1) forcibly close the tooltip for the element under the mouse and (2) toggle ToolTipService.
        try
        {
            if (Mouse.DirectlyOver is DependencyObject over)
            {
                for (var d = over; d != null; d = VisualTreeHelper.GetParent(d))
                {
                    if (d is FrameworkElement fe && fe.ToolTip is not null)
                    {
                        // If the tooltip is an actual ToolTip instance, close it.
                        if (fe.ToolTip is ToolTip tt)
                        {
                            tt.IsOpen = false;
                        }

                        // Also disable/re-enable tooltips for this element to close string-based tooltips.
                        ToolTipService.SetIsEnabled(fe, false);
                        Dispatcher.BeginInvoke(new Action(() => ToolTipService.SetIsEnabled(fe, true)), DispatcherPriority.Background);
                        break;
                    }
                }
            }
        }
        catch
        {
            // best-effort only
        }

        ToolTipService.SetIsEnabled(this, false);
        Dispatcher.BeginInvoke(new Action(() => ToolTipService.SetIsEnabled(this, true)), DispatcherPriority.Background);
    }

    private void HomeButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetStyleNav(HomeButton);
        HeaderTitleText.Text = "Home";
        ShowHome();
        SetStatus("Home — summary of recent cycles. F5 refreshes this page.");
        _ = SafeRunAsync(RefreshHomeDashboardAsync);
    }

    private void HomeGoRegisterButton_OnClick(object sender, RoutedEventArgs e) =>
        CyclesButton_OnClick(RegisterLoadButton, e);

    private void HomeGoLoadRecordsButton_OnClick(object sender, RoutedEventArgs e) =>
        LoadRecordsButton_OnClick(LoadRecordsButton, e);

    private void HomeGoBiLogButton_OnClick(object sender, RoutedEventArgs e) =>
        BiLogSheetsButton_OnClick(BiLogSheetsButton, e);

    private void CyclesButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetStyleNav(active: RegisterLoadButton);
        HeaderTitleText.Text = "Register load";
        ShowRegisterLoad();
        SetStatus("Cycle entry — fill the form below.");
        _ = SafeRunAsync(EnsureCycleNoAsync);
        SterilizerCombo.Focus();
    }

    private void BiLogSheetsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetStyleNav(BiLogSheetsButton);
        HeaderTitleText.Text = "BI Log Sheets";
        ShowBiLogSheets();
        _ = SafeRunAsync(LoadBiLogSheetAsync);
    }

    private void NavPlaceholder_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string tag) return;
        SetStyleNav(active: b);
        HeaderTitleText.Text = tag;
        var msg = tag switch
        {
            "Instruments Check" => "Instruments check (pre/post) will open here in the next pass.",
            "Load Records" => "Load records is now available from the left menu.",
            "BI Log Sheets" => "BI log sheet + BI summary reports will open here — we will port your existing RDLC layouts.",
            "Machines" => "Machines (sterilizers) overview will open here in the next pass.",
            "Inventory" => "Inventory / instruments master and stock will open here in the next pass.",
            "Checklist" => "Checklist screens will open here in the next pass.",
            _ => "This section is scheduled for the next implementation pass."
        };
        HsmsAlertWindow.ShowInfo(this, msg, tag);
    }

    private void LoadRecordsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetStyleNav(LoadRecordsButton);
        HeaderTitleText.Text = "Load records";
        ShowLoadRecords();
        LoadRecordsSearchBox.Text = "";
        _ = SafeRunAsync(() => RefreshLoadRecordsAsync(searchAction: false));
    }

    private void SetStyleNav(Button active)
    {
        var buttons = new List<Button>
        {
            HomeButton,
            RegisterLoadButton,
            InstrumentsCheckButton,
            QaTestsButton,
            LoadRecordsButton,
            BiLogSheetsButton,
            AnalyticsButton,
            MachinesButton,
            InventoryButton,
            ChecklistButton
        };
        if (AdminPanelButton.Visibility == Visibility.Visible)
        {
            buttons.Add(AdminPanelButton);
        }

        foreach (var btn in buttons)
        {
            btn.Style = (Style)FindResource(btn == active ? "NavButtonActive" : "NavButton");
        }
    }

    private async void MaintenanceButton_OnClick(object sender, RoutedEventArgs e)
    {
        await MaintenanceButton_OnClickAsync(sender, e);
    }

    private async void AdminPanelButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!string.Equals(_session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SetStyleNav(AdminPanelButton);
        HeaderTitleText.Text = "Admin panel";
        await MaintenanceButton_OnClickAsync(sender, e);
        SetStyleNav(RegisterLoadButton);
        HeaderTitleText.Text = "Register load";
        ShowRegisterLoad();
    }

    private async Task MaintenanceButton_OnClickAsync(object sender, RoutedEventArgs e)
    {
        var win = new MaintenanceWindow(_data, _excelExport) { Owner = this };
        win.ShowDialog();
        await SafeRunAsync(LoadSterilizersAsync);
        await SafeRunAsync(LoadDepartmentAndDoctorOptionsAsync);
    }

    private void QaTestsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetStyleNav(QaTestsButton);
        HeaderTitleText.Text = "Test Records (QA)";
        ShowTestRecords();
        SetStatus("Test Records (QA) — sterilization QA records and workflows.");
    }

    private void MainContent_OnRequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        // Register load is screen-fit without an outer scrollbar; suppress ScrollViewer reacting to keyboard focus navigation.
        if (RegisterLoadShell.Visibility == Visibility.Visible)
        {
            e.Handled = true;
        }
    }

    private void ShowHome()
    {
        MainContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Visible;
        AnalyticsTitle.Visibility = Visibility.Collapsed;
        InstrumentsCheckTitle.Visibility = Visibility.Collapsed;
        TestRecordsTitle.Visibility = Visibility.Collapsed;

        RegisterLoadShell.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
        InstrumentsCheckView.Visibility = Visibility.Collapsed;
        TestRecordsHost.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
    }

    private void ShowRegisterLoad()
    {
        MainContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        RegisterLoadTitle.Visibility = Visibility.Visible;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;
        AnalyticsTitle.Visibility = Visibility.Collapsed;
        InstrumentsCheckTitle.Visibility = Visibility.Collapsed;
        TestRecordsTitle.Visibility = Visibility.Collapsed;

        RegisterLoadShell.Visibility = Visibility.Visible;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
        InstrumentsCheckView.Visibility = Visibility.Collapsed;
        TestRecordsHost.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;
    }

    private void ShowLoadRecords()
    {
        MainContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Visible;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;
        AnalyticsTitle.Visibility = Visibility.Collapsed;
        InstrumentsCheckTitle.Visibility = Visibility.Collapsed;
        TestRecordsTitle.Visibility = Visibility.Collapsed;

        RegisterLoadShell.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Visible;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
        InstrumentsCheckView.Visibility = Visibility.Collapsed;
        TestRecordsHost.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;
    }

    private void ShowBiLogSheets()
    {
        MainContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;
        AnalyticsTitle.Visibility = Visibility.Collapsed;
        InstrumentsCheckTitle.Visibility = Visibility.Collapsed;
        TestRecordsTitle.Visibility = Visibility.Collapsed;

        RegisterLoadShell.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Visible;
        AnalyticsView.Visibility = Visibility.Collapsed;
        InstrumentsCheckView.Visibility = Visibility.Collapsed;
        TestRecordsHost.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;
    }

    private void ShowInstrumentsCheck()
    {
        MainContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;
        AnalyticsTitle.Visibility = Visibility.Collapsed;
        InstrumentsCheckTitle.Visibility = Visibility.Visible;
        TestRecordsTitle.Visibility = Visibility.Collapsed;

        RegisterLoadShell.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
        InstrumentsCheckView.Visibility = Visibility.Visible;
        TestRecordsHost.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;
    }

    private void ShowTestRecords()
    {
        MainContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;
        AnalyticsTitle.Visibility = Visibility.Collapsed;
        InstrumentsCheckTitle.Visibility = Visibility.Collapsed;
        TestRecordsTitle.Visibility = Visibility.Visible;

        RegisterLoadShell.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Collapsed;
        InstrumentsCheckView.Visibility = Visibility.Collapsed;
        TestRecordsHost.Visibility = Visibility.Visible;
        HomeView.Visibility = Visibility.Collapsed;
    }

    private void ShowAnalytics()
    {
        MainContentScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;
        AnalyticsTitle.Visibility = Visibility.Visible;
        InstrumentsCheckTitle.Visibility = Visibility.Collapsed;
        TestRecordsTitle.Visibility = Visibility.Collapsed;

        RegisterLoadShell.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        AnalyticsView.Visibility = Visibility.Visible;
        InstrumentsCheckView.Visibility = Visibility.Collapsed;
        TestRecordsHost.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;

        // Permissions scaffold: currently all authenticated Staff/Admin can view/export/print.
        // This is where we would disable/hide controls if we later add per-account permissions.
        var canExport = true;
        var canPrint = true;
        AnalyticsExportCsvButton.IsEnabled = canExport;
        AnalyticsExportPdfButton.IsEnabled = canExport;
        AnalyticsExportXlsxButton.IsEnabled = canExport;
        AnalyticsPrintButton.IsEnabled = canPrint;
    }

    private void InstrumentsCheckButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetStyleNav(InstrumentsCheckButton);
        HeaderTitleText.Text = "Instruments Check";
        ShowInstrumentsCheck();
        SetStatus("Instruments check — add items and export to PDF.");
        InstrumentsCheckCheckedByTextBox.Text = GetSignedInDisplayName();
        _ = SafeRunAsync(RefreshInstrumentChecksAsync);
        InstrumentsCheckItemCombo.Focus();
    }

    private async Task RefreshInstrumentChecksAsync()
    {
        var (items, err) = await _data.SearchInstrumentChecksAsync(query: null, take: 500);
        if (err is not null)
        {
            SetStatus($"Could not load instruments check rows ({err}).");
            return;
        }

        _instrumentChecks.Clear();
        foreach (var it in items.OrderByDescending(x => x.CheckedAtUtc))
        {
            _instrumentChecks.Add(new InstrumentCheckRow
            {
                CheckedAtLocal = HsmsDeploymentTimeZone.UtcToDeployment(it.CheckedAtUtc),
                ItemName = it.ItemName,
                SerialReference = it.SerialReference ?? "",
                CheckedBy = it.CheckedByName,
                WitnessBy = it.WitnessByName ?? "",
                Remarks = it.Remarks ?? ""
            });
        }

        _instrumentChecksView?.Refresh();
        SetStatus($"Loaded instruments check rows ({_instrumentChecks.Count}).");
    }

    private void AnalyticsButton_OnClick(object sender, RoutedEventArgs e)
    {
        SetStyleNav(AnalyticsButton);
        HeaderTitleText.Text = "Analytics";
        ShowAnalytics();

        var nowLocal = HsmsDeploymentTimeZone.NowInDeploymentZone().Date;
        AnalyticsFromDate.SelectedDate ??= nowLocal.AddDays(-6);
        AnalyticsToDate.SelectedDate ??= nowLocal;

        var isAdmin = string.Equals(_session.Role, "Admin", StringComparison.OrdinalIgnoreCase);
        AnalyticsScopePanel.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        AnalyticsUserPanel.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (!isAdmin)
        {
            AnalyticsUserCombo.Items.Clear();
            AnalyticsUserCombo.Items.Add(GetSignedInDisplayName());
            AnalyticsUserCombo.SelectedIndex = 0;
        }
        ApplyAnalyticsScopeUi();
        ApplyAnalyticsPlainTextMode(isPlainText: AnalyticsPlainTextToggle.IsChecked == true);

        _ = SafeRunAsync(EnsureAnalyticsPresetsLoadedAsync);
        _ = SafeRunAsync(EnsureAnalyticsFilterLookupsLoadedAsync);
        _ = SafeRunAsync(RefreshAnalyticsAsync);
    }

    private void SetupAnalyticsFilterControls()
    {
        AnalyticsFilterStatusCombo.Items.Clear();
        AnalyticsFilterStatusCombo.Items.Add("(All)");
        foreach (var s in LoadRecordCycleStatuses.All)
        {
            AnalyticsFilterStatusCombo.Items.Add(s);
        }
        AnalyticsFilterStatusCombo.SelectedIndex = 0;

        AnalyticsFilterImplantsCombo.Items.Clear();
        AnalyticsFilterImplantsCombo.Items.Add("(All)");
        AnalyticsFilterImplantsCombo.Items.Add("Yes");
        AnalyticsFilterImplantsCombo.Items.Add("No");
        AnalyticsFilterImplantsCombo.SelectedIndex = 0;

        AnalyticsFilterBiResultCombo.Items.Clear();
        AnalyticsFilterBiResultCombo.Items.Add("(All)");
        foreach (var r in BiResultValues.All)
        {
            AnalyticsFilterBiResultCombo.Items.Add(r);
        }
        AnalyticsFilterBiResultCombo.SelectedIndex = 0;
    }

    private readonly ObservableCollection<SterilizerListItemDto> _analyticsSterilizerLookup = [];

    private async Task EnsureAnalyticsFilterLookupsLoadedAsync()
    {
        var (sters, sterErr) = await _data.GetSterilizersAsync();
        if (sterErr is not null)
        {
            SetStatus($"Analytics filters: {sterErr}");
            return;
        }

        _analyticsSterilizerLookup.Clear();
        foreach (var s in sters)
        {
            _analyticsSterilizerLookup.Add(s);
        }

        AnalyticsFilterSterilizerCombo.Items.Clear();
        AnalyticsFilterSterilizerCombo.Items.Add("(All)");
        foreach (var s in _analyticsSterilizerLookup.OrderBy(x => x.SterilizerNo))
        {
            AnalyticsFilterSterilizerCombo.Items.Add($"{s.SterilizerNo} (#{s.SterilizerId})");
        }
        AnalyticsFilterSterilizerCombo.SelectedIndex = 0;
    }

    private async Task ApplyAnalyticsFiltersAsync()
    {
        await RefreshAnalyticsAsync();
    }

    private async Task ResetAnalyticsFiltersAsync()
    {
        AnalyticsFilterSterilizerCombo.SelectedIndex = 0;
        AnalyticsFilterStatusCombo.SelectedIndex = 0;
        AnalyticsFilterImplantsCombo.SelectedIndex = 0;
        AnalyticsFilterBiResultCombo.SelectedIndex = 0;
        AnalyticsFilterSearchTextBox.Text = "";
        await RefreshAnalyticsAsync();
    }

    private async Task EnsureAnalyticsPresetsLoadedAsync()
    {
        var (items, err) = await _data.ListAnalyticsPresetsAsync();
        if (err is not null)
        {
            SetStatus($"Analytics presets: {err}");
            return;
        }

        _analyticsPresets.Clear();
        _analyticsPresets.Add(new AnalyticsPresetListItemDto { PresetId = 0, Name = "(no preset)", IsDefault = false });
        foreach (var it in items)
        {
            _analyticsPresets.Add(it);
        }

        // Prefer default preset if one exists.
        var defaultPreset = items.FirstOrDefault(x => x.IsDefault);
        if (defaultPreset is not null)
        {
            AnalyticsPresetsCombo.SelectedItem = _analyticsPresets.FirstOrDefault(x => x.PresetId == defaultPreset.PresetId);
        }
        else
        {
            AnalyticsPresetsCombo.SelectedIndex = 0;
        }
    }

    private async Task LoadSelectedAnalyticsPresetAsync()
    {
        if (AnalyticsPresetsCombo.SelectedItem is not AnalyticsPresetListItemDto picked)
        {
            return;
        }

        if (picked.PresetId <= 0)
        {
            _selectedAnalyticsPresetId = null;
            return;
        }

        _selectedAnalyticsPresetId = picked.PresetId;
        var (preset, err) = await _data.GetAnalyticsPresetAsync(picked.PresetId);
        if (err is not null || preset is null)
        {
            MessageBox.Show(this, err ?? "Preset could not be loaded.", "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ApplyPresetToAnalyticsUi(preset);
        await RefreshAnalyticsAsync();
    }

    private void ApplyPresetToAnalyticsUi(AnalyticsPresetDto preset)
    {
        // Apply filter state (dates + any structured fields we currently expose in the UI).
        var f = preset.Query?.Filter ?? new AnalyticsFilterDto();

        if (f.FromUtc.HasValue)
        {
            AnalyticsFromDate.SelectedDate = HsmsDeploymentTimeZone.UtcToDeployment(f.FromUtc.Value).Date;
        }
        if (f.ToUtc.HasValue)
        {
            AnalyticsToDate.SelectedDate = HsmsDeploymentTimeZone.UtcToDeployment(f.ToUtc.Value).Date;
        }

        if (!string.IsNullOrWhiteSpace(f.OperatorName))
        {
            // Admin can choose an individual user; staff is always constrained server-side anyway.
            AnalyticsScopeCombo.SelectedIndex = 1;
            AnalyticsUserCombo.SelectedItem = f.OperatorName;
        }
        else
        {
            AnalyticsScopeCombo.SelectedIndex = 0;
        }

        // Structured filters in the panel
        if (f.SterilizerId.HasValue)
        {
            var match = _analyticsSterilizerLookup.FirstOrDefault(x => x.SterilizerId == f.SterilizerId.Value);
            if (match is not null)
            {
                AnalyticsFilterSterilizerCombo.SelectedItem = $"{match.SterilizerNo} (#{match.SterilizerId})";
            }
        }
        else
        {
            AnalyticsFilterSterilizerCombo.SelectedIndex = 0;
        }

        if (!string.IsNullOrWhiteSpace(f.LoadStatus))
        {
            AnalyticsFilterStatusCombo.SelectedItem = LoadRecordCycleStatuses.Normalize(f.LoadStatus) ?? "(All)";
        }
        else
        {
            AnalyticsFilterStatusCombo.SelectedIndex = 0;
        }

        if (f.Implants is true) AnalyticsFilterImplantsCombo.SelectedIndex = 1;
        else if (f.Implants is false) AnalyticsFilterImplantsCombo.SelectedIndex = 2;
        else AnalyticsFilterImplantsCombo.SelectedIndex = 0;

        if (!string.IsNullOrWhiteSpace(f.BiResult))
        {
            AnalyticsFilterBiResultCombo.SelectedItem = f.BiResult.Trim();
        }
        else
        {
            AnalyticsFilterBiResultCombo.SelectedIndex = 0;
        }

        AnalyticsFilterSearchTextBox.Text = f.GlobalSearch ?? "";
    }

    private AnalyticsPresetUpsertDto BuildPresetFromAnalyticsUi(string name, bool setAsDefault)
    {
        var fromLocal = (AnalyticsFromDate.SelectedDate ?? HsmsDeploymentTimeZone.NowInDeploymentZone().Date).Date;
        var toLocal = (AnalyticsToDate.SelectedDate ?? fromLocal).Date;
        var fromUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified));
        var toUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(toLocal.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified));

        var f = BuildCurrentAnalyticsBaseFilter();
        f.FromUtc = fromUtc;
        f.ToUtc = toUtc;

        return new AnalyticsPresetUpsertDto
        {
            Name = name,
            SetAsDefault = setAsDefault,
            Query = new AnalyticsDashboardQueryDto { Filter = f }
        };
    }

    private async Task SaveAnalyticsPresetAsync()
    {
        var name = Microsoft.VisualBasic.Interaction.InputBox("Preset name:", "HSMS — Save preset", "My preset");
        if (string.IsNullOrWhiteSpace(name)) return;

        var setDefault = MessageBox.Show(this, "Set this preset as default?", "HSMS — Analytics", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

        var payload = BuildPresetFromAnalyticsUi(name.Trim(), setDefault);
        var (preset, err) = await _data.UpsertAnalyticsPresetAsync(null, payload);
        if (err is not null || preset is null)
        {
            MessageBox.Show(this, err ?? "Could not save preset.", "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await EnsureAnalyticsPresetsLoadedAsync();
        AnalyticsPresetsCombo.SelectedItem = _analyticsPresets.FirstOrDefault(x => x.PresetId == preset.PresetId);
        SetStatus($"Saved analytics preset — {preset.Name}.");
    }

    private async Task RenameAnalyticsPresetAsync()
    {
        if (_selectedAnalyticsPresetId is not int id)
        {
            MessageBox.Show(this, "Pick a preset first.", "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (cur, err) = await _data.GetAnalyticsPresetAsync(id);
        if (err is not null || cur is null)
        {
            MessageBox.Show(this, err ?? "Preset could not be loaded.", "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = Microsoft.VisualBasic.Interaction.InputBox("New preset name:", "HSMS — Rename preset", cur.Name);
        if (string.IsNullOrWhiteSpace(name)) return;

        var payload = new AnalyticsPresetUpsertDto
        {
            Name = name.Trim(),
            SetAsDefault = cur.IsDefault,
            Query = cur.Query,
            ChartPreferences = cur.ChartPreferences,
            Breakdowns = cur.Breakdowns
        };
        var (saved, saveErr) = await _data.UpsertAnalyticsPresetAsync(id, payload);
        if (saveErr is not null || saved is null)
        {
            MessageBox.Show(this, saveErr ?? "Could not rename preset.", "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await EnsureAnalyticsPresetsLoadedAsync();
        AnalyticsPresetsCombo.SelectedItem = _analyticsPresets.FirstOrDefault(x => x.PresetId == saved.PresetId);
        SetStatus($"Renamed preset — {saved.Name}.");
    }

    private async Task DeleteAnalyticsPresetAsync()
    {
        if (_selectedAnalyticsPresetId is not int id)
        {
            MessageBox.Show(this, "Pick a preset first.", "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(this, "Delete this preset?", "HSMS — Analytics", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        var err = await _data.DeleteAnalyticsPresetAsync(id);
        if (err is not null)
        {
            MessageBox.Show(this, err, "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _selectedAnalyticsPresetId = null;
        await EnsureAnalyticsPresetsLoadedAsync();
        SetStatus("Deleted preset.");
    }

    private async Task SetDefaultAnalyticsPresetAsync()
    {
        if (_selectedAnalyticsPresetId is not int id)
        {
            MessageBox.Show(this, "Pick a preset first.", "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var err = await _data.SetDefaultAnalyticsPresetAsync(id);
        if (err is not null)
        {
            MessageBox.Show(this, err, "HSMS — Analytics", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await EnsureAnalyticsPresetsLoadedAsync();
        SetStatus("Default preset updated.");
    }

    private void ApplyAnalyticsScopeUi()
    {
        if (AnalyticsScopePanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var scope = (AnalyticsScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var individual = scope.Contains("Individual", StringComparison.OrdinalIgnoreCase);
        AnalyticsUserPanel.Visibility = individual ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task EnsureAnalyticsUserListAsync()
    {
        var isAdmin = string.Equals(_session.Role, "Admin", StringComparison.OrdinalIgnoreCase);
        if (!isAdmin || AnalyticsScopePanel.Visibility != Visibility.Visible)
        {
            return;
        }

        var scope = (AnalyticsScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
        var individual = scope.Contains("Individual", StringComparison.OrdinalIgnoreCase);
        if (!individual)
        {
            return;
        }

        // If we already have a list, keep it.
        if (AnalyticsUserCombo.Items.Count > 1)
        {
            return;
        }

        // Build from current overall results first (fast), then fall back to querying overall.
        var users = _analyticsOperators.Select(x => x.OperatorName).Distinct().OrderBy(x => x).ToList();
        if (users.Count == 0)
        {
            var fromLocal = (AnalyticsFromDate.SelectedDate ?? HsmsDeploymentTimeZone.NowInDeploymentZone().Date).Date;
            var toLocal = (AnalyticsToDate.SelectedDate ?? fromLocal).Date;
            if (toLocal < fromLocal)
            {
                (fromLocal, toLocal) = (toLocal, fromLocal);
            }

            var fromUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified));
            var toUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(toLocal.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified));
            var (overall, overallErr) = await _data.GetSterilizationAnalyticsAsync(fromUtc, toUtc, operatorName: null);
            if (overallErr is null && overall is not null)
            {
                users = overall.ByOperator.Select(x => x.OperatorName).Distinct().OrderBy(x => x).ToList();
            }
        }

        var selectedBefore = AnalyticsUserCombo.SelectedItem?.ToString();
        _suppressAnalyticsUserSelectionChanged = true;
        try
        {
        AnalyticsUserCombo.Items.Clear();
        AnalyticsUserCombo.Items.Add("(pick user)");
        foreach (var u in users)
        {
            AnalyticsUserCombo.Items.Add(u);
        }
        if (!string.IsNullOrWhiteSpace(selectedBefore) && !selectedBefore.StartsWith("(") && users.Contains(selectedBefore))
        {
            AnalyticsUserCombo.SelectedItem = selectedBefore;
        }
        else
        {
            AnalyticsUserCombo.SelectedIndex = 0; // "(pick user)"
        }
        }
        finally
        {
            _suppressAnalyticsUserSelectionChanged = false;
        }
    }

    private async Task RefreshAnalyticsAsync()
    {
        var fromLocal = (AnalyticsFromDate.SelectedDate ?? HsmsDeploymentTimeZone.NowInDeploymentZone().Date).Date;
        var toLocal = (AnalyticsToDate.SelectedDate ?? fromLocal).Date;
        if (toLocal < fromLocal)
        {
            (fromLocal, toLocal) = (toLocal, fromLocal);
        }

        var fromUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(fromLocal, DateTimeKind.Unspecified));
        // inclusive end-of-day
        var toUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(toLocal.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified));

        string? operatorFilter = null;
        var isAdmin = string.Equals(_session.Role, "Admin", StringComparison.OrdinalIgnoreCase);
        if (!isAdmin)
        {
            operatorFilter = GetSignedInDisplayName();
        }
        else
        {
            var scope = (AnalyticsScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var individual = scope.Contains("Individual", StringComparison.OrdinalIgnoreCase);
            if (individual)
            {
                var picked = AnalyticsUserCombo.SelectedItem?.ToString();
                if (string.IsNullOrWhiteSpace(picked) || picked.StartsWith("("))
                {
                    SetStatus("Pick a user first, then press Refresh.");
                    return;
                }

                operatorFilter = picked;
            }
        }

        var spanDays = (int)(toLocal - fromLocal).TotalDays + 1;
        if (spanDays < 1)
        {
            spanDays = 1;
        }

        var prevPeriodEndLocal = fromLocal.AddDays(-1);
        var prevPeriodStartLocal = prevPeriodEndLocal.AddDays(-(spanDays - 1));
        var compareFromUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(
            DateTime.SpecifyKind(prevPeriodStartLocal, DateTimeKind.Unspecified));
        var compareToUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(
            DateTime.SpecifyKind(prevPeriodEndLocal.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified));

        var filter = new AnalyticsFilterDto
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            OperatorName = operatorFilter
        };

        if (AnalyticsFilterSterilizerCombo.SelectedIndex > 0)
        {
            // item text ends with "(#id)"
            var text = AnalyticsFilterSterilizerCombo.SelectedItem?.ToString() ?? "";
            var hash = text.LastIndexOf("#", StringComparison.Ordinal);
            if (hash >= 0 && int.TryParse(text[(hash + 1)..].TrimEnd(')', ' '), out var sid))
            {
                filter.SterilizerId = sid;
            }
        }
        if (AnalyticsFilterStatusCombo.SelectedIndex > 0)
        {
            filter.LoadStatus = AnalyticsFilterStatusCombo.SelectedItem?.ToString();
        }
        if (AnalyticsFilterImplantsCombo.SelectedIndex == 1) filter.Implants = true;
        if (AnalyticsFilterImplantsCombo.SelectedIndex == 2) filter.Implants = false;
        if (AnalyticsFilterBiResultCombo.SelectedIndex > 0)
        {
            filter.BiResult = AnalyticsFilterBiResultCombo.SelectedItem?.ToString();
        }
        if (!string.IsNullOrWhiteSpace(AnalyticsFilterSearchTextBox.Text))
        {
            filter.GlobalSearch = AnalyticsFilterSearchTextBox.Text.Trim();
        }

        _analyticsAppliedFilter = filter;

        var v2 = new AnalyticsDashboardQueryDto
        {
            Filter = filter,
            CompareFromUtc = compareFromUtc,
            CompareToUtc = compareToUtc
        };

        if (spanDays >= 370)
        {
            if (MessageBox.Show(
                    this,
                    $"You selected {spanDays:N0} days.\n\nLarge ranges may be slow and will be summarized (Top-N + caps).\nContinue?",
                    "HSMS — Analytics",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var (a, err) = await _data.GetSterilizationAnalyticsV2Async(v2, default);
        if (err is not null || a is null)
        {
            MessageBox.Show(this, err ?? "Could not load analytics.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _analyticsLastResult = a;
        _analyticsLastFromLocal = fromLocal;
        _analyticsLastToLocal = toLocal;

        AnalyticsHeaderRangeText.Text = $"{fromLocal:yyyy-MM-dd} to {toLocal:yyyy-MM-dd}" + (string.IsNullOrWhiteSpace(operatorFilter) ? "" : $" • Operator: {operatorFilter}");
        AnalyticsPlainRange.Text = AnalyticsHeaderRangeText.Text;

        AnalyticsTotalText.Text = a.TotalLoads.ToString("N0", CultureInfo.InvariantCulture);
        AnalyticsDraftText.Text = a.DraftLoads.ToString("N0", CultureInfo.InvariantCulture);
        AnalyticsCompletedText.Text = a.CompletedLoads.ToString("N0", CultureInfo.InvariantCulture);
        AnalyticsVoidedText.Text = a.VoidedLoads.ToString("N0", CultureInfo.InvariantCulture);
        AnalyticsPcsText.Text = a.TotalPcs.ToString("N0", CultureInfo.InvariantCulture);
        AnalyticsQtyText.Text = a.TotalQty.ToString("N0", CultureInfo.InvariantCulture);

        AnalyticsPlainTotalLoads.Text = a.TotalLoads.ToString("N0", CultureInfo.InvariantCulture);
        AnalyticsPlainPcsQty.Text = $"{a.TotalPcs:N0} / {a.TotalQty:N0}";
        AnalyticsPlainCompleted.Text = a.CompletedLoads.ToString("N0", CultureInfo.InvariantCulture);
        AnalyticsPlainDraft.Text = a.DraftLoads.ToString("N0", CultureInfo.InvariantCulture);
        AnalyticsPlainVoided.Text = a.VoidedLoads.ToString("N0", CultureInfo.InvariantCulture);

        _analyticsOperators.Clear();
        foreach (var r in a.ByOperator)
        {
            _analyticsOperators.Add(r);
        }

        _analyticsSterilizers.Clear();
        foreach (var r in a.BySterilizer)
        {
            _analyticsSterilizers.Add(r);
        }

        ApplyAnalyticsChartsDashboard(a);
        ApplyAnalyticsSupplementTexts(a);
        ApplyAnalyticsBreakdownView();
        await SafeRunAsync(RefreshBiAnalyticsAsync);

        _ = SafeRunAsync(() => _data.AppendAnalyticsAuditAsync(new AnalyticsAuditEventDto
        {
            Action = HSMS.Application.Audit.AuditActions.AnalyticsRefresh,
            Format = null,
            ReportType = "dashboard",
            Filter = filter,
            ClientMachine = Environment.MachineName,
            Notes = $"spanDays={spanDays}"
        }));

        _analyticsPlainOperators.Clear();
        var opTop = (a.ByOperator ?? [])
            .OrderByDescending(x => x.Loads)
            .ThenBy(x => x.OperatorName)
            .Take(10)
            .ToList();
        for (var i = 0; i < opTop.Count; i++)
        {
            var r = opTop[i];
            _analyticsPlainOperators.Add(new AnalyticsPlainRow
            {
                Rank = i + 1,
                Name = r.OperatorName,
                Loads = r.Loads,
                Pcs = r.Pcs,
                Qty = r.Qty
            });
        }

        _analyticsPlainSterilizers.Clear();
        var stTop = (a.BySterilizer ?? [])
            .OrderByDescending(x => x.Loads)
            .ThenBy(x => x.SterilizerNo)
            .Take(10)
            .ToList();
        for (var i = 0; i < stTop.Count; i++)
        {
            var r = stTop[i];
            _analyticsPlainSterilizers.Add(new AnalyticsPlainRow
            {
                Rank = i + 1,
                Name = r.SterilizerNo,
                Loads = r.Loads,
                Pcs = r.Pcs,
                Qty = r.Qty
            });
        }

        // Admin: populate user picker from overall results, even when viewing an individual.
        if (isAdmin && AnalyticsScopePanel.Visibility == Visibility.Visible)
        {
            var scope = (AnalyticsScopeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
            var individual = scope.Contains("Individual", StringComparison.OrdinalIgnoreCase);
            if (individual)
            {
                var selected = AnalyticsUserCombo.SelectedItem?.ToString();
                // Always compute the user list from overall analytics so the picker isn't limited by the current filter.
                var (overall, overallErr) = await _data.GetSterilizationAnalyticsAsync(fromUtc, toUtc, operatorName: null);
                var source = overallErr is null && overall is not null ? overall.ByOperator : a.ByOperator;
                var users = (source ?? []).Select(x => x.OperatorName).Distinct().OrderBy(x => x).ToList();
                _suppressAnalyticsUserSelectionChanged = true;
                try
                {
                    AnalyticsUserCombo.Items.Clear();
                    AnalyticsUserCombo.Items.Add("(pick user)");
                    foreach (var u in users)
                    {
                        AnalyticsUserCombo.Items.Add(u);
                    }
                    if (!string.IsNullOrWhiteSpace(selected) && users.Contains(selected))
                    {
                        AnalyticsUserCombo.SelectedItem = selected;
                    }
                    else if (AnalyticsUserCombo.Items.Count > 1)
                    {
                        AnalyticsUserCombo.SelectedIndex = 1;
                    }
                }
                finally
                {
                    _suppressAnalyticsUserSelectionChanged = false;
                }
            }
        }

        SetStatus($"Analytics — {fromLocal:yyyy-MM-dd} to {toLocal:yyyy-MM-dd}.");
    }

    private void ApplyAnalyticsChartsDashboard(SterilizationAnalyticsDto a)
    {
        var axisLabelPaint = new SolidColorPaint(new SKColor(0x6B, 0x72, 0x80));
        var gridLinePaint = new SolidColorPaint(new SKColor(0xE5, 0xE7, 0xEB));

        // Top Operators (grouped columns)
        var opTop = (a.ByOperator ?? [])
            .OrderByDescending(x => x.Pcs)
            .ThenByDescending(x => x.Loads)
            .ThenBy(x => x.OperatorName)
            .Take(8)
            .ToList();

        var opLabels = opTop.Select(x => x.OperatorName).ToArray();
        _chartOperatorLabels = opLabels;
        AnalyticsTopOperatorsChart.Series = new ISeries[]
        {
            new ColumnSeries<int>
            {
                Name = "Loads",
                Values = opTop.Select(x => x.Loads).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0xF5, 0x9E, 0x0B)), // orange
                Stroke = null,
                MaxBarWidth = 22
            },
            new ColumnSeries<int>
            {
                Name = "Pcs",
                Values = opTop.Select(x => x.Pcs).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0x0D, 0x94, 0x88)), // teal
                Stroke = null,
                MaxBarWidth = 22
            },
            new ColumnSeries<int>
            {
                Name = "Qty",
                Values = opTop.Select(x => x.Qty).ToArray(),
                Fill = new SolidColorPaint(new SKColor(0x0E, 0x74, 0x9A)),
                Stroke = null,
                MaxBarWidth = 22
            }
        };
        AnalyticsTopOperatorsChart.XAxes = new Axis[]
        {
            new Axis
            {
                Labels = opLabels,
                LabelsRotation = 0,
                TextSize = 12,
                LabelsPaint = axisLabelPaint,
                SeparatorsPaint = gridLinePaint
            }
        };
        AnalyticsTopOperatorsChart.YAxes = new Axis[]
        {
            new Axis
            {
                Labeler = v => v.ToString("0", CultureInfo.InvariantCulture),
                TextSize = 12,
                LabelsPaint = axisLabelPaint,
                SeparatorsPaint = gridLinePaint
            }
        };
        AnalyticsTopOperatorsChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom;

        // Load Distribution (pie)
        AnalyticsLoadDistributionSubtitle.Text = $"Status breakdown (Total: {a.TotalLoads:N0})";
        AnalyticsLoadDistributionPie.Series = new ISeries[]
        {
            new PieSeries<int>
            {
                Name = "Draft",
                Values = new[] { a.DraftLoads },
                Fill = new SolidColorPaint(new SKColor(0x0D,0x94,0x88)),
                DataLabelsSize = 12,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Outer,
                DataLabelsPaint = new SolidColorPaint(new SKColor(0x6B, 0x72, 0x80)),
                DataLabelsFormatter = p => $"Draft: {p.Model:0}"
            },
            new PieSeries<int>
            {
                Name = "Completed",
                Values = new[] { a.CompletedLoads },
                Fill = new SolidColorPaint(new SKColor(0xF9,0x73,0x16)),
                DataLabelsSize = 12,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Outer,
                DataLabelsPaint = new SolidColorPaint(new SKColor(0x6B, 0x72, 0x80)),
                DataLabelsFormatter = p => $"Completed: {p.Model:0}"
            },
            new PieSeries<int>
            {
                Name = "Voided",
                Values = new[] { a.VoidedLoads },
                Fill = new SolidColorPaint(new SKColor(0x0F,0x76,0xA0)),
                DataLabelsSize = 12,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Outer,
                DataLabelsPaint = new SolidColorPaint(new SKColor(0x6B, 0x72, 0x80)),
                DataLabelsFormatter = p => $"Voided: {p.Model:0}"
            }
        };
        AnalyticsLoadDistributionPie.LegendPosition = LiveChartsCore.Measure.LegendPosition.Hidden;
        AnalyticsLoadDistributionSubtitle.Text = $"Status breakdown (Total: {a.TotalLoads:N0})";

        // Top Sterilizers Performance (horizontal grouped rows: Loads/Pcs/Qty)
        var sterTop = (a.BySterilizer ?? [])
            .OrderByDescending(x => x.Qty)
            .ThenByDescending(x => x.Pcs)
            .ThenByDescending(x => x.Loads)
            .ThenBy(x => x.SterilizerNo)
            .Take(10)
            .ToList();

        var sterLabels = sterTop.Select(x => x.SterilizerNo).ToArray();
        _chartSterilizerLabels = sterLabels;
        _chartSterilizerIds = sterTop.Select(x => x.SterilizerId).ToArray();
        AnalyticsTopSterilizersChart.Series = new ISeries[]
        {
            new RowSeries<int> { Name = "Loads", Values = sterTop.Select(x => x.Loads).ToArray(), Fill = new SolidColorPaint(new SKColor(0xF5,0x9E,0x0B)), MaxBarWidth = 18 },
            new RowSeries<int> { Name = "Pcs", Values = sterTop.Select(x => x.Pcs).ToArray(), Fill = new SolidColorPaint(new SKColor(0x0D,0x94,0x88)), MaxBarWidth = 18 },
            new RowSeries<int> { Name = "Qty", Values = sterTop.Select(x => x.Qty).ToArray(), Fill = new SolidColorPaint(new SKColor(0x0E,0x74,0x9A)), MaxBarWidth = 18 }
        };
        AnalyticsTopSterilizersChart.YAxes = new Axis[]
        {
            new Axis { Labels = sterLabels, TextSize = 11, LabelsPaint = axisLabelPaint, SeparatorsPaint = gridLinePaint }
        };
        AnalyticsTopSterilizersChart.XAxes = new Axis[]
        {
            new Axis { Labeler = v => v.ToString("0", CultureInfo.InvariantCulture), TextSize = 11, LabelsPaint = axisLabelPaint, SeparatorsPaint = gridLinePaint }
        };
        AnalyticsTopSterilizersChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom;

        // Daily trend (ByDay → local calendar labels)
        var days = (a.ByDay ?? [])
            .OrderBy(x => x.DayUtc)
            .ToList();
        var dayLabels = days.ConvertAll(x => HsmsDeploymentTimeZone.FormatInDeploymentZone(x.DayUtc, "MM-dd", CultureInfo.InvariantCulture)).ToArray();
        _chartDayPoints = days.ToArray();
        var loads = days.Select(x => x.Loads).ToArray();
        var pcs = days.Select(x => x.Pcs).ToArray();
        var qty = days.Select(x => x.Qty).ToArray();
        var ma7 = MovingAverage(loads, 7);

        AnalyticsDailyTrendChart.Series =
        [
            new LineSeries<int>
            {
                Name = "Loads",
                Values = loads,
                GeometrySize = 6,
                Stroke = new SolidColorPaint(new SKColor(0x0E, 0xA5, 0xE9), 3),
                Fill = null
            },
            new LineSeries<double>
            {
                Name = "Loads (7d MA)",
                Values = ma7,
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new SKColor(0x03, 0x7F, 0xC5), 2),
                Fill = null
            },
            new LineSeries<int>
            {
                Name = "Pcs",
                Values = pcs,
                GeometrySize = 6,
                Stroke = new SolidColorPaint(new SKColor(0x8B, 0x5C, 0xF6), 3),
                Fill = null
            },
            new LineSeries<int>
            {
                Name = "Qty",
                Values = qty,
                GeometrySize = 6,
                Stroke = new SolidColorPaint(new SKColor(0x14, 0xB8, 0xA6), 3),
                Fill = null
            }
        ];
        AnalyticsDailyTrendChart.XAxes =
        [
            new Axis
            {
                Labels = dayLabels.Length > 0 ? dayLabels : ["—"],
                LabelsRotation = dayLabels.Length > 14 ? -40 : -15,
                TextSize = 10,
                LabelsPaint = axisLabelPaint,
                SeparatorsPaint = gridLinePaint
            }
        ];
        AnalyticsDailyTrendChart.YAxes =
        [
            new Axis { Labeler = v => v.ToString("0", CultureInfo.InvariantCulture), TextSize = 11, LabelsPaint = axisLabelPaint, SeparatorsPaint = gridLinePaint }
        ];
        AnalyticsDailyTrendChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom;

        // Top operators snapshot (not a time axis)
        var top3 = (a.ByOperator ?? [])
            .OrderByDescending(x => x.Qty)
            .ThenBy(x => x.OperatorName)
            .Take(3)
            .ToList();
        var top3Labels = top3.Count > 0
            ? top3.ConvertAll(x => x.OperatorName).ToArray()
            : new[] { "—" };
        _chartOperatorQtySnapshotLabels = top3Labels;
        AnalyticsOperatorSnapshotChart.Series =
        [
            new LineSeries<int>
            {
                Name = "Qty",
                Values = top3.Count > 0 ? top3.ConvertAll(x => x.Qty).ToArray() : [0],
                GeometrySize = 10,
                Stroke = new SolidColorPaint(new SKColor(0xF9,0x73,0x16), 3),
                Fill = null
            },
            new LineSeries<int>
            {
                Name = "Loads",
                Values = top3.Count > 0 ? top3.ConvertAll(x => x.Loads).ToArray() : [0],
                GeometrySize = 10,
                Stroke = new SolidColorPaint(new SKColor(0x0D,0x94,0x88), 3),
                Fill = null
            }
        ];
        AnalyticsOperatorSnapshotChart.XAxes =
        [
            new Axis { Labels = top3Labels, TextSize = 11, LabelsPaint = axisLabelPaint, SeparatorsPaint = gridLinePaint }
        ];
        AnalyticsOperatorSnapshotChart.YAxes =
        [
            new Axis { Labeler = v => v.ToString("0", CultureInfo.InvariantCulture), TextSize = 11, LabelsPaint = axisLabelPaint, SeparatorsPaint = gridLinePaint }
        ];
        AnalyticsOperatorSnapshotChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom;
    }

    private static double[] MovingAverage(int[] values, int window)
    {
        if (values.Length == 0) return [];
        if (window <= 1) return values.Select(x => (double)x).ToArray();
        var w = Math.Min(window, values.Length);
        var result = new double[values.Length];
        double sum = 0;
        for (var i = 0; i < values.Length; i++)
        {
            sum += values[i];
            if (i >= w) sum -= values[i - w];
            var denom = Math.Min(i + 1, w);
            result[i] = sum / denom;
        }
        return result;
    }

    private void HookAnalyticsChartPointDrilldown()
    {
        AnalyticsTopOperatorsChart.DataPointerDown += AnalyticsCharts_OnOperatorsDataPointerDown;
        AnalyticsTopSterilizersChart.DataPointerDown += AnalyticsCharts_OnSterilizersDataPointerDown;
        AnalyticsDailyTrendChart.DataPointerDown += AnalyticsCharts_OnDailyTrendDataPointerDown;
        AnalyticsOperatorSnapshotChart.DataPointerDown += AnalyticsCharts_OnOperatorSnapshotDataPointerDown;
        AnalyticsLoadDistributionPie.DataPointerDown += AnalyticsCharts_OnPieDataPointerDown;
    }

    private static void InvokeAnalyticsChartDrill(Action drill)
    {
        var d = global::System.Windows.Application.Current?.Dispatcher;
        if (d is null)
        {
            drill();
            return;
        }

        _ = d.BeginInvoke(drill, DispatcherPriority.Input);
    }

    private static ChartPoint? PickAnalyticsChartPoint(IEnumerable<ChartPoint> points) =>
        points.FirstOrDefault(p => !p.IsEmpty);

    private void AnalyticsCharts_OnOperatorsDataPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
    {
        if (PickAnalyticsChartPoint(points) is not { } point)
        {
            return;
        }

        InvokeAnalyticsChartDrill(() =>
        {
            var i = point.Index;
            if ((uint)i < (uint)_chartOperatorLabels.Length)
            {
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = WithOperator(BuildCurrentAnalyticsBaseFilter(), _chartOperatorLabels[i]),
                    Context = new AnalyticsDrilldownContextDto { Title = $"Operator = {_chartOperatorLabels[i]}" }
                });
            }
        });
    }

    private void AnalyticsCharts_OnSterilizersDataPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
    {
        if (PickAnalyticsChartPoint(points) is not { } point)
        {
            return;
        }

        InvokeAnalyticsChartDrill(() =>
        {
            var i = point.Index;
            if ((uint)i < (uint)_chartSterilizerLabels.Length)
            {
                var id = (uint)i < (uint)_chartSterilizerIds.Length ? _chartSterilizerIds[i] : 0;
                var f = BuildCurrentAnalyticsBaseFilter();
                if (id > 0) f.SterilizerId = id;
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = $"Sterilizer = {_chartSterilizerLabels[i]}" }
                });
            }
        });
    }

    private void AnalyticsCharts_OnDailyTrendDataPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
    {
        if (PickAnalyticsChartPoint(points) is not { } point)
        {
            return;
        }

        InvokeAnalyticsChartDrill(() =>
        {
            var i = point.Index;
            if ((uint)i >= (uint)_chartDayPoints.Length || _chartDayPoints.Length == 0)
            {
                return;
            }

            var dayUtc = HsmsDeploymentTimeZone.AsUtcKind(_chartDayPoints[i].DayUtc).Date;
            var localDay = HsmsDeploymentTimeZone.UtcToDeployment(dayUtc).Date;
            var startLocal = localDay.Date;
            var endLocal = localDay.Date;
            var fromUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(startLocal, DateTimeKind.Unspecified));
            var toUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(endLocal.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified));
            var f = BuildCurrentAnalyticsBaseFilter();
            f.FromUtc = fromUtc;
            f.ToUtc = toUtc;
            OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
            {
                Filter = f,
                Context = new AnalyticsDrilldownContextDto { Title = $"Day = {startLocal:yyyy-MM-dd}" }
            });
        });
    }

    private void AnalyticsCharts_OnOperatorSnapshotDataPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
    {
        if (PickAnalyticsChartPoint(points) is not { } point)
        {
            return;
        }

        InvokeAnalyticsChartDrill(() =>
        {
            var i = point.Index;
            if ((uint)i >= (uint)_chartOperatorQtySnapshotLabels.Length)
            {
                return;
            }

            var name = _chartOperatorQtySnapshotLabels[i]?.Trim();
            if (string.IsNullOrEmpty(name) || string.Equals(name, "—", StringComparison.Ordinal))
            {
                return;
            }

            OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
            {
                Filter = WithOperator(BuildCurrentAnalyticsBaseFilter(), name),
                Context = new AnalyticsDrilldownContextDto { Title = $"Operator = {name}" }
            });
        });
    }

    private void AnalyticsCharts_OnPieDataPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
    {
        if (PickAnalyticsChartPoint(points) is not { } point)
        {
            return;
        }

        InvokeAnalyticsChartDrill(() =>
        {
            var seriesTitle = point.Context.Series?.Name;
            var normalized = LoadRecordCycleStatuses.Normalize(seriesTitle ?? "");
            if (!string.IsNullOrEmpty(normalized))
            {
                var f = BuildCurrentAnalyticsBaseFilter();
                f.LoadStatus = normalized;
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = $"Status = {normalized}" }
                });
                return;
            }

            var i = point.Index;
            if ((uint)i < (uint)AnalyticsPieStatusSearch.Length)
            {
                var status = AnalyticsPieStatusSearch[i];
                var f = BuildCurrentAnalyticsBaseFilter();
                f.LoadStatus = status;
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = $"Status = {status}" }
                });
            }
        });
    }

    private AnalyticsFilterDto BuildCurrentAnalyticsBaseFilter()
    {
        // Always use the last applied structured filter so drill-down/export/presets preserve state.
        return new AnalyticsFilterDto
        {
            FromUtc = _analyticsAppliedFilter.FromUtc,
            ToUtc = _analyticsAppliedFilter.ToUtc,
            SterilizerId = _analyticsAppliedFilter.SterilizerId,
            SterilizationType = _analyticsAppliedFilter.SterilizationType,
            LoadStatus = _analyticsAppliedFilter.LoadStatus,
            Implants = _analyticsAppliedFilter.Implants,
            BiLotNo = _analyticsAppliedFilter.BiLotNo,
            BiResult = _analyticsAppliedFilter.BiResult,
            Department = _analyticsAppliedFilter.Department,
            DoctorRoomId = _analyticsAppliedFilter.DoctorRoomId,
            CycleProgram = _analyticsAppliedFilter.CycleProgram,
            OperatorName = _analyticsAppliedFilter.OperatorName,
            QaStatus = _analyticsAppliedFilter.QaStatus,
            GlobalSearch = _analyticsAppliedFilter.GlobalSearch
        };
    }

    private static AnalyticsFilterDto WithOperator(AnalyticsFilterDto f, string operatorName)
    {
        f.OperatorName = operatorName;
        return f;
    }

    private void OpenAnalyticsDrilldown(AnalyticsDrilldownRequestDto request)
    {
        var win = new AnalyticsDrilldownWindow(_data, request) { Owner = this };
        win.Show();
    }

    private void SetupAnalyticsBreakdownUi()
    {
        AnalyticsBreakdownCombo.Items.Clear();
        AnalyticsBreakdownCombo.Items.Add("Sterilization type");
        AnalyticsBreakdownCombo.Items.Add("BI result (load header)");
        AnalyticsBreakdownCombo.Items.Add("Department (item lines)");
        AnalyticsBreakdownCombo.Items.Add("Top items by quantity");
        AnalyticsBreakdownCombo.Items.Add("Doctor / room (header)");
        AnalyticsBreakdownCombo.SelectedIndex = 0;

        AnalyticsBreakdownGrid.Columns.Clear();
        AnalyticsBreakdownGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Key",
            Binding = new Binding(nameof(AnalyticsBreakdownRowDto.Key)),
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        AnalyticsBreakdownGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Loads",
            Binding = new Binding(nameof(AnalyticsBreakdownRowDto.Loads)),
            Width = new DataGridLength(80, DataGridLengthUnitType.Pixel)
        });
        AnalyticsBreakdownGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Pcs",
            Binding = new Binding(nameof(AnalyticsBreakdownRowDto.Pcs)),
            Width = new DataGridLength(72, DataGridLengthUnitType.Pixel)
        });
        AnalyticsBreakdownGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Qty",
            Binding = new Binding(nameof(AnalyticsBreakdownRowDto.Qty)),
            Width = new DataGridLength(72, DataGridLengthUnitType.Pixel)
        });

        AnalyticsPlainBreakdownCombo.Items.Clear();
        foreach (var it in AnalyticsBreakdownCombo.Items)
        {
            AnalyticsPlainBreakdownCombo.Items.Add(it);
        }
        AnalyticsPlainBreakdownCombo.SelectedIndex = AnalyticsBreakdownCombo.SelectedIndex;

        AnalyticsPlainBreakdownGrid.Columns.Clear();
        foreach (var col in AnalyticsBreakdownGrid.Columns)
        {
            if (col is DataGridTextColumn tc)
            {
                AnalyticsPlainBreakdownGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = tc.Header,
                    Binding = tc.Binding,
                    Width = tc.Width
                });
            }
        }
    }

    private void ApplyAnalyticsBreakdownView()
    {
        if (_analyticsLastResult is null)
        {
            AnalyticsBreakdownGrid.ItemsSource = Array.Empty<AnalyticsBreakdownRowDto>();
            AnalyticsPlainBreakdownGrid.ItemsSource = Array.Empty<AnalyticsBreakdownRowDto>();
            return;
        }

        var idx = AnalyticsPlainBreakdownCombo.IsKeyboardFocusWithin
            ? AnalyticsPlainBreakdownCombo.SelectedIndex
            : AnalyticsBreakdownCombo.SelectedIndex;
        if (idx < 0) idx = 0;

        if (AnalyticsBreakdownCombo.SelectedIndex != idx) AnalyticsBreakdownCombo.SelectedIndex = idx;
        if (AnalyticsPlainBreakdownCombo.SelectedIndex != idx) AnalyticsPlainBreakdownCombo.SelectedIndex = idx;

        var list = idx switch
        {
            0 => _analyticsLastResult.BySterilizationType,
            1 => _analyticsLastResult.ByBiResult,
            2 => _analyticsLastResult.ByDepartment,
            3 => _analyticsLastResult.TopItemsByQty,
            4 => _analyticsLastResult.ByDoctorRoom,
            _ => (IReadOnlyList<AnalyticsBreakdownRowDto>)[]
        };

        AnalyticsBreakdownGrid.ItemsSource = list;
        AnalyticsPlainBreakdownGrid.ItemsSource = list;
        if (AnalyticsBreakdownGrid.Columns.Count >= 2 && AnalyticsBreakdownGrid.Columns[1] is DataGridTextColumn lc)
        {
            lc.Header = idx is 2 or 3 ? "Lines" : "Loads";
        }
        if (AnalyticsPlainBreakdownGrid.Columns.Count >= 2 && AnalyticsPlainBreakdownGrid.Columns[1] is DataGridTextColumn plc)
        {
            plc.Header = idx is 2 or 3 ? "Lines" : "Loads";
        }
    }

    private static string FormatAnalyticsDeltaLabel(int cur, int prev)
    {
        var d = cur - prev;
        if (d > 0)
        {
            return "+" + d.ToString(CultureInfo.InvariantCulture);
        }

        if (d < 0)
        {
            return d.ToString(CultureInfo.InvariantCulture);
        }

        return "±0";
    }

    private void ApplyAnalyticsSupplementTexts(SterilizationAnalyticsDto a)
    {
        if (a.ComparePriorPeriod is { } cmp)
        {
            var cmpStartDisplay = HsmsDeploymentTimeZone.FormatInDeploymentZone(cmp.PeriodStartUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var cmpEndDisplay = HsmsDeploymentTimeZone.FormatInDeploymentZone(cmp.PeriodEndUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            AnalyticsCompareText.Text =
                $"Prior period ({cmpStartDisplay} to {cmpEndDisplay} vs current): loads {cmp.TotalLoads} ({FormatAnalyticsDeltaLabel(a.TotalLoads, cmp.TotalLoads)}), " +
                $"completed {cmp.CompletedLoads} ({FormatAnalyticsDeltaLabel(a.CompletedLoads, cmp.CompletedLoads)}), " +
                $"pcs {cmp.TotalPcs:N0} ({FormatAnalyticsDeltaLabel(a.TotalPcs, cmp.TotalPcs)}), " +
                $"qty {cmp.TotalQty:N0} ({FormatAnalyticsDeltaLabel(a.TotalQty, cmp.TotalQty)}).";
            AnalyticsCompareText.Visibility = Visibility.Visible;
        }
        else
        {
            AnalyticsCompareText.Text = "";
            AnalyticsCompareText.Visibility = Visibility.Collapsed;
        }

        if (a.QaTests is { } qa)
        {
            AnalyticsQaSummaryText.Text =
                $"Leak Pass {qa.LeakPass}, Fail {qa.LeakFail} · Bowie-Dick Pass {qa.BowiePass}, Fail {qa.BowieFail}. Pending approval: {qa.PendingApproval}.";
        }
        else
        {
            AnalyticsQaSummaryText.Text = "—";
        }

        if (a.InstrumentChecks is { } ins)
        {
            AnalyticsInstrumentSummaryText.Text =
                $"Total checks {ins.TotalChecks:N0}: witness pending {ins.WitnessPending:N0}, approved {ins.WitnessApproved:N0}. (Not filtered by load operator.)";
        }
        else
        {
            AnalyticsInstrumentSummaryText.Text =
                "Instrument Checks table unavailable for this deployment. Run DDL 016 (instrument checks) if you expect checks here.";
        }

        var plainExtras = new List<string>();
        if (a.ComparePriorPeriod is not null && !string.IsNullOrWhiteSpace(AnalyticsCompareText.Text))
        {
            plainExtras.Add(AnalyticsCompareText.Text);
        }

        if (!string.IsNullOrWhiteSpace(AnalyticsQaSummaryText.Text) && AnalyticsQaSummaryText.Text != "—")
        {
            plainExtras.Add("QA: " + AnalyticsQaSummaryText.Text);
        }

        if (!string.IsNullOrWhiteSpace(AnalyticsInstrumentSummaryText.Text))
        {
            plainExtras.Add("Instruments: " + AnalyticsInstrumentSummaryText.Text);
        }

        if (a.BiLogPaper is { } bPlain)
        {
            plainExtras.Add("BI paper QA: Lot " + bPlain.LotNoCaptured + " / BI in " + bPlain.BiTimeInCaptured + " / BI out "
                            + bPlain.BiTimeOutCaptured + "; both BI times " + bPlain.BiTimesBothCaptured);
        }
        else
        {
            plainExtras.Add("BI paper QA: unavailable (DDL 013 BI log QA columns missing).");
        }

        AnalyticsPlainSupplement.Text = plainExtras.Count > 0 ? string.Join("\n\n", plainExtras) : "";
        ApplyAnalyticsBiPaperPanel(a);
    }

    private async Task RefreshBiAnalyticsAsync()
    {
        var f = BuildCurrentAnalyticsBaseFilter();
        var (bi, err) = await _data.GetBiAnalyticsAsync(f);
        if (err is not null || bi is null)
        {
            AnalyticsBiLogSummaryText.Text = err ?? "Could not load BI analytics.";
            _analyticsLastBiResult = null;
            ApplyBiTrendChart(null);
            HookBiTrendChartDrilldown();
            return;
        }

        _analyticsLastBiResult = bi;
        AnalyticsBiLogSummaryText.Text =
            $"Total BI cycles: {bi.TotalBiCycles:N0}. " +
            $"Passed {bi.Passed:N0}; Failed {bi.Failed:N0}; Pending {bi.Pending:N0}; Missing entries {bi.MissingEntries:N0}. " +
            $"Completeness — Lot# {bi.Completeness.LotNoCaptured:N0}/{bi.Completeness.CyclesInScope:N0}, " +
            $"Result {bi.Completeness.ResultCaptured:N0}/{bi.Completeness.CyclesInScope:N0}, " +
            $"TimeIn {bi.Completeness.TimeInCaptured:N0}/{bi.Completeness.CyclesInScope:N0}, " +
            $"TimeOut {bi.Completeness.TimeOutCaptured:N0}/{bi.Completeness.CyclesInScope:N0}.";

        ApplyBiTrendChart(bi);
        HookBiTrendChartDrilldown();
    }

    private void ApplyBiTrendChart(BiAnalyticsDto? bi)
    {
        var axisLabelPaint = new SolidColorPaint(new SKColor(0x6B, 0x72, 0x80));
        var gridLinePaint = new SolidColorPaint(new SKColor(0xE5, 0xE7, 0xEB));

        var days = (bi?.ByDay ?? []).OrderBy(x => x.DayUtc).ToList();
        _chartBiDayPoints = days.ToArray();

        var dayLabels = days.Count == 0
            ? new[] { "—" }
            : days.ConvertAll(x => HsmsDeploymentTimeZone.FormatInDeploymentZone(x.DayUtc, "MM-dd", CultureInfo.InvariantCulture)).ToArray();

        AnalyticsBiTrendChart.Series =
        [
            new LineSeries<int> { Name = "Pass", Values = days.Count == 0 ? [0] : days.Select(x => x.Pass).ToArray(), GeometrySize = 5, Stroke = new SolidColorPaint(new SKColor(0x10, 0xB9, 0x81), 3), Fill = null },
            new LineSeries<int> { Name = "Fail", Values = days.Count == 0 ? [0] : days.Select(x => x.Fail).ToArray(), GeometrySize = 5, Stroke = new SolidColorPaint(new SKColor(0xEF, 0x44, 0x44), 3), Fill = null },
            new LineSeries<int> { Name = "Pending", Values = days.Count == 0 ? [0] : days.Select(x => x.Pending).ToArray(), GeometrySize = 5, Stroke = new SolidColorPaint(new SKColor(0xF5, 0x9E, 0x0B), 3), Fill = null },
            new LineSeries<int> { Name = "Missing", Values = days.Count == 0 ? [0] : days.Select(x => x.Missing).ToArray(), GeometrySize = 5, Stroke = new SolidColorPaint(new SKColor(0x0E, 0x74, 0x9A), 3), Fill = null }
        ];

        AnalyticsBiTrendChart.XAxes =
        [
            new Axis
            {
                Labels = dayLabels,
                LabelsRotation = dayLabels.Length > 14 ? -40 : -15,
                TextSize = 10,
                LabelsPaint = axisLabelPaint,
                SeparatorsPaint = gridLinePaint
            }
        ];
        AnalyticsBiTrendChart.YAxes =
        [
            new Axis { Labeler = v => v.ToString("0", CultureInfo.InvariantCulture), TextSize = 11, LabelsPaint = axisLabelPaint, SeparatorsPaint = gridLinePaint }
        ];
        AnalyticsBiTrendChart.LegendPosition = LiveChartsCore.Measure.LegendPosition.Bottom;
    }

    private bool _biTrendHooked;
    private void HookBiTrendChartDrilldown()
    {
        if (_biTrendHooked)
        {
            return;
        }

        _biTrendHooked = true;
        AnalyticsBiTrendChart.DataPointerDown += AnalyticsBiTrendChart_OnDataPointerDown;
    }

    private void AnalyticsBiTrendChart_OnDataPointerDown(IChartView chart, IEnumerable<ChartPoint> points)
    {
        if (PickAnalyticsChartPoint(points) is not { } point)
        {
            return;
        }

        InvokeAnalyticsChartDrill(() =>
        {
            var i = point.Index;
            if ((uint)i >= (uint)_chartBiDayPoints.Length || _chartBiDayPoints.Length == 0)
            {
                return;
            }

            var dayUtc = HsmsDeploymentTimeZone.AsUtcKind(_chartBiDayPoints[i].DayUtc).Date;
            var localDay = HsmsDeploymentTimeZone.UtcToDeployment(dayUtc).Date;
            var fromUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(localDay, DateTimeKind.Unspecified));
            var toUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(localDay.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified));

            var f = BuildCurrentAnalyticsBaseFilter();
            f.FromUtc = fromUtc;
            f.ToUtc = toUtc;

            OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
            {
                Filter = f,
                Context = new AnalyticsDrilldownContextDto { Title = $"BI day = {localDay:yyyy-MM-dd}" }
            });
        });
    }

    private static string FormatAnalyticsBiSignBuckets(IReadOnlyList<AnalyticsBreakdownRowDto>? rows)
    {
        if (rows is null || rows.Count == 0)
        {
            return "—";
        }

        return string.Join("; ", rows.Select(r => $"{r.Key}:{r.Loads:N0}"));
    }

    private void ApplyAnalyticsBiPaperPanel(SterilizationAnalyticsDto? a)
    {
        if (a?.BiLogPaper is not { } b)
        {
            var msg =
                "BI log sheet (periodic QA paper form): aggregates are unavailable. Run DDL 013_hsms_bi_log_sheet_qa_form.sql (and related BI scripts) so the database exposes these columns.";
            AnalyticsBiPaperDetailText.Text = msg;
            AnalyticsPlainBiPaperDetailText.Text = msg;
            return;
        }

        var body =
            $"Loads in filter: {b.LoadsInScope:N0}. " +
            $"Lot # recorded {b.LotNoCaptured:N0}; routine daily (paper) marked {b.RoutineDailyMarked:N0}; " +
            $"BI time in {b.BiTimeInCaptured:N0}; BI time out {b.BiTimeOutCaptured:N0}; both BI times {b.BiTimesBothCaptured:N0}; " +
            $"incubator reading checked {b.IncubatorReadingChecked:N0}.\n" +
            $"Processed sample 24m (+/− buckets): {FormatAnalyticsBiSignBuckets(b.ProcessedSample24mSign)}\n" +
            $"Processed sample 24h: {FormatAnalyticsBiSignBuckets(b.ProcessedSample24hSign)}\n" +
            $"Control sample 24m: {FormatAnalyticsBiSignBuckets(b.ControlSample24mSign)}\n" +
            $"Control sample 24h: {FormatAnalyticsBiSignBuckets(b.ControlSample24hSign)}";
        AnalyticsBiPaperDetailText.Text = body;
        AnalyticsPlainBiPaperDetailText.Text = body;
    }

    private void ExportAnalyticsCsv()
    {
        if (_analyticsLastResult is null)
        {
            MessageBox.Show(this, "Refresh analytics first, then export.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"hsms_analytics_{_analyticsLastFromLocal:yyyy-MM-dd}_{_analyticsLastToLocal:yyyy-MM-dd}.csv"
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var csv = BuildAnalyticsCsv(_analyticsLastResult, _analyticsLastFromLocal, _analyticsLastToLocal);
            File.WriteAllText(dlg.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            SetStatus($"Exported analytics CSV — {dlg.FileName}.");
            _ = SafeRunAsync(() => _data.AppendAnalyticsAuditAsync(new AnalyticsAuditEventDto
            {
                Action = HSMS.Application.Audit.AuditActions.AnalyticsExportCsv,
                Format = "CSV",
                ReportType = "analytics",
                Filter = BuildCurrentAnalyticsBaseFilter(),
                ClientMachine = Environment.MachineName
            }));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed:\n{ex.Message}", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private enum AnalyticsPrintMode
    {
        PlainText = 1,
        Charts = 2
    }

    private AnalyticsPrintMode? PromptAnalyticsPrintMode(string actionVerb)
    {
        var result = MessageBox.Show(
            this,
            $"Choose what to {actionVerb}:\n\nYes = Plain text report\nNo = Charts dashboard\nCancel = Back",
            "HSMS — Analytics",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => AnalyticsPrintMode.PlainText,
            MessageBoxResult.No => AnalyticsPrintMode.Charts,
            _ => null
        };
    }

    private async Task PrintAnalyticsAsync()
    {
        if (_analyticsLastResult is null)
        {
            MessageBox.Show(this, "Refresh analytics first, then print.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = PromptAnalyticsPrintMode("print");
        if (mode is null)
        {
            return;
        }

        // Print via WPF paginator so it behaves like Load Records / BI Log Sheet printing.
        var doc = BuildAnalyticsFlowDocument(mode.Value, includeInstitutionalBanner: true);
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true)
        {
            return;
        }

        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "HSMS — Analytics");
        SetStatus("Sent Analytics to printer.");
        _ = SafeRunAsync(() => _data.AppendAnalyticsAuditAsync(new AnalyticsAuditEventDto
        {
            Action = HSMS.Application.Audit.AuditActions.AnalyticsPrint,
            Format = "WPF",
            ReportType = "analytics",
            Filter = BuildCurrentAnalyticsBaseFilter(),
            ClientMachine = Environment.MachineName
        }));
        await Task.CompletedTask;
    }

    private async Task ExportAnalyticsPdfAsync()
    {
        if (_analyticsLastResult is null)
        {
            MessageBox.Show(this, "Refresh analytics first, then export.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = PromptAnalyticsPrintMode("export");
        if (mode is null)
        {
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Analytics (PDF)",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"hsms-analytics-{_analyticsLastFromLocal:yyyyMMdd}-{_analyticsLastToLocal:yyyyMMdd}-{DateTime.Now:yyyyMMdd-HHmm}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        QuestPDF.Settings.License = LicenseType.Community;
        BuildAnalyticsPdf(mode.Value).GeneratePdf(dlg.FileName);
        SetStatus($"Exported analytics to PDF — {dlg.FileName}.");
        _ = SafeRunAsync(() => _data.AppendAnalyticsAuditAsync(new AnalyticsAuditEventDto
        {
            Action = HSMS.Application.Audit.AuditActions.AnalyticsExportPdf,
            Format = "PDF",
            ReportType = "analytics",
            Filter = BuildCurrentAnalyticsBaseFilter(),
            ClientMachine = Environment.MachineName
        }));
        await Task.CompletedTask;
    }

    private async Task ExportAnalyticsXlsxAsync()
    {
        if (_analyticsLastResult is null)
        {
            MessageBox.Show(this, "Refresh analytics first, then export.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_workbookExport is null)
        {
            MessageBox.Show(this, "Excel export service is not initialized.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Analytics (XLSX)",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            FileName = $"hsms-analytics-{_analyticsLastFromLocal:yyyyMMdd}-{_analyticsLastToLocal:yyyyMMdd}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var filter = BuildCurrentAnalyticsBaseFilter();
            var bi = _analyticsLastBiResult;
            await using var fs = File.Open(dlg.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            _workbookExport.WriteWorkbook(wb =>
            {
                AnalyticsXlsxExporter.BuildWorkbook(
                    wb,
                    generatedBy: GetSignedInDisplayName(),
                    generatedAtUtc: DateTime.UtcNow,
                    filter,
                    _analyticsLastResult,
                    bi);
            }, fs);

            SetStatus($"Exported analytics XLSX — {dlg.FileName}.");
            _ = SafeRunAsync(() => _data.AppendAnalyticsAuditAsync(new AnalyticsAuditEventDto
            {
                Action = HSMS.Application.Audit.AuditActions.AnalyticsExportXlsx,
                Format = "XLSX",
                ReportType = "analytics",
                Filter = BuildCurrentAnalyticsBaseFilter(),
                ClientMachine = Environment.MachineName
            }));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed:\n{ex.Message}", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static byte[]? TryRenderElementPngBytes(FrameworkElement el, double maxWidthPx = 1050)
    {
        try
        {
            // Ensure it is fully measured/arranged.
            el.UpdateLayout();

            var w = Math.Max(1.0, el.ActualWidth);
            var h = Math.Max(1.0, el.ActualHeight);

            // Scale up to improve print quality but cap to avoid huge bitmaps.
            var scale = Math.Max(1.0, Math.Min(2.0, maxWidthPx / w));
            var pxW = (int)Math.Ceiling(w * scale);
            var pxH = (int)Math.Ceiling(h * scale);

            var rtb = new RenderTargetBitmap(pxW, pxH, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            rtb.Render(el);

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage? BitmapImageFromPngBytes(byte[]? png)
    {
        if (png is null || png.Length == 0) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = new MemoryStream(png);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch
        {
            return null;
        }
    }

    private FlowDocument BuildAnalyticsFlowDocument(AnalyticsPrintMode mode, bool includeInstitutionalBanner)
    {
        const double pageW = 793;   // A4 width @96dpi
        const double padH = 42;
        var doc = new FlowDocument
        {
            PageWidth = pageW,
            PageHeight = 1122,
            ColumnWidth = pageW - padH - padH,
            PagePadding = new Thickness(padH, 36, padH, 36),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11
        };

        if (includeInstitutionalBanner)
        {
            doc.Blocks.Add(BuildLoadRecordLetterheadBlock());
        }

        var titleText = mode == AnalyticsPrintMode.PlainText ? "ANALYTICS — STATISTICS REPORT" : "ANALYTICS — DASHBOARD (CHARTS)";
        doc.Blocks.Add(new Paragraph(new Run(titleText))
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        });

        doc.Blocks.Add(new Paragraph(new Run(AnalyticsHeaderRangeText.Text ?? ""))
        {
            FontSize = 10,
            Foreground = Brushes.DimGray,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16)
        });

        if (mode == AnalyticsPrintMode.PlainText)
        {
            // Use the already formatted plain text block (includes QA/instruments/BI paper text).
            doc.Blocks.Add(new Paragraph(new Run(AnalyticsPlainSupplement.Text ?? ""))
            {
                FontSize = 9.5,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 0, 0, 12)
            });

            // Render all breakdown views (not just the selected one).
            doc.Blocks.Add(new Paragraph(new Run("Top operators")) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            doc.Blocks.Add(BuildSimpleTableFromRows(
                headers: ["Operator", "Loads", "Pcs", "Qty"],
                rows: (_analyticsPlainOperators ?? []).Select(r => new[] { r.Name, r.Loads.ToString(), r.Pcs.ToString(), r.Qty.ToString() })));

            doc.Blocks.Add(new Paragraph(new Run("Top sterilizers")) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6) });
            doc.Blocks.Add(BuildSimpleTableFromRows(
                headers: ["Sterilizer", "Loads", "Pcs", "Qty"],
                rows: (_analyticsPlainSterilizers ?? []).Select(r => new[] { r.Name, r.Loads.ToString(), r.Pcs.ToString(), r.Qty.ToString() })));

            if (_analyticsLastResult is { } a)
            {
                var sections = new (string Title, string LoadsHeader, IReadOnlyList<AnalyticsBreakdownRowDto> Rows)[]
                {
                    ("Sterilization type", "Loads", a.BySterilizationType),
                    ("BI result (load header)", "Loads", a.ByBiResult),
                    ("Department (item lines)", "Lines", a.ByDepartment),
                    ("Top items by quantity", "Lines", a.TopItemsByQty),
                    ("Doctor / room (header)", "Loads", a.ByDoctorRoom)
                };

                foreach (var (title, loadsHeader, rows) in sections)
                {
                    doc.Blocks.Add(new Paragraph(new Run($"Breakdown — {title}"))
                    {
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 12, 0, 6)
                    });
                    doc.Blocks.Add(BuildSimpleTableFromRows(
                        headers: ["Key", loadsHeader, "Pcs", "Qty"],
                        rows: (rows ?? []).Select(r => new[] { r.Key, r.Loads.ToString(), r.Pcs.ToString(), r.Qty.ToString() })));
                }
            }
        }
        else
        {
            // Charts mode: embed chart bitmaps (each chart on its own block).
            var parts = new (string Title, FrameworkElement El)[]
            {
                ("Top operators", AnalyticsTopOperatorsChart),
                ("Load distribution", AnalyticsLoadDistributionPie),
                ("Top sterilizers", AnalyticsTopSterilizersChart),
                ("Daily activity", AnalyticsDailyTrendChart),
                ("Operator snapshot", AnalyticsOperatorSnapshotChart)
            };

            foreach (var (t, el) in parts)
            {
                var png = TryRenderElementPngBytes(el);
                var img = BitmapImageFromPngBytes(png);
                doc.Blocks.Add(new Paragraph(new Run(t)) { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });

                if (img is null)
                {
                    doc.Blocks.Add(new Paragraph(new Run("(Chart unavailable for printing)")) { Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 12) });
                    continue;
                }

                var image = new System.Windows.Controls.Image
                {
                    Source = img,
                    Stretch = Stretch.Uniform,
                    Width = pageW - padH - padH
                };
                doc.Blocks.Add(new BlockUIContainer(image) { Margin = new Thickness(0, 0, 0, 14) });
            }
        }

        doc.Blocks.Add(new Paragraph(new Run($"Printed: {DateTime.Now:yyyy-MM-dd HH:mm}"))
        {
            FontSize = 9,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 18, 0, 0)
        });

        return doc;
    }

    private static Block BuildSimpleTableFromRows(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var table = new Table { CellSpacing = 0 };
        foreach (var _ in headers)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        }

        var g = new TableRowGroup();
        table.RowGroups.Add(g);

        TableCell HeadCell(string text)
        {
            var p = new Paragraph(new Run(text)) { Margin = new Thickness(0) };
            return new TableCell(p)
            {
                Padding = new Thickness(4),
                Background = Brushes.Gainsboro,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1)
            };
        }

        TableCell Cell(string text)
        {
            var p = new Paragraph(new Run(text ?? "")) { Margin = new Thickness(0) };
            return new TableCell(p)
            {
                Padding = new Thickness(4),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(1)
            };
        }

        var hr = new TableRow();
        foreach (var h in headers)
        {
            hr.Cells.Add(HeadCell(h));
        }
        g.Rows.Add(hr);

        foreach (var r in rows)
        {
            var tr = new TableRow();
            for (var i = 0; i < headers.Count; i++)
            {
                tr.Cells.Add(Cell(i < r.Length ? r[i] : ""));
            }
            g.Rows.Add(tr);
        }

        return table;
    }

    private IDocument BuildAnalyticsPdf(AnalyticsPrintMode mode)
    {
        if (_analyticsLastResult is null)
        {
            return new AnalyticsPdfDocument(
                mode,
                printedAt: DateTime.Now,
                logoPng: TryLoadLogoPngBytes(),
                headerRangeText: "",
                plainSupplementText: "",
                plainTopOperators: [],
                plainTopSterilizers: [],
                breakdownSections: [],
                biPaperDetailText: "",
                charts: []);
        }

        var a = _analyticsLastResult;
        var headerRangeText = AnalyticsHeaderRangeText.Text ?? "";
        var plainSupplementText = AnalyticsPlainSupplement.Text ?? "";
        var biPaperDetailText = (AnalyticsPlainBiPaperDetailText.Text ?? AnalyticsBiPaperDetailText.Text) ?? "";

        var breakdownSections = new List<AnalyticsPdfBreakdownSection>
        {
            new("Sterilization type", "Loads", a.BySterilizationType),
            new("BI result (load header)", "Loads", a.ByBiResult),
            new("Department (item lines)", "Lines", a.ByDepartment),
            new("Top items by quantity", "Lines", a.TopItemsByQty),
            new("Doctor / room (header)", "Loads", a.ByDoctorRoom)
        };

        List<AnalyticsPdfChart> charts = [];
        if (mode == AnalyticsPrintMode.Charts)
        {
            charts =
            [
                new AnalyticsPdfChart("Top operators", TryRenderElementPngBytes(AnalyticsTopOperatorsChart)),
                new AnalyticsPdfChart("Load distribution", TryRenderElementPngBytes(AnalyticsLoadDistributionPie)),
                new AnalyticsPdfChart("Top sterilizers", TryRenderElementPngBytes(AnalyticsTopSterilizersChart)),
                new AnalyticsPdfChart("Daily activity", TryRenderElementPngBytes(AnalyticsDailyTrendChart)),
                new AnalyticsPdfChart("Operator snapshot", TryRenderElementPngBytes(AnalyticsOperatorSnapshotChart))
            ];
        }

        return new AnalyticsPdfDocument(
            mode,
            printedAt: DateTime.Now,
            logoPng: TryLoadLogoPngBytes(),
            headerRangeText: headerRangeText,
            plainSupplementText: plainSupplementText,
            plainTopOperators: _analyticsPlainOperators.ToList(),
            plainTopSterilizers: _analyticsPlainSterilizers.ToList(),
            breakdownSections: breakdownSections,
            biPaperDetailText: biPaperDetailText,
            charts: charts);
    }

    private sealed record AnalyticsPdfChart(string Title, byte[]? PngBytes);
    private sealed record AnalyticsPdfBreakdownSection(string Title, string LoadsHeader, IReadOnlyList<AnalyticsBreakdownRowDto> Rows);

    private sealed class AnalyticsPdfDocument : IDocument
    {
        private readonly AnalyticsPrintMode _mode;
        private readonly DateTime _printedAt;
        private readonly byte[]? _logoPng;
        private readonly string _headerRangeText;
        private readonly string _plainSupplementText;
        private readonly IReadOnlyList<AnalyticsPlainRow> _plainTopOperators;
        private readonly IReadOnlyList<AnalyticsPlainRow> _plainTopSterilizers;
        private readonly IReadOnlyList<AnalyticsPdfBreakdownSection> _breakdownSections;
        private readonly string _biPaperDetailText;
        private readonly IReadOnlyList<AnalyticsPdfChart> _charts;

        public AnalyticsPdfDocument(
            AnalyticsPrintMode mode,
            DateTime printedAt,
            byte[]? logoPng,
            string headerRangeText,
            string plainSupplementText,
            IReadOnlyList<AnalyticsPlainRow> plainTopOperators,
            IReadOnlyList<AnalyticsPlainRow> plainTopSterilizers,
            IReadOnlyList<AnalyticsPdfBreakdownSection> breakdownSections,
            string biPaperDetailText,
            IReadOnlyList<AnalyticsPdfChart> charts)
        {
            _mode = mode;
            _printedAt = printedAt;
            _logoPng = logoPng;
            _headerRangeText = headerRangeText ?? "";
            _plainSupplementText = plainSupplementText ?? "";
            _plainTopOperators = plainTopOperators ?? [];
            _plainTopSterilizers = plainTopSterilizers ?? [];
            _breakdownSections = breakdownSections ?? [];
            _biPaperDetailText = biPaperDetailText ?? "";
            _charts = charts ?? [];
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(QPageSizes.A4);
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(10));

                page.Content().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Mediclinic Airport Road Hospital").SemiBold();
                            c.Item().Text("Central Supply Sterilization Department");
                        });

                        r.ConstantItem(150).AlignRight().AlignMiddle().Height(44).Element(x =>
                        {
                            if (_logoPng is not null && _logoPng.Length > 0)
                            {
                                x.Image(_logoPng).FitArea();
                            }
                            return x;
                        });
                    });

                    col.Item().PaddingTop(10).AlignCenter()
                        .Text(_mode == AnalyticsPrintMode.PlainText ? "ANALYTICS — STATISTICS REPORT" : "ANALYTICS — DASHBOARD (CHARTS)")
                        .FontSize(14).SemiBold();
                    col.Item().AlignCenter().Text(_printedAt.ToString("yyyy-MM-dd HH:mm")).FontSize(9).FontColor("#4B5563");

                    if (!string.IsNullOrWhiteSpace(_headerRangeText))
                    {
                        col.Item().PaddingTop(6).AlignCenter().Text(_headerRangeText).FontSize(9).FontColor("#4B5563");
                    }

                    if (_mode == AnalyticsPrintMode.PlainText)
                    {
                        if (!string.IsNullOrWhiteSpace(_plainSupplementText))
                        {
                            col.Item().PaddingTop(12).Text(_plainSupplementText).FontSize(8.5f).FontColor("#4B5563");
                        }

                        if (!string.IsNullOrWhiteSpace(_biPaperDetailText))
                        {
                            col.Item().PaddingTop(10).Background("#FFF7ED").Border(1).BorderColor("#FDBA74").Padding(8)
                                .Text(_biPaperDetailText).FontSize(8.5f).FontColor("#9A3412");
                        }

                        col.Item().PaddingTop(12).Text("Top operators").SemiBold();
                        col.Item().PaddingTop(6).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2.4f);
                                c.ConstantColumn(55);
                                c.ConstantColumn(55);
                                c.ConstantColumn(55);
                            });

                            static QContainer Head(QContainer x) =>
                                x.Border(1).BorderColor(QColors.Black).Background("#F5F5F5").Padding(4);
                            static QContainer Cell(QContainer x) =>
                                x.Border(1).BorderColor(QColors.Black).Padding(4);

                            t.Header(h =>
                            {
                                h.Cell().Element(Head).Text("Operator").SemiBold();
                                h.Cell().Element(Head).AlignRight().Text("Loads").SemiBold();
                                h.Cell().Element(Head).AlignRight().Text("Pcs").SemiBold();
                                h.Cell().Element(Head).AlignRight().Text("Qty").SemiBold();
                            });

                            foreach (var r in _plainTopOperators)
                            {
                                t.Cell().Element(Cell).Text(r.Name ?? "");
                                t.Cell().Element(Cell).AlignRight().Text(r.Loads.ToString(CultureInfo.InvariantCulture));
                                t.Cell().Element(Cell).AlignRight().Text(r.Pcs.ToString(CultureInfo.InvariantCulture));
                                t.Cell().Element(Cell).AlignRight().Text(r.Qty.ToString(CultureInfo.InvariantCulture));
                            }
                        });

                        col.Item().PaddingTop(12).Text("Top sterilizers").SemiBold();
                        col.Item().PaddingTop(6).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2.4f);
                                c.ConstantColumn(55);
                                c.ConstantColumn(55);
                                c.ConstantColumn(55);
                            });

                            static QContainer Head(QContainer x) =>
                                x.Border(1).BorderColor(QColors.Black).Background("#F5F5F5").Padding(4);
                            static QContainer Cell(QContainer x) =>
                                x.Border(1).BorderColor(QColors.Black).Padding(4);

                            t.Header(h =>
                            {
                                h.Cell().Element(Head).Text("Sterilizer").SemiBold();
                                h.Cell().Element(Head).AlignRight().Text("Loads").SemiBold();
                                h.Cell().Element(Head).AlignRight().Text("Pcs").SemiBold();
                                h.Cell().Element(Head).AlignRight().Text("Qty").SemiBold();
                            });

                            foreach (var r in _plainTopSterilizers)
                            {
                                t.Cell().Element(Cell).Text(r.Name ?? "");
                                t.Cell().Element(Cell).AlignRight().Text(r.Loads.ToString(CultureInfo.InvariantCulture));
                                t.Cell().Element(Cell).AlignRight().Text(r.Pcs.ToString(CultureInfo.InvariantCulture));
                                t.Cell().Element(Cell).AlignRight().Text(r.Qty.ToString(CultureInfo.InvariantCulture));
                            }
                        });

                        foreach (var sec in _breakdownSections)
                        {
                            var title = string.IsNullOrWhiteSpace(sec.Title) ? "Breakdown" : $"Breakdown — {sec.Title}";
                            col.Item().PaddingTop(12).Text(title).SemiBold();
                            col.Item().PaddingTop(6).Table(t =>
                            {
                                t.ColumnsDefinition(c =>
                                {
                                    c.RelativeColumn(2.4f);
                                    c.ConstantColumn(55);
                                    c.ConstantColumn(55);
                                    c.ConstantColumn(55);
                                });

                                static QContainer Head(QContainer x) =>
                                    x.Border(1).BorderColor(QColors.Black).Background("#F5F5F5").Padding(4);
                                static QContainer Cell(QContainer x) =>
                                    x.Border(1).BorderColor(QColors.Black).Padding(4);

                                t.Header(h =>
                                {
                                    h.Cell().Element(Head).Text("Key").SemiBold();
                                    h.Cell().Element(Head).AlignRight().Text(string.IsNullOrWhiteSpace(sec.LoadsHeader) ? "Loads" : sec.LoadsHeader).SemiBold();
                                    h.Cell().Element(Head).AlignRight().Text("Pcs").SemiBold();
                                    h.Cell().Element(Head).AlignRight().Text("Qty").SemiBold();
                                });

                                foreach (var r in sec.Rows ?? [])
                                {
                                    t.Cell().Element(Cell).Text(r.Key ?? "");
                                    t.Cell().Element(Cell).AlignRight().Text(r.Loads.ToString(CultureInfo.InvariantCulture));
                                    t.Cell().Element(Cell).AlignRight().Text(r.Pcs.ToString(CultureInfo.InvariantCulture));
                                    t.Cell().Element(Cell).AlignRight().Text(r.Qty.ToString(CultureInfo.InvariantCulture));
                                }
                            });
                        }
                    }
                    else
                    {
                        foreach (var chart in _charts)
                        {
                            if (!string.IsNullOrWhiteSpace(chart.Title))
                            {
                                col.Item().PaddingTop(12).Text(chart.Title).SemiBold();
                            }

                            if (chart.PngBytes is null || chart.PngBytes.Length == 0)
                            {
                                col.Item().PaddingTop(6).Text("(Chart unavailable)").FontSize(9).FontColor("#6B7280");
                                continue;
                            }

                            col.Item().PaddingTop(6).Border(1).BorderColor("#E5E7EB").Padding(6)
                                .Image(chart.PngBytes).FitWidth();
                        }
                    }
                });

                page.Footer().PaddingTop(8).AlignLeft().Text(txt =>
                {
                    txt.Span("Printed: ").SemiBold();
                    txt.Span(_printedAt.ToString("yyyy-MM-dd HH:mm"));
                });
            });
        }
    }

    private static string AnalyticsCsvEscaped(string s)
    {
        return "\"" + s.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string BuildAnalyticsCsv(SterilizationAnalyticsDto a, DateTime fromLocal, DateTime toLocal)
    {
        var sb = new StringBuilder();

        void AppendSection(string title, params string[] rows)
        {
            sb.AppendLine(title);
            foreach (var row in rows)
            {
                sb.AppendLine(row);
            }

            sb.AppendLine();
        }

        AppendSection(
            "Range",
            $"from,{AnalyticsCsvEscaped(fromLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}",
            $"to,{AnalyticsCsvEscaped(toLocal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))}");

        AppendSection(
            "Totals",
            $"total_loads,{a.TotalLoads}",
            $"completed,{a.CompletedLoads}",
            $"draft,{a.DraftLoads}",
            $"voided,{a.VoidedLoads}",
            $"total_pcs,{a.TotalPcs}",
            $"total_qty,{a.TotalQty}");

        if (a.ComparePriorPeriod is { } c)
        {
            AppendSection(
                "Prior_period",
                $"prior_start_utc,{AnalyticsCsvEscaped(c.PeriodStartUtc.ToString("O", CultureInfo.InvariantCulture))}",
                $"prior_end_utc,{AnalyticsCsvEscaped(c.PeriodEndUtc.ToString("O", CultureInfo.InvariantCulture))}",
                $"prior_total_loads,{c.TotalLoads}",
                $"prior_completed,{c.CompletedLoads}",
                $"prior_draft,{c.DraftLoads}",
                $"prior_voided,{c.VoidedLoads}",
                $"prior_pcs,{c.TotalPcs}",
                $"prior_qty,{c.TotalQty}");
        }

        if (a.QaTests is { } qa)
        {
            AppendSection(
                "QA_tests",
                $"leak_pass,{qa.LeakPass}",
                $"leak_fail,{qa.LeakFail}",
                $"bowie_pass,{qa.BowiePass}",
                $"bowie_fail,{qa.BowieFail}",
                $"pending_approval,{qa.PendingApproval}");
        }

        if (a.InstrumentChecks is { } ins)
        {
            AppendSection(
                "Instrument_checks",
                $"total,{ins.TotalChecks}",
                $"witness_pending,{ins.WitnessPending}",
                $"witness_approved,{ins.WitnessApproved}");
        }

        AppendSection(
            "By_operator",
            ["operator,loads,pcs,qty", .. a.ByOperator.ConvertAll(o => $"{AnalyticsCsvEscaped(o.OperatorName)},{o.Loads},{o.Pcs},{o.Qty}")]);

        AppendSection(
            "By_sterilizer",
            ["sterilizer,loads,pcs,qty", .. a.BySterilizer.ConvertAll(o => $"{AnalyticsCsvEscaped(o.SterilizerNo)},{o.Loads},{o.Pcs},{o.Qty}")]);

        AppendSection(
            "By_day",
            ["day_utc,loads,pcs,qty", .. a.ByDay.ConvertAll(d =>
                $"{AnalyticsCsvEscaped(d.DayUtc.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))},{d.Loads},{d.Pcs},{d.Qty}")]);

        AppendSection(
            "By_sterilization_type",
            ["type,loads,pcs,qty", .. a.BySterilizationType.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads},{x.Pcs},{x.Qty}")]);

        AppendSection(
            "By_bi_result",
            ["bi_result,loads,pcs,qty", .. a.ByBiResult.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads},{x.Pcs},{x.Qty}")]);

        AppendSection(
            "By_department_item_lines",
            ["department,lines,pcs,qty", .. a.ByDepartment.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads},{x.Pcs},{x.Qty}")]);

        AppendSection(
            "Top_items_by_qty",
            ["item_name,lines,pcs,qty", .. a.TopItemsByQty.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads},{x.Pcs},{x.Qty}")]);

        AppendSection(
            "Doctor_room_header",
            ["doctor_room,loads,pcs,qty", .. a.ByDoctorRoom.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads},{x.Pcs},{x.Qty}")]);

        if (a.BiLogPaper is { } bip)
        {
            AppendSection(
                "BI_log_paper",
                $"loads_in_scope,{bip.LoadsInScope}",
                $"lot_no_captured,{bip.LotNoCaptured}",
                $"routine_daily_marked,{bip.RoutineDailyMarked}",
                $"bi_time_in_captured,{bip.BiTimeInCaptured}",
                $"bi_time_out_captured,{bip.BiTimeOutCaptured}",
                $"bi_times_both_captured,{bip.BiTimesBothCaptured}",
                $"incubator_reading_checked,{bip.IncubatorReadingChecked}");
            AppendSection(
                "BI_paper_processed_24m_sign",
                ["sign,loads", .. bip.ProcessedSample24mSign.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads}")]);
            AppendSection(
                "BI_paper_processed_24h_sign",
                ["sign,loads", .. bip.ProcessedSample24hSign.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads}")]);
            AppendSection(
                "BI_paper_control_24m_sign",
                ["sign,loads", .. bip.ControlSample24mSign.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads}")]);
            AppendSection(
                "BI_paper_control_24h_sign",
                ["sign,loads", .. bip.ControlSample24hSign.ConvertAll(x => $"{AnalyticsCsvEscaped(x.Key)},{x.Loads}")]);
        }

        return sb.ToString();
    }

    private void AnalyticsBreakdownGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AnalyticsBreakdownGrid.SelectedItem is not AnalyticsBreakdownRowDto row)
        {
            return;
        }

        var idx = AnalyticsPlainBreakdownCombo.IsKeyboardFocusWithin
            ? AnalyticsPlainBreakdownCombo.SelectedIndex
            : AnalyticsBreakdownCombo.SelectedIndex;
        if (idx < 0) idx = 0;

        var f = BuildCurrentAnalyticsBaseFilter();
        var key = (row.Key ?? "").Trim();

        // Map breakdown selection to structured filter.
        switch (idx)
        {
            case 0: // Sterilization type
                if (!string.IsNullOrWhiteSpace(key) && !string.Equals(key, "(blank)", StringComparison.Ordinal))
                {
                    f.SterilizationType = key;
                }
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = $"SterilizationType = {key}" }
                });
                break;
            case 1: // BI result
                if (!string.IsNullOrWhiteSpace(key) && !string.Equals(key, "(blank)", StringComparison.Ordinal))
                {
                    f.BiResult = key;
                }
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = $"BI Result = {key}" }
                });
                break;
            case 2: // Department (item lines)
                if (!string.IsNullOrWhiteSpace(key) && !string.Equals(key, "(blank)", StringComparison.Ordinal))
                {
                    f.Department = key;
                }
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = $"Department = {key}" }
                });
                break;
            case 3: // Top items by qty
                if (!string.IsNullOrWhiteSpace(key) && !string.Equals(key, "(blank)", StringComparison.Ordinal))
                {
                    // No dedicated structured field for item name yet; use global search for now.
                    // (We’ll promote this to a structured ItemName filter once the full filter panel is in place.)
                    f.GlobalSearch = key;
                }
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = $"Item contains = {key}" }
                });
                break;
            case 4: // Doctor / room (header)
                if (row.Id is int drId)
                {
                    f.DoctorRoomId = drId;
                }
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = $"Doctor/Room = {key}" }
                });
                break;
            default:
                OpenAnalyticsDrilldown(new AnalyticsDrilldownRequestDto
                {
                    Filter = f,
                    Context = new AnalyticsDrilldownContextDto { Title = "Breakdown" }
                });
                break;
        }
    }

    private void AnalyticsPlainTopOperatorsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AnalyticsPlainTopOperatorsGrid.SelectedItem is not AnalyticsPlainRow row || string.IsNullOrWhiteSpace(row.Name))
        {
            return;
        }

        OpenLoadRecordsFromAnalytics(row.Name.Trim());
    }

    private void AnalyticsPlainTopSterilizersGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AnalyticsPlainTopSterilizersGrid.SelectedItem is not AnalyticsPlainRow row || string.IsNullOrWhiteSpace(row.Name))
        {
            return;
        }

        OpenLoadRecordsFromAnalytics(row.Name.Trim());
    }

    private void OpenLoadRecordsFromAnalytics(string searchQuery)
    {
        OpenLoadRecordsFromAnalytics(searchQuery, _analyticsLastFromLocal, _analyticsLastToLocal);
    }

    private void OpenLoadRecordsFromAnalytics(string searchQuery, DateTime fromLocal, DateTime toLocal)
    {
        SetStyleNav(LoadRecordsButton);
        HeaderTitleText.Text = "Load records";
        ShowLoadRecords();
        LoadRecordsSearchBox.Text = searchQuery.Trim();
        LoadRecordsFromDate.SelectedDate = fromLocal;
        LoadRecordsToDate.SelectedDate = toLocal;
        _ = SafeRunAsync(() => RefreshLoadRecordsAsync(searchAction: true));
    }

    private void ApplyAnalyticsPlainTextMode(bool isPlainText)
    {
        AnalyticsPlainTextPanel.Visibility = isPlainText ? Visibility.Visible : Visibility.Collapsed;
        AnalyticsDashboardPanel.Visibility = isPlainText ? Visibility.Collapsed : Visibility.Visible;
    }

    // Plain-text mode now uses Excel-like DataGrids instead of multi-line strings.

    private async Task RefreshHomeDashboardAsync()
    {
        var display = $"{_session.Profile?.FirstName} {_session.Profile?.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(display))
        {
            display = _session.Username;
        }

        var (items, err) = await _data.SearchCyclesAsync("");
        var nowLocal = HsmsDeploymentTimeZone.NowInDeploymentZone();
        if (err is not null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                SetStatus($"Home: could not load summary — {err}");
            });
            return;
        }

        var list = items.ToList();
        var today = nowLocal.Date;
        var draft = list.Count(x => string.Equals(x.CycleStatus, "Draft", StringComparison.OrdinalIgnoreCase));
        var completedToday = list.Count(x =>
            string.Equals(x.CycleStatus, "Completed", StringComparison.OrdinalIgnoreCase)
            && HsmsDeploymentTimeZone.UtcToDeployment(HsmsDeploymentTimeZone.AsUtcKind(x.CycleDateTimeUtc)).Date == today);

        await Dispatcher.InvokeAsync(() =>
        {
            HomeWelcomeLine.Text = $"Welcome, {display}";
            HomeDeploymentClockLine.Text =
                $"Deployment time (UAE): {nowLocal:dddd, d MMMM yyyy — HH:mm}";

            HomeStatTotalValue.Text = list.Count.ToString(CultureInfo.InvariantCulture);
            HomeStatTotalCaption.Text = list.Count >= 500
                ? "Up to 500 most recent cycles (empty search uses this cap)."
                : "Cycles returned for an empty search (newest first).";

            HomeStatDraftValue.Text = draft.ToString(CultureInfo.InvariantCulture);
            HomeStatCompletedTodayValue.Text = completedToday.ToString(CultureInfo.InvariantCulture);
            HomeStatCompletedTodayCaption.Text =
                $"Today is {today:yyyy-MM-dd} in the deployment zone (UAE).";

            _homeRecentCycles.Clear();
            foreach (var row in list.Take(12))
            {
                _homeRecentCycles.Add(row);
            }

            SetStatus($"Home — {list.Count} cycle(s) in summary snapshot.");
        });
    }

    private static ComboBox? GetEditingComboBox(DependencyObject? root)
    {
        if (root is null)
        {
            return null;
        }

        if (root is ComboBox direct)
        {
            return direct;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            var found = GetEditingComboBox(child);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>DataGrid cell templates sometimes leave <see cref="ComboBox.SelectedItem"/> unset while the list still shows the pick.</summary>
    private static string? GetLoadRecordsStatusPick(ComboBox cb)
    {
        if (cb.SelectedItem is string sel)
        {
            return sel;
        }

        if (cb.SelectionBoxItem is string box)
        {
            return box;
        }

        if (cb.SelectedIndex >= 0 && cb.SelectedIndex < cb.Items.Count)
        {
            return cb.Items[cb.SelectedIndex] as string ?? cb.Items[cb.SelectedIndex]?.ToString();
        }

        var text = (cb.Text ?? "").Trim();
        return text.Length == 0 ? null : text;
    }

    private static bool IsFocusedUnderCycleEndMaskedEditor(KeyEventArgs e)
    {
        for (DependencyObject? d = e.OriginalSource as DependencyObject; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is LoadRecordCycleEndTimeTextBox)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Prevent DataGrid/scroll chrome from reacting to paging keys while the masked cycle-end editor is active.</summary>
    private void LoadRecordsGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not DataGrid dg || dg != LoadRecordsGrid)
        {
            return;
        }

        if (!IsFocusedUnderCycleEndMaskedEditor(e))
        {
            return;
        }

        if (e.Key is not (Key.PageUp or Key.PageDown) || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = true;
    }

    private void LoadRecordsSearchBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        _ = SafeRunAsync(() => RefreshLoadRecordsAsync(searchAction: true));
    }

    private async Task RefreshLoadRecordsAsync(bool searchAction = false)
    {
        var q = string.IsNullOrWhiteSpace(LoadRecordsSearchBox.Text) ? "" : LoadRecordsSearchBox.Text.Trim();
        DateTime? fromUtc = null;
        DateTime? toUtc = null;
        if (LoadRecordsFromDate.SelectedDate is { } fromLocal)
        {
            fromUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(fromLocal.Date, DateTimeKind.Unspecified));
        }

        if (LoadRecordsToDate.SelectedDate is { } toLocal)
        {
            // inclusive end-of-day
            toUtc = HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(toLocal.Date.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified));
        }

        // If user picks an inverted range, normalize it.
        if (fromUtc.HasValue && toUtc.HasValue && toUtc.Value < fromUtc.Value)
        {
            (fromUtc, toUtc) = (toUtc, fromUtc);
        }

        var (rows, err) = await _data.SearchCyclesFilteredAsync(q, fromUtc, toUtc);
        if (err is not null)
        {
            MessageBox.Show(this, err, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _loadRecords.Clear();
        foreach (var r in rows)
        {
            _loadRecords.Add(new LoadRecordGridRow(r));
        }

        if (_loadRecords.Count > 0)
        {
            LoadRecordsGrid.SelectedIndex = 0;
        }

        var rangeText = (LoadRecordsFromDate.SelectedDate, LoadRecordsToDate.SelectedDate) switch
        {
            (null, null) => "",
            ({ } f, null) => $" (from {f:yyyy-MM-dd})",
            (null, { } t) => $" (to {t:yyyy-MM-dd})",
            ({ } f, { } t) => $" ({f:yyyy-MM-dd} to {t:yyyy-MM-dd})"
        };
        var prefix = searchAction ? "Search — " : "";
        SetStatus($"{prefix}Load records{rangeText} — {rows.Count} load(s).");
    }

    private void LoadRecordsGrid_OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Row.Item is not LoadRecordGridRow row)
        {
            return;
        }

        if (string.Equals(e.Column.Header as string, "Status", StringComparison.Ordinal))
        {
            if (GetEditingComboBox(e.EditingElement) is { } statusCb)
            {
                statusCb.SelectedItem = LoadRecordCycleStatuses.Normalize(row.CycleStatus);
            }

            return;
        }

        if (e.Column.Header is not string cycleHdr || !cycleHdr.StartsWith("Cycle end", StringComparison.Ordinal))
        {
            return;
        }

        if (e.EditingElement is LoadRecordCycleEndTimeTextBox masked)
        {
            masked.ValueHm = LoadRecordGridRow.FormatCycleEndTimeHm(row.CycleTimeOutUtc);
        }
    }

    private async void LoadRecordsGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel)
        {
            return;
        }

        if (e.Row.Item is not LoadRecordGridRow row)
        {
            return;
        }

        if (!CanEditSterilizationRow(row.CreatedByAccountId))
        {
            MessageBox.Show(this,
                "You can only edit records that you created (or you must be an administrator).\n\nYou can still view and print/export this record.",
                "HSMS",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            e.Cancel = true;
            await RefreshLoadRecordsAsync();
            return;
        }

        if (string.Equals(e.Column.Header as string, "Status", StringComparison.Ordinal))
        {
            if (GetEditingComboBox(e.EditingElement) is not { } statusCb)
            {
                return;
            }

            var chosen = GetLoadRecordsStatusPick(statusCb);
            if (chosen is null)
            {
                MessageBox.Show(this, "Choose a status: Draft, Completed, or Voided.", "HSMS", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                e.Cancel = true;
                return;
            }

            var normalized = LoadRecordCycleStatuses.Normalize(chosen);
            if (normalized is null)
            {
                e.Cancel = true;
                return;
            }

            var currentCanon = LoadRecordCycleStatuses.Normalize(row.CycleStatus);
            if (currentCanon is not null && string.Equals(normalized, currentCanon, StringComparison.Ordinal))
            {
                return;
            }

            e.Cancel = true;

            var statusPayload = new SterilizationCycleStatusPatchDto
            {
                RowVersion = row.RowVersion,
                CycleStatus = normalized,
                ClientMachine = Environment.MachineName
            };

            var (stOk, stErr, stRv) =
                await _data.UpdateSterilizationCycleStatusAsync(row.SterilizationId, statusPayload);
            if (!stOk || string.IsNullOrEmpty(stRv))
            {
                MessageBox.Show(this, stErr ?? "Could not save status.", "HSMS", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                await RefreshLoadRecordsAsync();
                return;
            }

            row.ApplySavedCycleStatus(normalized, stRv);

            // If staff marks the cycle as finished/voided, auto-stamp cycle end with "now" (only when empty).
            // This keeps the flow fast: status selection => end time is captured without opening another cell.
            if (!string.Equals(normalized, LoadRecordCycleStatuses.Draft, StringComparison.Ordinal) &&
                row.CycleTimeOutUtc is null)
            {
                var nowUtc = DateTime.UtcNow;
                var endPayload = new SterilizationCycleEndPatchDto
                {
                    RowVersion = row.RowVersion,
                    CycleEndUtc = nowUtc,
                    ClientMachine = Environment.MachineName
                };

                var (endOk, endErr, endRv) = await _data.UpdateSterilizationCycleEndAsync(row.SterilizationId, endPayload);
                if (!endOk || string.IsNullOrEmpty(endRv))
                {
                    MessageBox.Show(this, endErr ?? "Could not auto-fill cycle end.", "HSMS", MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    await RefreshLoadRecordsAsync();
                    return;
                }

                row.ApplySavedCycleEnd(nowUtc, endRv);
                SetStatus($"Status saved for {row.CycleNo}: {normalized}. Cycle end set.");
                return;
            }

            SetStatus($"Status saved for {row.CycleNo}: {normalized}.");
            return;
        }

        if (e.Column.Header is not string cycleHdrEdit || !cycleHdrEdit.StartsWith("Cycle end", StringComparison.Ordinal))
        {
            return;
        }

        if (e.EditingElement is not LoadRecordCycleEndTimeTextBox masked)
        {
            return;
        }

        masked.CommitToSource();

        if (!LoadRecordGridRow.TryParseCycleEndHm(masked.ValueHm, row, out var newUtc, out var parseErr))
        {
            MessageBox.Show(this, parseErr ?? "Invalid time.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
            return;
        }

        if (Nullable.Equals(newUtc, row.CycleTimeOutUtc))
        {
            return;
        }

        e.Cancel = true;

        var payload = new SterilizationCycleEndPatchDto
        {
            RowVersion = row.RowVersion,
            CycleEndUtc = newUtc,
            ClientMachine = Environment.MachineName
        };

        var (ok, err, newRv) = await _data.UpdateSterilizationCycleEndAsync(row.SterilizationId, payload);
        if (!ok || string.IsNullOrEmpty(newRv))
        {
            MessageBox.Show(this, err ?? "Could not save cycle end.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            await RefreshLoadRecordsAsync();
            return;
        }

        row.ApplySavedCycleEnd(newUtc, newRv);
        SetStatus($"Cycle end saved for {row.CycleNo}.");
    }

    private void LoadRecordsGrid_OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (e.Row?.Item is not LoadRecordGridRow row)
        {
            return;
        }

        if (!CanEditSterilizationRow(row.CreatedByAccountId))
        {
            e.Cancel = true;
            SetStatus("View-only: you can only edit records that you created (unless you are an administrator).");
        }
    }

    private bool CanEditSterilizationRow(int? createdByAccountId)
    {
        if (string.Equals(_session.Role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (createdByAccountId is null)
        {
            return false;
        }

        return createdByAccountId.Value == _session.AccountId;
    }

    private async void LoadRecordsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Double-click prints the selected row. Avoid intercepting double-click during cell edit.
        if (LoadRecordsGrid.IsKeyboardFocusWithin)
        {
            // If a cell is currently being edited, WPF treats a double-click as an edit gesture; don't print then.
            if (!LoadRecordsGrid.CommitEdit(DataGridEditingUnit.Cell, true))
            {
                return;
            }
        }

        await SafeRunAsync(PrintSelectedLoadRecordAsync);
    }

    private async void LoadRecordsPreviewSelected_OnClick(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(PreviewSelectedLoadRecordAsync);

    private async void LoadRecordsPrintSelected_OnClick(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(PrintSelectedLoadRecordAsync);

    private async void LoadRecordsExportSelected_OnClick(object sender, RoutedEventArgs e) =>
        await SafeRunAsync(ExportSelectedLoadRecordPdfAsync);

    private LoadRecordGridRow? GetSelectedLoadRecordRow() =>
        LoadRecordsGrid.SelectedItem as LoadRecordGridRow;

    private async Task<(SterilizationDetailsDto detail, LoadRecordGridRow row)?> LoadSelectedLoadRecordDetailAsync()
    {
        var row = GetSelectedLoadRecordRow();
        if (row is null)
        {
            SetStatus("Select a load record row first.");
            return null;
        }

        var (detail, err) = await _data.GetCycleAsync(row.SterilizationId);
        if (err is not null || detail is null)
        {
            MessageBox.Show(this, err ?? "Could not load cycle details.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }

        return (detail, row);
    }

    private async Task PrintSelectedLoadRecordAsync()
    {
        var loaded = await LoadSelectedLoadRecordDetailAsync();
        if (loaded is null)
        {
            return;
        }

        var (detail, row) = loaded.Value;
        var doc = BuildLoadRecordFlowDocument(detail, row, includeInstitutionalBanner: true);

        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, $"HSMS — Load record {detail.CycleNo}");
        SetStatus($"Sent load record {detail.CycleNo} to printer.");
    }

    private async Task PreviewSelectedLoadRecordAsync()
    {
        var loaded = await LoadSelectedLoadRecordDetailAsync();
        if (loaded is null)
        {
            return;
        }

        var (detail, row) = loaded.Value;
        var doc = BuildLoadRecordFlowDocument(detail, row, includeInstitutionalBanner: false);

        var viewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            IsToolBarVisible = true,
            MinZoom = 50,
            MaxZoom = 500,
            Zoom = 100,
            Margin = new Thickness(16),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var preview = new Window
        {
            Title = $"Preview — Load record {detail.CycleNo}",
            Width = Math.Min(SystemParameters.WorkArea.Width * 0.55, 920),
            Height = Math.Min(SystemParameters.WorkArea.Height * 0.88, 1020),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = viewer
        };

        SetStatus($"Opened preview for load record {detail.CycleNo}.");
        preview.ShowDialog();
    }

    private async Task ExportSelectedLoadRecordPdfAsync()
    {
        var loaded = await LoadSelectedLoadRecordDetailAsync();
        if (loaded is null)
        {
            return;
        }

        var (detail, row) = loaded.Value;

        var dlg = new SaveFileDialog
        {
            Title = "Export Load Record (PDF)",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"hsms-load-record-{detail.CycleNo}-{DateTime.Now:yyyyMMdd-HHmm}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog(this) != true) return;

        QuestPDF.Settings.License = LicenseType.Community;
        BuildLoadRecordPdf(detail, row).GeneratePdf(dlg.FileName);
        SetStatus($"Exported load record {detail.CycleNo} to PDF.");
    }

    private bool InstrumentsCheckFilter(object obj)
    {
        if (obj is not InstrumentCheckRow row) return false;
        var q = (InstrumentsCheckSearchBox?.Text ?? "").Trim();
        if (string.IsNullOrWhiteSpace(q)) return true;

        bool Has(string? s) => (s ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        return Has(row.ItemName) || Has(row.SerialReference) || Has(row.CheckedBy) || Has(row.WitnessBy) || Has(row.Remarks);
    }

    private async Task AddInstrumentCheckRowAsync()
    {
        var itemName = InstrumentsCheckItemCombo.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(itemName))
        {
            MessageBox.Show(this, "Please enter the instrument name / item description.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            InstrumentsCheckItemCombo.Focus();
            return;
        }

        var checkedBy = GetSignedInDisplayName();
        if (string.IsNullOrWhiteSpace(checkedBy))
        {
            MessageBox.Show(this, "Could not determine the signed-in operator name. Please sign in again.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var (created, err) = await _data.CreateInstrumentCheckAsync(new InstrumentCheckCreateDto
        {
            ItemName = itemName,
            SerialReference = InstrumentsCheckSerialTextBox.Text?.Trim(),
            CheckedByName = checkedBy,
            WitnessByName = InstrumentsCheckWitnessByTextBox.Text?.Trim(),
            Remarks = InstrumentsCheckRemarksTextBox.Text?.Trim()
        });

        if (err is not null || created is null)
        {
            MessageBox.Show(this, err ?? "Could not save instrument check item.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var row = new InstrumentCheckRow
        {
            CheckedAtLocal = HsmsDeploymentTimeZone.UtcToDeployment(created.CheckedAtUtc),
            ItemName = itemName,
            SerialReference = created.SerialReference ?? "",
            CheckedBy = checkedBy,
            WitnessBy = created.WitnessByName ?? "",
            Remarks = created.Remarks ?? ""
        };

        _instrumentChecks.Insert(0, row);
        _instrumentChecksView?.Refresh();

        // Feed the Item Description master list so it appears in Register Load dropdown.
        // Also add to Dept Items (Admin Panel) so it persists as an item description.
        // We pick the signed-in user's Department first; if blank, fall back to the first known department option.
        var deptForMaster = (_session.Profile?.Department ?? "").Trim();
        if (string.IsNullOrWhiteSpace(deptForMaster))
        {
            deptForMaster = (DepartmentOptions.FirstOrDefault() ?? "").Trim();
        }

        await EnsureItemDescriptionOptionAsync(
            departmentName: string.IsNullOrWhiteSpace(deptForMaster) ? null : deptForMaster,
            itemName: row.ItemName,
            pcs: 1,
            qty: 1,
            updateExistingMasterDefaults: false);

        // Make sure the new row is visible immediately (even if user had typed a filter).
        InstrumentsCheckSearchBox.Text = "";
        _instrumentChecksView?.Refresh();
        InstrumentsCheckGrid.SelectedItem = row;
        InstrumentsCheckGrid.ScrollIntoView(row);

        InstrumentsCheckSerialTextBox.Text = "";
        InstrumentsCheckCheckedByTextBox.Text = GetSignedInDisplayName();
        InstrumentsCheckWitnessByTextBox.Text = "";
        InstrumentsCheckRemarksTextBox.Text = "";
        InstrumentsCheckItemCombo.Text = "";

        SetStatus($"Added instrument check item: {row.ItemName}.");
        InstrumentsCheckItemCombo.Focus();
    }

    private Task ExportInstrumentsCheckPdfAsync()
    {
        var rows = (_instrumentChecksView?.Cast<object>() ?? _instrumentChecks.Cast<object>())
            .OfType<InstrumentCheckRow>()
            .OrderBy(x => x.CheckedAtLocal)
            .ToList();

        if (rows.Count == 0)
        {
            SetStatus("No instruments check items to export.");
            MessageBox.Show(this, "No rows to export. Adjust search or add items first.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export Instruments Check (PDF)",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"hsms-instruments-check-{DateTime.Now:yyyyMMdd-HHmm}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog(this) != true) return Task.CompletedTask;

        QuestPDF.Settings.License = LicenseType.Community;
        BuildInstrumentsCheckPdf(rows, (InstrumentsCheckSearchBox?.Text ?? "").Trim(), CurrentOperatorDisplayName()).GeneratePdf(dlg.FileName);
        SetStatus($"Exported instruments check to PDF ({rows.Count} row(s)).");
        return Task.CompletedTask;
    }

    private string CurrentOperatorDisplayName()
    {
        var first = (_session.Profile?.FirstName ?? "").Trim();
        var last = (_session.Profile?.LastName ?? "").Trim();
        var full = $"{first} {last}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) return full;
        var u = (_signedInUsername ?? "").Trim();
        return string.IsNullOrWhiteSpace(u) ? "Unknown" : u;
    }

    private static IDocument BuildLoadRecordPdf(SterilizationDetailsDto detail, LoadRecordGridRow row) =>
        new LoadRecordPdfDocument(detail, row, TryLoadLogoPngBytes());

    private static IDocument BuildInstrumentsCheckPdf(IReadOnlyList<InstrumentCheckRow> rows, string search, string printedBy) =>
        new InstrumentsCheckPdfDocument(rows, search, printedBy, TryLoadLogoPngBytes());

    private sealed class InstrumentsCheckPdfDocument : IDocument
    {
        private readonly IReadOnlyList<InstrumentCheckRow> _rows;
        private readonly string _search;
        private readonly string _printedBy;
        private readonly byte[]? _logoPng;

        public InstrumentsCheckPdfDocument(IReadOnlyList<InstrumentCheckRow> rows, string search, string printedBy, byte[]? logoPng)
        {
            _rows = rows;
            _search = search ?? "";
            _printedBy = string.IsNullOrWhiteSpace(printedBy) ? "Unknown" : printedBy;
            _logoPng = logoPng;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Size(QPageSizes.A4);
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(10));

                page.Content().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Mediclinic Airport Road Hospital").SemiBold();
                            c.Item().Text("Central Supply Sterilization Department");
                        });

                        r.ConstantItem(160).AlignRight().AlignMiddle().Height(48).Element(x =>
                        {
                            if (_logoPng is not null && _logoPng.Length > 0)
                            {
                                x.Image(_logoPng).FitArea();
                            }

                            return x;
                        });
                    });

                    col.Item().PaddingTop(10).AlignCenter().Text("INSTRUMENTS CHECK").FontSize(14).SemiBold();
                    col.Item().AlignCenter().Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).FontSize(9).FontColor("#4B5563");
                    if (!string.IsNullOrWhiteSpace(_search))
                    {
                        col.Item().PaddingTop(6).AlignCenter().Text($"Filter: {_search}").FontSize(9).FontColor("#4B5563");
                    }

                    col.Item().PaddingTop(12).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(90); // Date
                            c.ConstantColumn(55); // Time
                            c.RelativeColumn(1.7f); // Name
                            c.RelativeColumn(1.2f); // Serial
                            c.RelativeColumn(1.0f); // Checked by
                            c.RelativeColumn(1.0f); // Witness
                            c.RelativeColumn(1.4f); // Remarks
                        });

                        static QContainer Head(QContainer x) =>
                            x.Border(1).BorderColor(QColors.Black).Background("#F5F5F5").Padding(4);
                        static QContainer Cell(QContainer x) =>
                            x.Border(1).BorderColor(QColors.Black).Padding(4);

                        t.Header(h =>
                        {
                            h.Cell().Element(Head).Text("Date").SemiBold();
                            h.Cell().Element(Head).Text("Time").SemiBold();
                            h.Cell().Element(Head).Text("Name").SemiBold();
                            h.Cell().Element(Head).Text("Serial/Ref").SemiBold();
                            h.Cell().Element(Head).Text("Checked by").SemiBold();
                            h.Cell().Element(Head).Text("Witness").SemiBold();
                            h.Cell().Element(Head).Text("Remarks").SemiBold();
                        });

                        foreach (var it in _rows)
                        {
                            var dt = it.CheckedAtLocal;
                            t.Cell().Element(Cell).Text(dt.ToString("yyyy-MM-dd"));
                            t.Cell().Element(Cell).Text(dt.ToString("HH:mm"));
                            t.Cell().Element(Cell).Text(it.ItemName ?? "");
                            t.Cell().Element(Cell).Text(it.SerialReference ?? "");
                            t.Cell().Element(Cell).Text(it.CheckedBy ?? "");
                            t.Cell().Element(Cell).Text(it.WitnessBy ?? "");
                            t.Cell().Element(Cell).Text(it.Remarks ?? "");
                        }
                    });
                });

                page.Footer().PaddingTop(10).AlignLeft().Text(txt =>
                {
                    txt.Span("Operator: ").SemiBold();
                    txt.Span(_printedBy);
                    txt.Span(" • Total rows: ").SemiBold();
                    txt.Span(_rows.Count.ToString(CultureInfo.InvariantCulture));
                });
            });
        }
    }

    private sealed class LoadRecordPdfDocument : IDocument
    {
        private readonly SterilizationDetailsDto _detail;
        private readonly LoadRecordGridRow _row;
        private readonly byte[]? _logoPng;

        public LoadRecordPdfDocument(SterilizationDetailsDto detail, LoadRecordGridRow row, byte[]? logoPng)
        {
            _detail = detail;
            _row = row;
            _logoPng = logoPng;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            var cycleNo = _detail.CycleNo;
            var nowLocal = HsmsDeploymentTimeZone.UtcToDeployment(_detail.CycleDateTimeUtc);
            var startLocal = HsmsDeploymentTimeZone.UtcToDeployment(_row.RegisteredAtUtc);
            var endLocal = _row.CycleTimeOutUtc is { } outUtc ? HsmsDeploymentTimeZone.UtcToDeployment(outUtc) : (DateTime?)null;

            container.Page(page =>
            {
                page.Size(QPageSizes.A4);
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(10));
                page.Footer().PaddingTop(10).AlignLeft().Text(txt =>
                {
                    txt.Span("Operator: ").SemiBold();
                    txt.Span(string.IsNullOrWhiteSpace(_detail.OperatorName) ? "" : _detail.OperatorName.Trim());
                });

                page.Content().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Mediclinic Airport Road Hospital").SemiBold();
                            c.Item().Text("Central Supply Sterilization Department");
                        });

                        r.ConstantItem(160).AlignRight().AlignMiddle().Height(48).Element(x =>
                        {
                            if (_logoPng is not null && _logoPng.Length > 0)
                            {
                                x.Image(_logoPng).FitArea();
                            }

                            return x;
                        });
                    });

                    col.Item().PaddingTop(8);
                    col.Item().AlignCenter().Text("STEAM STERILIZATION LOAD RECORD").FontSize(14).SemiBold();
                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.RelativeColumn();
                        });

                        static QContainer HeaderCell(QContainer x) => x.PaddingVertical(2).PaddingRight(10);

                        void Row(string l1, string v1, string l2, string v2)
                        {
                            t.Cell().Element(HeaderCell).Text(txt =>
                            {
                                txt.Span(l1 + ": ").SemiBold();
                                txt.Span(v1 ?? "");
                            });
                            t.Cell().Element(HeaderCell).Text(txt =>
                            {
                                txt.Span(l2 + ": ").SemiBold();
                                txt.Span(v2 ?? "");
                            });
                        }

                        Row("Date", nowLocal.ToString("yyyy-MM-dd"),
                            "Cycle", cycleNo);
                        Row("Sterilizer", _row.SterilizerNo,
                            "Temperature", _detail.TemperatureC is { } temp ? $"{temp:0.#}°C" : "");
                        Row("Cycle start", startLocal.ToString("g", System.Globalization.CultureInfo.CurrentCulture),
                            "Cycle end", endLocal?.ToString("g", System.Globalization.CultureInfo.CurrentCulture) ?? "");
                        var statusCanon = LoadRecordCycleStatuses.Normalize(_detail.CycleStatus) ?? _detail.CycleStatus;
                        var isVoided = string.Equals(LoadRecordCycleStatuses.Normalize(statusCanon), LoadRecordCycleStatuses.Voided, StringComparison.Ordinal);
                        var biYes = !isVoided && (!string.IsNullOrWhiteSpace(_detail.BiLotNo) || !string.IsNullOrWhiteSpace(_detail.BiResult));
                        Row("Biological", biYes ? "Yes" : "No",
                            "Result", string.IsNullOrWhiteSpace(statusCanon) ? "" : statusCanon);
                    });

                    col.Item().PaddingTop(14).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1.2f); // Department
                            c.RelativeColumn(1.2f); // Doctor
                            c.RelativeColumn(2.2f); // Item
                            c.RelativeColumn(0.6f); // Pcs
                            c.RelativeColumn(0.6f); // Qty
                        });

                        static QContainer Head(QContainer x) =>
                            x.Border(1).BorderColor(QColors.Black).Background("#F5F5F5").Padding(4);
                        static QContainer Cell(QContainer x) =>
                            x.Border(1).BorderColor(QColors.Black).Padding(4);

                        t.Header(h =>
                        {
                            h.Cell().Element(Head).Text("Department").SemiBold();
                            h.Cell().Element(Head).Text("Doctor").SemiBold();
                            h.Cell().Element(Head).Text("Item description").SemiBold();
                            h.Cell().Element(Head).AlignRight().Text("Pcs").SemiBold();
                            h.Cell().Element(Head).AlignRight().Text("Qty").SemiBold();
                        });

                        var totalPcs = 0;
                        var totalQty = 0;
                        foreach (var it in _detail.Items ?? [])
                        {
                            totalPcs += Math.Max(0, it.Pcs);
                            totalQty += Math.Max(0, it.Qty);

                            t.Cell().Element(Cell).Text(it.DepartmentName ?? "");
                            t.Cell().Element(Cell).Text(NormalizeDoctorDisplay(it.DoctorOrRoom, it.DepartmentName));
                            t.Cell().Element(Cell).Text(it.ItemName ?? "");
                            t.Cell().Element(Cell).AlignRight().Text(it.Pcs.ToString(System.Globalization.CultureInfo.InvariantCulture));
                            t.Cell().Element(Cell).AlignRight().Text(it.Qty.ToString(System.Globalization.CultureInfo.InvariantCulture));
                        }

                        t.Cell().Element(Cell).Text("");
                        t.Cell().Element(Cell).Text("");
                        t.Cell().Element(Cell).Text("TOTAL").SemiBold();
                        t.Cell().Element(Cell).AlignRight().Text(totalPcs.ToString(System.Globalization.CultureInfo.InvariantCulture)).SemiBold();
                        t.Cell().Element(Cell).AlignRight().Text(totalQty.ToString(System.Globalization.CultureInfo.InvariantCulture)).SemiBold();
                    });
                });
            });
        }
    }

    private static byte[]? TryLoadLogoPngBytes()
    {
        try
        {
            var uri = new Uri("pack://application:,,,/logo.png", UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo?.Stream is null)
            {
                return null;
            }

            using var s = streamInfo.Stream;
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private FlowDocument BuildLoadRecordFlowDocument(SterilizationDetailsDto detail, LoadRecordGridRow row, bool includeInstitutionalBanner)
    {
        // A4 portrait with sensible margins. Finite ColumnWidth gives Table * columns a real width in print
        // and in FlowDocumentScrollViewer (preview omits letterhead only; see includeInstitutionalBanner).
        const double pageW = 793;   // ~ 8.27in * 96
        const double padH = 42;
        var doc = new FlowDocument
        {
            PageWidth = pageW,
            PageHeight = 1122, // ~ 11.69in * 96
            ColumnWidth = pageW - padH - padH,
            PagePadding = new Thickness(padH, 36, padH, 36),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11
        };

        if (includeInstitutionalBanner)
        {
            doc.Blocks.Add(BuildLoadRecordLetterheadBlock());
        }

        var title = new Paragraph(new Run("STEAM STERILIZATION LOAD RECORD"))
        {
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18)
        };
        doc.Blocks.Add(title);

        var nowLocal = HsmsDeploymentTimeZone.UtcToDeployment(detail.CycleDateTimeUtc);
        var startLocal = HsmsDeploymentTimeZone.UtcToDeployment(row.RegisteredAtUtc);
        var endLocal = row.CycleTimeOutUtc is { } outUtc ? HsmsDeploymentTimeZone.UtcToDeployment(outUtc) : (DateTime?)null;

        var headerGrid = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 14) };
        headerGrid.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        var g = new TableRowGroup();
        headerGrid.RowGroups.Add(g);

        void AddHeaderRow(string l1, string v1, string l2, string v2)
        {
            var r = new TableRow();
            r.Cells.Add(MakeHeaderCell(l1, v1));
            r.Cells.Add(MakeHeaderCell(l2, v2));
            g.Rows.Add(r);
        }

        AddHeaderRow("Date", nowLocal.ToString("yyyy-MM-dd"),
            "Cycle", detail.CycleNo);
        AddHeaderRow("Sterilizer", row.SterilizerNo,
            "Temperature", detail.TemperatureC is { } t ? $"{t:0.#}°C" : "");
        AddHeaderRow("Cycle start", startLocal.ToString("g", CultureInfo.CurrentCulture),
            "Cycle end", endLocal?.ToString("g", CultureInfo.CurrentCulture) ?? "");
        var statusCanon = LoadRecordCycleStatuses.Normalize(detail.CycleStatus) ?? detail.CycleStatus;
        var isVoided = string.Equals(LoadRecordCycleStatuses.Normalize(statusCanon), LoadRecordCycleStatuses.Voided, StringComparison.Ordinal);
        var biYes = !isVoided && (!string.IsNullOrWhiteSpace(detail.BiLotNo) || !string.IsNullOrWhiteSpace(detail.BiResult));
        AddHeaderRow("Biological", biYes ? "Yes" : "No",
            "Result", string.IsNullOrWhiteSpace(statusCanon) ? "" : statusCanon);

        doc.Blocks.Add(headerGrid);

        // Items table: Department | Doctor | Item description | Pcs | Qty
        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(1.2, GridUnitType.Star) }); // Department
        table.Columns.Add(new TableColumn { Width = new GridLength(1.2, GridUnitType.Star) }); // Doctor
        table.Columns.Add(new TableColumn { Width = new GridLength(2.2, GridUnitType.Star) }); // Item
        table.Columns.Add(new TableColumn { Width = new GridLength(0.55, GridUnitType.Star) }); // Pcs
        table.Columns.Add(new TableColumn { Width = new GridLength(0.55, GridUnitType.Star) }); // Qty

        var body = new TableRowGroup();
        table.RowGroups.Add(body);

        var hdr = new TableRow();
        hdr.Cells.Add(MakeTableHeader("Department"));
        hdr.Cells.Add(MakeTableHeader("Doctor"));
        hdr.Cells.Add(MakeTableHeader("Item description"));
        hdr.Cells.Add(MakeTableHeader("Pcs", right: true));
        hdr.Cells.Add(MakeTableHeader("Qty", right: true));
        body.Rows.Add(hdr);

        var totalPcs = 0;
        var totalQty = 0;
        foreach (var it in detail.Items ?? [])
        {
            totalPcs += Math.Max(0, it.Pcs);
            totalQty += Math.Max(0, it.Qty);

            var r = new TableRow();
            r.Cells.Add(MakeTableCell(it.DepartmentName ?? ""));
            r.Cells.Add(MakeTableCell(NormalizeDoctorDisplay(it.DoctorOrRoom, it.DepartmentName)));
            r.Cells.Add(MakeTableCell(it.ItemName ?? ""));
            r.Cells.Add(MakeTableCell(it.Pcs.ToString(CultureInfo.InvariantCulture), right: true));
            r.Cells.Add(MakeTableCell(it.Qty.ToString(CultureInfo.InvariantCulture), right: true));
            body.Rows.Add(r);
        }

        // Totals row
        var tot = new TableRow();
        tot.Cells.Add(MakeTableCell(""));
        tot.Cells.Add(MakeTableCell(""));
        tot.Cells.Add(MakeTableCell("TOTAL", bold: true));
        tot.Cells.Add(MakeTableCell(totalPcs.ToString(CultureInfo.InvariantCulture), right: true, bold: true));
        tot.Cells.Add(MakeTableCell(totalQty.ToString(CultureInfo.InvariantCulture), right: true, bold: true));
        body.Rows.Add(tot);

        doc.Blocks.Add(table);

        doc.Blocks.Add(new Paragraph
        {
            Margin = new Thickness(0, 18, 0, 0),
            Inlines =
            {
                new Run("Operator: ") { FontWeight = FontWeights.SemiBold },
                new Run(string.IsNullOrWhiteSpace(detail.OperatorName) ? "" : detail.OperatorName.Trim())
            }
        });
        return doc;
    }

    private static Block BuildLoadRecordLetterheadBlock()
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 10) };
        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(180) });

        var g = new TableRowGroup();
        table.RowGroups.Add(g);

        var r = new TableRow();
        g.Rows.Add(r);

        var left = new Paragraph { Margin = new Thickness(0) };
        left.Inlines.Add(new Run("Mediclinic Airport Road Hospital") { FontWeight = FontWeights.SemiBold });
        left.Inlines.Add(new LineBreak());
        left.Inlines.Add(new Run("Central Supply Sterilization Department"));
        r.Cells.Add(new TableCell(left) { Padding = new Thickness(0, 0, 10, 0) });

        var img = new System.Windows.Controls.Image
        {
            Height = 46,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };

        try
        {
            img.Source = new BitmapImage(new Uri("pack://application:,,,/logo.png", UriKind.Absolute));
        }
        catch
        {
            // ignore missing logo
        }

        r.Cells.Add(new TableCell(new BlockUIContainer(img)) { Padding = new Thickness(0) });
        return table;
    }

    private static string NormalizeDoctorDisplay(string? doctorOrRoom, string? department)
    {
        var raw = (doctorOrRoom ?? "").Trim();
        if (raw.Length == 0)
        {
            return "";
        }

        // Legacy values may be stored as "Doctor / Department". If Department column already shows it, keep just the doctor.
        var slash = raw.IndexOf('/');
        if (slash >= 0)
        {
            var left = raw[..slash].Trim();
            var right = raw[(slash + 1)..].Trim();
            if (!string.IsNullOrWhiteSpace(department) &&
                string.Equals(right, department.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return left;
            }

            return left; // still prefer a clean doctor name
        }

        return raw;
    }

    private static TableCell MakeHeaderCell(string label, string value)
    {
        var p = new Paragraph { Margin = new Thickness(0) };
        p.Inlines.Add(new Run(label + ": ") { FontWeight = FontWeights.SemiBold });
        p.Inlines.Add(new Run(value));

        return new TableCell(p)
        {
            Padding = new Thickness(2, 2, 10, 2),
            BorderThickness = new Thickness(0, 0, 0, 0)
        };
    }

    private static TableCell MakeTableHeader(string text, bool right = false)
    {
        var p = new Paragraph(new Run(text))
        {
            Margin = new Thickness(0),
            FontWeight = FontWeights.SemiBold
        };
        if (right) p.TextAlignment = TextAlignment.Right;
        return new TableCell(p)
        {
            Padding = new Thickness(6, 6, 6, 6),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5, 0.5, 0.5, 0.5)
        };
    }

    private static TableCell MakeTableCell(string text, bool right = false, bool bold = false)
    {
        var run = new Run(text ?? "");
        var p = new Paragraph(run) { Margin = new Thickness(0) };
        if (right) p.TextAlignment = TextAlignment.Right;
        if (bold) p.FontWeight = FontWeights.SemiBold;
        return new TableCell(p)
        {
            Padding = new Thickness(6, 5, 6, 5),
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5, 0.5, 0.5, 0.5)
        };
    }

    private async Task LoadBiLogSheetAsync()
    {
        var fromHour = GetSelectedHour(BiFromHourCombo);
        var toHour = GetSelectedHour(BiToHourCombo);

        DateTime? fromUtc = BiFromDate.SelectedDate is { } fromDate
            ? fromHour is { } fh
                ? HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(fromDate.Date.AddHours(fh), DateTimeKind.Unspecified))
                : HsmsDeploymentTimeZone.DeploymentCalendarDayStartUtc(fromDate)
            : null;

        DateTime? toUtc = BiToDate.SelectedDate is { } toDate
            ? toHour is { } th
                ? HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(toDate.Date.AddHours(th).AddMinutes(59).AddSeconds(59).AddMilliseconds(999), DateTimeKind.Unspecified))
                : HsmsDeploymentTimeZone.DeploymentCalendarDayEndUtc(toDate)
            : null;

        var type = BiTypeCombo.SelectedIndex <= 0 ? null : BiTypeCombo.SelectedItem?.ToString();
        var cycle = string.IsNullOrWhiteSpace(BiCycleSearchText.Text) ? null : BiCycleSearchText.Text.Trim();

        var (rows, err) = await _data.GetBiLogSheetAsync(fromUtc, toUtc, type, cycle);
        if (err is not null)
        {
            MessageBox.Show(this, err, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _biRows.Clear();
        foreach (var r in rows)
        {
            _biRows.Add(r);
        }

        RefreshBiComplianceAlerts(showDuePopups: true);
        SetStatus($"BI log sheet — {rows.Count} row(s).");
        _ = Dispatcher.BeginInvoke(new Action(UpdateBiLogGridHeight), DispatcherPriority.Loaded);
    }

    private void RefreshBiComplianceAlerts(bool showDuePopups = false)
    {
        _biComplianceAlerts.Clear();

        var total = _biRows.Count;
        if (total == 0)
        {
            BiComplianceSummaryText.Text = "No BI log rows in the selected range.";
            _biComplianceAlerts.Add(new BiComplianceAlertRow
            {
                Title = "No rows found",
                Detail = "Widen the date range or clear filters if BI loads are expected."
            });
            return;
        }

        var missingLot = _biRows.Count(r => string.IsNullOrWhiteSpace(r.BiLotNo));
        var missingTimeIn = _biRows.Count(r => string.IsNullOrWhiteSpace(r.BiTimeInTimeText));
        var missingTimeOut = _biRows.Count(r => string.IsNullOrWhiteSpace(r.BiTimeOutTimeText));
        var missingIncubatorCheck = _biRows.Count(r => r.BiIncubatorChecked != true);
        var failedProcessed = _biRows.Count(r => IsNegativeBiSign(r.BiProcessedResult24m) || IsNegativeBiSign(r.BiProcessedResult24h));
        var due24MinuteRows = GetDueBiProcessedRows(TimeSpan.FromMinutes(24), r => r.BiProcessedResult24m);
        var due24HourRows = GetDueBiProcessedRows(TimeSpan.FromHours(24), r => r.BiProcessedResult24h);
        var due24Minutes = due24MinuteRows.Count;
        var due24Hours = due24HourRows.Count;
        var missingDailyMark = _biRows.Count(r => r.BiDaily != true);
        var openItems = missingLot + missingTimeIn + missingTimeOut + missingIncubatorCheck + failedProcessed + due24Minutes + due24Hours;

        BiComplianceSummaryText.Text = $"{total} BI row(s) loaded. {openItems} compliance item(s) need attention.";

        AddBiComplianceAlert("Missing lot #", missingLot, "BI lot number is blank.");
        AddBiComplianceAlert("Missing BI time in", missingTimeIn, "Time-in has not been captured.");
        AddBiComplianceAlert("Missing BI time out", missingTimeOut, "Time-out has not been captured.");
        AddBiComplianceAlert("24 min result due", due24Minutes, "Time-in is older than 24 minutes and the 24 MIN processed result is still blank.");
        AddBiComplianceAlert("24 hr result due", due24Hours, "Time-in is older than 24 hours and the 24 HRS processed result is still blank.");
        AddBiComplianceAlert("Incubator unchecked", missingIncubatorCheck, "Incubator check is not marked.");
        AddBiComplianceAlert("Failed BI result", failedProcessed, "Processed BI result contains a negative sign.");
        AddBiComplianceAlert("Daily not marked", missingDailyMark, "Daily BI checkbox is not marked.");

        if (_biComplianceAlerts.Count == 0)
        {
            _biComplianceAlerts.Add(new BiComplianceAlertRow
            {
                Title = "No alerts",
                Detail = "BI lot, timing, incubator, and result fields are complete for this range."
            });
        }

        if (showDuePopups)
        {
            ShowBiDuePopupIfNeeded("24m", "24 MIN", due24MinuteRows);
            ShowBiDuePopupIfNeeded("24h", "24 HRS", due24HourRows);
        }
    }

    private void AddBiComplianceAlert(string title, int count, string detail)
    {
        if (count <= 0)
        {
            return;
        }

        _biComplianceAlerts.Add(new BiComplianceAlertRow
        {
            Title = $"{title}: {count}",
            Detail = detail
        });
    }

    private List<(BiLogSheetRowDto Row, DateTime DueAtUtc)> GetDueBiProcessedRows(
        TimeSpan dueAfter,
        Func<BiLogSheetRowDto, string?> resultSelector)
    {
        var nowUtc = DateTime.UtcNow;
        var due = new List<(BiLogSheetRowDto Row, DateTime DueAtUtc)>();
        foreach (var row in _biRows)
        {
            if (TryGetBiTimeInUtc(row) is not { } timeInUtc
                || nowUtc < timeInUtc.Add(dueAfter)
                || !string.IsNullOrWhiteSpace(resultSelector(row)))
            {
                continue;
            }

            due.Add((row, timeInUtc.Add(dueAfter)));
        }

        return due;
    }

    private void ShowBiDuePopupIfNeeded(
        string keySuffix,
        string label,
        IReadOnlyList<(BiLogSheetRowDto Row, DateTime DueAtUtc)> dueRows)
    {
        var newlyDue = dueRows
            .Where(x => _biCompliancePopupKeys.Add($"{x.Row.SterilizationId}:{keySuffix}:{x.DueAtUtc.Ticks}"))
            .OrderBy(x => x.DueAtUtc)
            .ToList();

        if (newlyDue.Count == 0)
        {
            return;
        }

        var examples = newlyDue
            .Take(5)
            .Select(x =>
            {
                var dueLocal = HsmsDeploymentTimeZone.UtcToDeployment(x.DueAtUtc);
                return $"- {x.Row.SterilizerNo} / {x.Row.CycleNo} due at {dueLocal:g}";
            });

        var more = newlyDue.Count > 5 ? $"{Environment.NewLine}- and {newlyDue.Count - 5} more" : "";
        var message =
            $"{newlyDue.Count} BI row(s) need the {label} processed result updated.{Environment.NewLine}{Environment.NewLine}" +
            string.Join(Environment.NewLine, examples) +
            more;

        HsmsAlertWindow.ShowWarning(this, message, $"BI {label} update due");
    }

    private static DateTime? TryGetBiTimeInUtc(BiLogSheetRowDto row)
    {
        if (row.BiTimeInUtc is { } utc)
        {
            return utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        }

        if (string.IsNullOrWhiteSpace(row.BiTimeInTimeText))
        {
            return null;
        }

        var parts = row.BiTimeInTimeText.Trim().Split(':');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minute)
            || hour is < 0 or > 23
            || minute is < 0 or > 59)
        {
            return null;
        }

        var cycleDay = HsmsDeploymentTimeZone.UtcToDeployment(row.CycleDateTimeUtc).Date;
        var deploymentTimeIn = DateTime.SpecifyKind(cycleDay.AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
        return HsmsDeploymentTimeZone.DeploymentWallToUtc(deploymentTimeIn);
    }

    private static bool IsNegativeBiSign(string? value) =>
        string.Equals((value ?? "").Trim(), "-", StringComparison.Ordinal);

    private static int? GetSelectedHour(ComboBox? combo)
    {
        if (combo?.SelectedIndex is null or <= 0)
        {
            return null;
        }

        var text = combo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hour) && hour is >= 0 and <= 23
            ? hour
            : null;
    }

    private static void BiLogTryOpenComboDropdown(ComboBox? cb)
    {
        if (cb is null)
        {
            return;
        }

        cb.IsDropDownOpen = true;
    }

    private void BiLogProcCombo_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (ReferenceEquals(Keyboard.FocusedElement, cb) || cb.IsKeyboardFocusWithin)
        {
            BiLogTryOpenComboDropdown(cb);
        }
    }

    private void BiLogTimeOutDatePicker_OnMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not DatePicker dp)
        {
            return;
        }

        var target = dp;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!target.IsVisible)
                {
                    return;
                }

                target.IsDropDownOpen = true;
            }),
            DispatcherPriority.Input);
    }

    private void BiLogTimeTextBox_OnMouseEnter(object sender, MouseEventArgs e)
    {
        AutoFillCurrentBiLogTimeIfEmpty(sender);
    }

    private void BiLogTimeTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        AutoFillCurrentBiLogTimeIfEmpty(sender);
    }

    private void AutoFillCurrentBiLogTimeIfEmpty(object? sender)
    {
        if (sender is not BiMasked24hTimeTextBox tb || !string.IsNullOrWhiteSpace(tb.ValueHm))
        {
            return;
        }

        var now = HsmsDeploymentTimeZone.NowInDeploymentZone();
        tb.ValueHm = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        tb.CommitToSource();
        SetStatus($"{(tb.IsTimeOut ? "Time Out" : "Time In")} auto-filled with current time; edit if needed.");
    }

    private void BiLogProcCombo_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is ComboBox cb)
        {
            BiLogTryOpenComboDropdown(cb);
        }
    }

    private void BiLogProcCombo_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not ComboBox cb || cb.IsDropDownOpen)
        {
            return;
        }

        e.Handled = true;
        cb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        Dispatcher.InvokeAsync(() => BiLogTryOpenComboDropdown(Keyboard.FocusedElement as ComboBox), DispatcherPriority.Background);
    }

    private void BiLogInitialsTextBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox tb || !string.IsNullOrWhiteSpace(tb.Text) || string.IsNullOrWhiteSpace(_biLogStickyInitials))
        {
            return;
        }

        tb.Text = _biLogStickyInitials;
    }

    private void BiLogInitialsTextBox_OnLostKeyboardFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox tb)
        {
            return;
        }

        var t = (tb.Text ?? "").Trim().ToUpperInvariant();
        if (t.Length is >= 2 and <= 4)
        {
            _biLogStickyInitials = t;
        }
    }

    private void BiLogInitialsTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None || sender is not DependencyObject dob)
        {
            return;
        }

        e.Handled = true;
        TryCommitBiLogGridCell(dob);
    }

    private static bool TryCommitBiLogGridCell(DependencyObject? start)
    {
        for (var o = start; o != null; o = VisualTreeHelper.GetParent(o))
        {
            if (o is DataGrid dg)
            {
                return dg.CommitEdit(DataGridEditingUnit.Cell, true);
            }
        }

        return false;
    }

    private void BiLogGrid_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (BiLogSheetsView.Visibility != Visibility.Visible)
        {
            return;
        }

        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && sender is DataGrid gridEnter)
        {
            // Excel-like: Enter moves down (same column) and begins editing.
            e.Handled = true;

            try
            {
                gridEnter.CommitEdit(DataGridEditingUnit.Cell, true);
                gridEnter.CommitEdit(DataGridEditingUnit.Row, true);
            }
            catch
            {
                // Ignore transient edit state issues; navigation still helps staff keep moving.
            }

            var col = gridEnter.CurrentCell.Column;
            var idx = gridEnter.Items.IndexOf(gridEnter.CurrentItem);
            var nextIdx = Math.Min(idx + 1, Math.Max(0, gridEnter.Items.Count - 1));
            if (nextIdx == idx || col is null)
            {
                return;
            }

            gridEnter.SelectedIndex = nextIdx;
            gridEnter.ScrollIntoView(gridEnter.Items[nextIdx], col);
            gridEnter.CurrentCell = new DataGridCellInfo(gridEnter.Items[nextIdx], col);
            gridEnter.BeginEdit();
            return;
        }

        if (e.Key != Key.Right || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (Keyboard.FocusedElement is not UIElement current)
        {
            return;
        }

        if (current is TextBox tb && tb.CaretIndex < tb.Text.Length)
        {
            return;
        }

        if (!current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Right)))
        {
            return;
        }

        e.Handled = true;
        Dispatcher.InvokeAsync(() => BiLogTryOpenComboDropdown(Keyboard.FocusedElement as ComboBox), DispatcherPriority.Background);
    }

    private bool IsBiLogEditableColumn(DataGridColumn? column)
    {
        if (column is null)
        {
            return false;
        }

        if (ReferenceEquals(column, BiLogTimeInMergedColumn) ||
            ReferenceEquals(column, BiLogTimeOutMergedColumn) ||
            ReferenceEquals(column, BiLogProc24MinColumn) ||
            ReferenceEquals(column, BiLogProc24HrColumn) ||
            ReferenceEquals(column, BiLogCtrl24MinColumn) ||
            ReferenceEquals(column, BiLogCtrl24HrColumn))
        {
            return true;
        }

        if (column is DataGridCheckBoxColumn && column.Header is string dh && dh == "Daily")
        {
            return true;
        }

        if (column is DataGridTextColumn && column.Header is string h)
        {
            if (h.StartsWith("Incubator", StringComparison.Ordinal))
            {
                return true;
            }

            if (h.StartsWith("Comments", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static BiLogSheetUpdatePayload BiLogSnapshotPayloadFromRow(BiLogSheetRowDto row) =>
        new(
            row.BiDaily,
            row.BiIncubatorTemp,
            row.BiIncubatorChecked,
            row.BiTimeInInitials,
            row.BiTimeOutInitials,
            string.IsNullOrWhiteSpace(row.BiProcessedResult24m) ? null : row.BiProcessedResult24m.Trim(),
            row.BiProcessedValue24m,
            string.IsNullOrWhiteSpace(row.BiProcessedResult24h) ? null : row.BiProcessedResult24h.Trim(),
            row.BiProcessedValue24h,
            string.IsNullOrWhiteSpace(row.BiControlResult24m) ? null : row.BiControlResult24m.Trim(),
            row.BiControlValue24m,
            string.IsNullOrWhiteSpace(row.BiControlResult24h) ? null : row.BiControlResult24h.Trim(),
            row.BiControlValue24h,
            row.Notes,
            row.BiTimeInUtc,
            row.BiTimeOutUtc);

    private static void ApplyBiLogPayloadToRow(BiLogSheetRowDto row, BiLogSheetUpdatePayload p)
    {
        row.BiDaily = p.BiDaily;
        row.BiIncubatorTemp = p.BiIncubatorTemp;
        row.BiIncubatorChecked = p.BiIncubatorChecked;
        row.BiTimeInInitials = p.BiTimeInInitials;
        row.BiTimeOutInitials = p.BiTimeOutInitials;
        row.BiProcessedResult24m = p.BiProcessedResult24m ?? "";
        row.BiProcessedValue24m = null;
        row.BiProcessedResult24h = p.BiProcessedResult24h ?? "";
        row.BiProcessedValue24h = null;
        row.BiControlResult24m = p.BiControlResult24m ?? "";
        row.BiControlValue24m = null;
        row.BiControlResult24h = p.BiControlResult24h ?? "";
        row.BiControlValue24h = null;
        row.Notes = p.Notes;
        row.BiTimeInUtc = p.BiTimeInUtc;
        row.BiTimeOutUtc = p.BiTimeOutUtc;
        BiLogSheetTimeEditor.SyncEditorsFromUtc(row);
    }

    private void BiLogGrid_OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (!IsBiLogEditableColumn(e.Column) || e.Row.Item is not BiLogSheetRowDto row)
        {
            return;
        }

        if (!CanEditSterilizationRow(row.CreatedByAccountId))
        {
            e.Cancel = true;
            SetStatus("View-only: you can only edit records that you created (unless you are an administrator).");
            return;
        }

        // Data entry mode: auto-hide sidebar to maximize table width.
        CollapseNavMenuIfVisible();
        _biLogEditSnapshot = BiLogSheetUpdateValidator.Normalize(BiLogSnapshotPayloadFromRow(row));
    }

    /// <remarks><see cref="DataGridBeginningEditEventArgs"/> has no editing element; focus is applied here instead.</remarks>
    private void BiLogGrid_OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.Column is not DataGridTemplateColumn tc || e.EditingElement is not FrameworkElement fe)
        {
            return;
        }

        if (ReferenceEquals(tc, BiLogTimeInMergedColumn) || ReferenceEquals(tc, BiLogTimeOutMergedColumn))
        {
            var nx = _biLogMergeNx;
            var ny = _biLogMergeNy;
            _biLogMergeNx = null;
            _biLogMergeNy = null;

            Dispatcher.BeginInvoke(new Action(() => BiLogTryFocusMergedEditors(fe, nx, ny)), DispatcherPriority.Loaded);
            return;
        }

        if (ReferenceEquals(tc, BiLogProc24MinColumn) ||
            ReferenceEquals(tc, BiLogProc24HrColumn) ||
            ReferenceEquals(tc, BiLogCtrl24MinColumn) ||
            ReferenceEquals(tc, BiLogCtrl24HrColumn))
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    if (ReferenceEquals(tc, BiLogProc24MinColumn) ||
                        ReferenceEquals(tc, BiLogProc24HrColumn) ||
                        ReferenceEquals(tc, BiLogCtrl24MinColumn) ||
                        ReferenceEquals(tc, BiLogCtrl24HrColumn))
                    {
                        BiLogFindVisualChild<BiPlusMinusSignToggle>(fe)?.FocusFirst();
                        return;
                    }

                    BiLogFindVisualChild<BiPlusMinusNumericEntry>(fe)?.FocusNumericField();
                }),
                DispatcherPriority.Loaded);
        }
    }

    private void BiLogGrid_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (BiLogSheetsView.Visibility != Visibility.Visible)
        {
            return;
        }

        if (sender is not DataGrid grid || e.OriginalSource is not DependencyObject src)
        {
            return;
        }

        var cell = BiLogFindAncestorDataGridCell(src);
        if (cell is not null)
        {
            // Data entry mode: hide menu as soon as staff click into the grid.
            CollapseNavMenuIfVisible();
        }

        if (cell?.Column is not DataGridTemplateColumn tc)
        {
            return;
        }

        if (!ReferenceEquals(tc, BiLogTimeInMergedColumn) && !ReferenceEquals(tc, BiLogTimeOutMergedColumn))
        {
            return;
        }

        var pt = e.GetPosition(cell);
        _biLogMergeNx = pt.X / Math.Max(cell.ActualWidth, 1.0);
        _biLogMergeNy = pt.Y / Math.Max(cell.ActualHeight, 1.0);
        grid.CurrentCell = new DataGridCellInfo(cell.DataContext, tc);

        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    if (grid.CurrentCell.Item == cell.DataContext && ReferenceEquals(grid.CurrentCell.Column, tc))
                    {
                        grid.BeginEdit();
                    }
                }
                catch (InvalidOperationException)
                {
                    // Already editing or transient state; ignore.
                }
            }),
            DispatcherPriority.Input);
    }

    private static DataGridCell? BiLogFindAncestorDataGridCell(DependencyObject? child)
    {
        for (var d = child; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is DataGridCell dc)
            {
                return dc;
            }
        }

        return null;
    }

    /// <summary>Puts keyboard focus on the control the user likely clicked (time, date, or initials).</summary>
    private void BiLogTryFocusMergedEditors(FrameworkElement editingRoot, double? nx, double? ny)
    {
        void Try(IInputElement target)
        {
            if (target is UIElement ue)
            {
                ue.Focus();
                Keyboard.Focus(ue);
            }
        }

        bool DeferIfUnmeasured()
        {
            if (editingRoot.ActualWidth >= 4 && editingRoot.ActualHeight >= 4)
            {
                return false;
            }

            Dispatcher.BeginInvoke(new Action(() => BiLogTryFocusMergedEditors(editingRoot, nx, ny)), DispatcherPriority.Loaded);
            return true;
        }

        if (DeferIfUnmeasured())
        {
            return;
        }

        var hasDatePicker = BiLogFindVisualChild<DatePicker>(editingRoot) != null;

        const double initialsSplitNx = 0.62;
        const double dateBandNyOut = 0.42;

        if (nx is { } rX && ny is { } rY &&
            !double.IsNaN(rX) && !double.IsInfinity(rX) &&
            !double.IsNaN(rY) && !double.IsInfinity(rY))
        {
            if (hasDatePicker)
            {
                if (rY < dateBandNyOut)
                {
                    var dp = BiLogFindVisualChild<DatePicker>(editingRoot);
                    if (dp != null)
                    {
                        Try(dp);
                        return;
                    }
                }

                if (rX >= initialsSplitNx)
                {
                    var init = BiLogFindInitialsTextBox(editingRoot);
                    if (init != null)
                    {
                        Try(init);
                        return;
                    }
                }

                var maskOut = BiLogFindVisualChild<BiMasked24hTimeTextBox>(editingRoot);
                if (maskOut != null)
                {
                    Try(maskOut);
                }

                return;
            }

            if (rX >= initialsSplitNx)
            {
                var initIn = BiLogFindInitialsTextBox(editingRoot);
                if (initIn != null)
                {
                    Try(initIn);
                    return;
                }
            }

            var maskIn = BiLogFindVisualChild<BiMasked24hTimeTextBox>(editingRoot);
            if (maskIn != null)
            {
                Try(maskIn);
            }

            return;
        }

        var maskFallback = BiLogFindVisualChild<BiMasked24hTimeTextBox>(editingRoot);
        if (maskFallback != null)
        {
            Try(maskFallback);
        }
    }

    private static TextBox? BiLogFindInitialsTextBox(DependencyObject root)
    {
        foreach (var d in BiLogEnumerateVisualDepthFirst(root))
        {
            if (d is TextBox tb && tb is not BiMasked24hTimeTextBox && !BiLogHasDatePickerAncestor(tb))
            {
                return tb;
            }
        }

        return null;
    }

    private static bool BiLogHasDatePickerAncestor(DependencyObject child)
    {
        for (var d = child; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is DatePicker)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<DependencyObject> BiLogEnumerateVisualDepthFirst(DependencyObject? root)
    {
        if (root == null)
        {
            yield break;
        }

        yield return root;
        int count;
        try
        {
            count = VisualTreeHelper.GetChildrenCount(root);
        }
        catch
        {
            yield break;
        }

        for (var i = 0; i < count; i++)
        {
            DependencyObject child;
            try
            {
                child = VisualTreeHelper.GetChild(root, i);
            }
            catch
            {
                continue;
            }

            foreach (var x in BiLogEnumerateVisualDepthFirst(child))
            {
                yield return x;
            }
        }
    }

    private static void BiLogFlushEditingCellBindingsToRow(object? editingElement)
    {
        if (editingElement is not FrameworkElement fe)
        {
            return;
        }

        BiLogFindVisualChild<BiMasked24hTimeTextBox>(fe)?.CommitToSource();
        var dp = BiLogFindVisualChild<DatePicker>(fe);
        if (dp != null)
        {
            BindingOperations.GetBindingExpression(dp, DatePicker.SelectedDateProperty)?.UpdateSource();
        }

        // Checkbox edits inside templates won't always commit unless we force-update the binding.
        var cb = BiLogFindVisualChild<CheckBox>(fe);
        if (cb != null)
        {
            BindingOperations.GetBindingExpression(cb, CheckBox.IsCheckedProperty)?.UpdateSource();
        }
    }

    private static T? BiLogFindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var d in BiLogEnumerateVisualDepthFirst(root))
        {
            if (d is T hit)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>DataGrid cannot call <see cref="ItemsControl.Items.Refresh"/> during an active cell edit transaction; defer until idle.</summary>
    private void ScheduleBiLogGridRefresh()
    {
        var grid = BiLogGrid;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    grid.Items.Refresh();
                }
                catch (InvalidOperationException)
                {
                    grid.Dispatcher.BeginInvoke(
                        new Action(() =>
                        {
                            try
                            {
                                grid.Items.Refresh();
                            }
                            catch (InvalidOperationException)
                            {
                                // Edit transaction may still be open; cell will redraw on next navigation.
                            }
                        }),
                        DispatcherPriority.SystemIdle);
                }
            }),
            DispatcherPriority.ApplicationIdle);
    }

    private async void BiLogGrid_OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit)
        {
            return;
        }

        if (!IsBiLogEditableColumn(e.Column) || e.Row.Item is not BiLogSheetRowDto row)
        {
            return;
        }

        BiLogFlushEditingCellBindingsToRow(e.EditingElement);

        if (!BiLogSheetTimeEditor.TryBuildCommittedPayload(row, out var afterRaw, out var mergeError))
        {
            if (_biLogEditSnapshot is { } snapMerge)
            {
                ApplyBiLogPayloadToRow(row, snapMerge);
            }
            else
            {
                BiLogSheetTimeEditor.SyncEditorsFromUtc(row);
            }

            ScheduleBiLogGridRefresh();
            if (!string.IsNullOrEmpty(mergeError))
            {
                SetStatus(mergeError);
                HsmsAlertWindow.ShowWarning(this, mergeError, "Invalid date/time");
            }

            return;
        }

        // Auto-stamp initials when staff edits Time In/Out (no initials textbox in UI).
        afterRaw = BiLogStampTimeInitials(row, afterRaw!, _biLogEditSnapshot);
        var after = BiLogSheetUpdateValidator.Normalize(afterRaw);
        if (_biLogEditSnapshot is null || after == _biLogEditSnapshot)
        {
            return;
        }

        if (BiLogSheetUpdateValidator.Validate(after) is { } validationMessage)
        {
            if (_biLogEditSnapshot is { } snap)
            {
                ApplyBiLogPayloadToRow(row, snap);
            }

            ScheduleBiLogGridRefresh();
            SetStatus(validationMessage);
            HsmsAlertWindow.ShowWarning(this, validationMessage, "Cannot save");
            return;
        }

        await _biLogResultSaveMutex.WaitAsync().ConfigureAwait(true);
        try
        {
            if (string.IsNullOrWhiteSpace(row.RowVersion))
            {
                if (_biLogEditSnapshot is { } snap0)
                {
                    ApplyBiLogPayloadToRow(row, snap0);
                }

                ScheduleBiLogGridRefresh();
                HsmsAlertWindow.ShowWarning(this,
                    "Cannot save: refresh the grid (press Go). If this is a new install, run hsms-db/ddl scripts (012, 013) on the database.",
                    "Cannot save");
                return;
            }

            var (ok, err, newRv, updatedUtc) = await _data.UpdateSterilizationBiLogSheetAsync(
                row.SterilizationId,
                row.RowVersion,
                after,
                Environment.MachineName).ConfigureAwait(true);

            if (!ok)
            {
                if (_biLogEditSnapshot is { } snap1)
                {
                    ApplyBiLogPayloadToRow(row, snap1);
                }

                ScheduleBiLogGridRefresh();
                SetStatus(err ?? "BI log sheet save failed.");
                HsmsAlertWindow.ShowWarning(this, err ?? "BI log sheet save failed.", SaveFailureDialogTitle(err ?? ""));
                return;
            }

            if (!string.IsNullOrEmpty(newRv))
            {
                row.RowVersion = newRv;
            }

            row.BiResultUpdatedAtUtc = updatedUtc;
            ScheduleBiLogGridRefresh();
            RefreshBiComplianceAlerts(showDuePopups: true);
            SetStatus("BI log sheet saved.");
        }
        finally
        {
            _biLogResultSaveMutex.Release();
        }
    }

    private void ExportBiLogSheetCsv()
    {
        try
        {
            var dlg = new SaveFileDialog
            {
                Title = "Export BI Log Sheet (CSV)",
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"hsms-bi-log-{DateTime.Now:yyyyMMdd-HHmm}.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
            };
            if (dlg.ShowDialog(this) != true) return;

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",",
                "Date_display",
                "Date_utc_ISO",
                "SterilizerCycleNo",
                "LotNo",
                "Daily",
                "ContainImplant",
                "LoadQty",
                "ExposureTimeTemp",
                "IncubatorChecked",
                "BiTimeIn_display",
                "BiTimeIn_utc_ISO",
                "TimeInInitials",
                "BiTimeOut_display",
                "BiTimeOut_utc_ISO",
                "TimeOutInitials",
                "BI_processed_24min",
                "BI_processed_24hr",
                "BI_control_24min",
                "BI_control_24hr",
                "Operator",
                "CommentsResult",
                "LastUpdated_display",
                "LastUpdated_utc_ISO"));

            foreach (var r in _biRows)
            {
                sb.AppendLine(string.Join(",",
                    Csv(HsmsDeploymentTimeZone.FormatInDeploymentZone(r.CycleDateTimeUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    CsvBiUtcIso(r.CycleDateTimeUtc),
                    Csv(r.SterilizerCycleNoDisplay),
                    Csv(r.BiLotNo),
                    Csv(r.BiDaily switch { true => "1", false => "0", null => "" }),
                    Csv(r.Implants ? "1" : "0"),
                    Csv(r.LoadQty?.ToString()),
                    Csv(r.ExposureTimeTempDisplay),
                    Csv(r.BiIncubatorChecked switch { true => "✓", false => "✗", null => "" }),
                    CsvBiNullableLocal(r.BiTimeInUtc),
                    CsvBiNullableIso(r.BiTimeInUtc),
                    Csv(r.BiTimeInInitials),
                    CsvBiNullableLocal(r.BiTimeOutUtc),
                    CsvBiNullableIso(r.BiTimeOutUtc),
                    Csv(r.BiTimeOutInitials),
                    Csv(r.BiProcessedResult24m),
                    Csv(r.BiProcessedResult24h),
                    Csv(r.BiControlResult24m),
                    Csv(r.BiControlResult24h),
                    Csv(r.OperatorName),
                    Csv(r.Notes),
                    CsvBiNullableLocal(r.BiResultUpdatedAtUtc),
                    CsvBiNullableIso(r.BiResultUpdatedAtUtc)));
            }

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            SetStatus($"Exported CSV: {dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string Csv(string? value)
    {
        value ??= "";
        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        if (!needsQuotes) return value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string CsvBiLocal(DateTime storedUtcOrUnspecified) =>
        Csv(HsmsDeploymentTimeZone.FormatInDeploymentZone(storedUtcOrUnspecified, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

    private static string CsvBiUtcIso(DateTime storedUtcOrUnspecified) =>
        Csv(HsmsDeploymentTimeZone.AsUtcKind(storedUtcOrUnspecified).ToString("O", CultureInfo.InvariantCulture));

    private static string CsvBiNullableLocal(DateTime? storedUtcOrUnspecified) =>
        storedUtcOrUnspecified is { } dt ? CsvBiLocal(dt) : "";

    private static string CsvBiNullableIso(DateTime? storedUtcOrUnspecified) =>
        storedUtcOrUnspecified is { } dt ? CsvBiUtcIso(dt) : "";

    private void BiLogPreview_OnClick(object sender, RoutedEventArgs e) =>
        _ = SafeRunAsync(PreviewBiLogSheetAsync);

    private void BiLogPrint_OnClick(object sender, RoutedEventArgs e) =>
        _ = SafeRunAsync(PrintBiLogSheetAsync);

    private void BiLogExportPdf_OnClick(object sender, RoutedEventArgs e) =>
        _ = SafeRunAsync(ExportBiLogSheetPdfAsync);

    private FlowDocument BuildBiLogSheetFlowDocument(bool includeInstitutionalBanner)
    {
        // A4 landscape-ish width for a wide sheet; FlowDocument doesn't support orientation, so we widen the page.
        const double pageW = 1122;  // ~ 11.69in * 96
        const double pageH = 793;   // ~ 8.27in * 96
        const double padH = 36;

        var doc = new FlowDocument
        {
            PageWidth = pageW,
            PageHeight = pageH,
            ColumnWidth = pageW - padH - padH,
            PagePadding = new Thickness(padH, 28, padH, 28),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 9
        };

        if (includeInstitutionalBanner)
        {
            doc.Blocks.Add(BuildLoadRecordLetterheadBlock());
        }

        doc.Blocks.Add(new Paragraph(new Run("STEAM STERILIZER PERIODIC QUALITY ASSURANCE — BIOLOGICAL INDICATOR TEST RESULT"))
        {
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var filter = BuildBiLogSheetFilterSummary();
        if (!string.IsNullOrWhiteSpace(filter))
        {
            doc.Blocks.Add(new Paragraph(new Run(filter))
            {
                FontSize = 9,
                Foreground = Brushes.DimGray,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }

        var table = new Table { CellSpacing = 0 };
        table.Columns.Add(new TableColumn { Width = new GridLength(70) }); // Date
        table.Columns.Add(new TableColumn { Width = new GridLength(92) }); // Sterilizer/Cycle
        table.Columns.Add(new TableColumn { Width = new GridLength(48) }); // Lot
        table.Columns.Add(new TableColumn { Width = new GridLength(40) }); // Daily
        table.Columns.Add(new TableColumn { Width = new GridLength(48) }); // Implant
        table.Columns.Add(new TableColumn { Width = new GridLength(40) }); // Qty
        table.Columns.Add(new TableColumn { Width = new GridLength(86) }); // Exposure
        table.Columns.Add(new TableColumn { Width = new GridLength(62) }); // Incubator
        table.Columns.Add(new TableColumn { Width = new GridLength(86) }); // Time in
        table.Columns.Add(new TableColumn { Width = new GridLength(86) }); // Time out
        table.Columns.Add(new TableColumn { Width = new GridLength(76) }); // Proc 24m
        table.Columns.Add(new TableColumn { Width = new GridLength(76) }); // Proc 24h
        table.Columns.Add(new TableColumn { Width = new GridLength(76) }); // Ctrl 24m
        table.Columns.Add(new TableColumn { Width = new GridLength(76) }); // Ctrl 24h
        table.Columns.Add(new TableColumn { Width = new GridLength(120) }); // Operator
        table.Columns.Add(new TableColumn { Width = new GridLength(160) }); // Notes

        var body = new TableRowGroup();
        table.RowGroups.Add(body);

        static TableCell MultiHeader(params string[] lines)
        {
            var p = new Paragraph { Margin = new Thickness(0), FontWeight = FontWeights.SemiBold };
            for (var i = 0; i < lines.Length; i++)
            {
                if (i != 0) p.Inlines.Add(new LineBreak());
                p.Inlines.Add(new Run(lines[i]));
            }

            return new TableCell(p)
            {
                Padding = new Thickness(6, 6, 6, 6),
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0.5, 0.5, 0.5, 0.5)
            };
        }

        // Use printer-safe glyphs (many printer drivers drop ✓/✗).
        static string Mark(bool? v) => v switch { true => "√", false => "X", null => "" };

        var hdr = new TableRow();
        hdr.Cells.Add(MultiHeader("Date"));
        hdr.Cells.Add(MultiHeader("Sterilizer No.", "Cycle No."));
        hdr.Cells.Add(MultiHeader("LOT NO"));
        hdr.Cells.Add(MultiHeader("Daily"));
        hdr.Cells.Add(MultiHeader("Contain", "implant"));
        hdr.Cells.Add(MultiHeader("Qty of items", "in the load"));
        hdr.Cells.Add(MultiHeader("Exposure time", "/ Temp"));
        hdr.Cells.Add(MultiHeader("Incubator temp", "(e.g. 60°C +12)"));
        hdr.Cells.Add(MultiHeader("Time In / Initials", "HH:mm · initials"));
        hdr.Cells.Add(MultiHeader("Time Out / Initials", "HH:mm · initials"));
        hdr.Cells.Add(MultiHeader("BI processed", "24 MIN"));
        hdr.Cells.Add(MultiHeader("BI processed", "24 HRS"));
        hdr.Cells.Add(MultiHeader("BI control", "24 MIN"));
        hdr.Cells.Add(MultiHeader("BI control", "24 HRS"));
        hdr.Cells.Add(MultiHeader("Operator"));
        hdr.Cells.Add(MultiHeader("Comments", "Result"));
        body.Rows.Add(hdr);

        foreach (var r in _biRows)
        {
            var row = new TableRow();
            row.Cells.Add(MakeTableCell(HsmsDeploymentTimeZone.FormatInDeploymentZone(r.CycleDateTimeUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
            row.Cells.Add(MakeTableCell(r.SterilizerCycleNoDisplay ?? ""));
            row.Cells.Add(MakeTableCell(r.BiLotNo ?? ""));
            row.Cells.Add(MakeTableCell(Mark(r.BiDaily), right: true));
            row.Cells.Add(MakeTableCell(Mark(r.Implants), right: true));
            row.Cells.Add(MakeTableCell(r.LoadQty?.ToString(CultureInfo.InvariantCulture) ?? "", right: true));
            row.Cells.Add(MakeTableCell(r.ExposureTimeTempDisplay ?? ""));
            row.Cells.Add(MakeTableCell(Mark(r.BiIncubatorChecked), right: true));
            // Show time only (no initials) in reports.
            row.Cells.Add(MakeTableCell(r.BiTimeInDisplayTime ?? "__:__"));
            row.Cells.Add(MakeTableCell(r.BiTimeOutDisplayTime ?? "__:__"));
            row.Cells.Add(MakeTableCell(r.BiProcessedResult24m ?? ""));
            row.Cells.Add(MakeTableCell(r.BiProcessedResult24h ?? ""));
            row.Cells.Add(MakeTableCell(r.BiControlResult24m ?? ""));
            row.Cells.Add(MakeTableCell(r.BiControlResult24h ?? ""));
            row.Cells.Add(MakeTableCell(r.OperatorName ?? ""));
            row.Cells.Add(MakeTableCell(r.Notes ?? ""));
            body.Rows.Add(row);
        }

        doc.Blocks.Add(table);
        doc.Blocks.Add(new Paragraph
        {
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 9,
            Foreground = Brushes.DimGray,
            Inlines =
            {
                new Run("Printed: ") { FontWeight = FontWeights.SemiBold },
                new Run(DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
                new Run(" • Total rows: ") { FontWeight = FontWeights.SemiBold },
                new Run(_biRows.Count.ToString(CultureInfo.InvariantCulture))
            }
        });

        return doc;
    }

    private string BuildBiLogSheetFilterSummary()
    {
        var parts = new List<string>();
        var quick = DescribeBiQuickRange();
        if (!string.IsNullOrWhiteSpace(quick))
        {
            parts.Add("Range: " + quick);
        }
        if (BiFromDate.SelectedDate is { } fd)
        {
            parts.Add("From: " + fd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (GetSelectedHour(BiFromHourCombo) is { } h ? $" {h}:00" : ""));
        }
        if (BiToDate.SelectedDate is { } td)
        {
            parts.Add("To: " + td.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + (GetSelectedHour(BiToHourCombo) is { } h2 ? $" {h2}:59" : ""));
        }
        var type = (BiTypeCombo.SelectedItem as string) ?? "";
        if (!string.IsNullOrWhiteSpace(type) && !string.Equals(type, "(All)", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("Type: " + type);
        }
        var cycle = (BiCycleSearchText.Text ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(cycle))
        {
            parts.Add("Cycle: " + cycle);
        }
        return string.Join(" • ", parts);
    }

    private async Task PreviewBiLogSheetAsync()
    {
        if (_biRows.Count == 0)
        {
            SetStatus("No BI log sheet rows to preview.");
            MessageBox.Show(this, "No rows to preview. Adjust filters and press Go.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var doc = BuildBiLogSheetFlowDocument(includeInstitutionalBanner: false);
        var viewer = new FlowDocumentScrollViewer
        {
            Document = doc,
            IsToolBarVisible = true,
            MinZoom = 50,
            MaxZoom = 500,
            Zoom = 100,
            Margin = new Thickness(16),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var preview = new Window
        {
            Title = "Preview — BI Log Sheet",
            Width = Math.Min(SystemParameters.WorkArea.Width * 0.82, 1180),
            Height = Math.Min(SystemParameters.WorkArea.Height * 0.88, 980),
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Content = viewer
        };

        SetStatus("Opened BI log sheet preview.");
        preview.ShowDialog();
        await Task.CompletedTask;
    }

    private async Task PrintBiLogSheetAsync()
    {
        if (_biRows.Count == 0)
        {
            SetStatus("No BI log sheet rows to print.");
            MessageBox.Show(this, "No rows to print. Adjust filters and press Go.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var doc = BuildBiLogSheetFlowDocument(includeInstitutionalBanner: true);
        var dlg = new PrintDialog();
        if (dlg.ShowDialog() != true) return;
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "HSMS — BI Log Sheet");
        SetStatus("Sent BI Log Sheet to printer.");
        await Task.CompletedTask;
    }

    private async Task ExportBiLogSheetPdfAsync()
    {
        if (_biRows.Count == 0)
        {
            SetStatus("No BI log sheet rows to export.");
            MessageBox.Show(this, "No rows to export. Adjust filters and press Go.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Export BI Log Sheet (PDF)",
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = $"hsms-bi-log-{DateTime.Now:yyyyMMdd-HHmm}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog(this) != true) return;

        QuestPDF.Settings.License = LicenseType.Community;
        new BiLogSheetPdfDocument(_biRows.ToList(), BuildBiLogSheetFilterSummary(), TryLoadLogoPngBytes()).GeneratePdf(dlg.FileName);
        SetStatus($"Exported BI log sheet to PDF ({_biRows.Count} row(s)).");
        await Task.CompletedTask;
    }

    private sealed class BiLogSheetPdfDocument : IDocument
    {
        private readonly IReadOnlyList<BiLogSheetRowDto> _rows;
        private readonly string _filter;
        private readonly byte[]? _logoPng;

        public BiLogSheetPdfDocument(IReadOnlyList<BiLogSheetRowDto> rows, string filter, byte[]? logoPng)
        {
            _rows = rows;
            _filter = filter ?? "";
            _logoPng = logoPng;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                // QuestPDF 2026.x: PageSizes presets return PageSize (no Landscape() helper), so swap dimensions.
                var a4 = QPageSizes.A4;
                page.Size(a4.Height, a4.Width);
                page.Margin(12);
                page.DefaultTextStyle(x => x.FontFamily("Segoe UI").FontSize(7.5f));

                page.Content().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Mediclinic Airport Road Hospital").SemiBold();
                            c.Item().Text("Central Supply Sterilization Department");
                        });

                        r.ConstantItem(150).AlignRight().AlignMiddle().Height(44).Element(x =>
                        {
                            if (_logoPng is not null && _logoPng.Length > 0)
                            {
                                x.Image(_logoPng).FitArea();
                            }
                            return x;
                        });
                    });

                    col.Item().PaddingTop(8).AlignCenter()
                        .Text("STEAM STERILIZER PERIODIC QUALITY ASSURANCE — BIOLOGICAL INDICATOR TEST RESULT")
                        .FontSize(12).SemiBold();

                    if (!string.IsNullOrWhiteSpace(_filter))
                    {
                        col.Item().PaddingTop(4).AlignCenter().Text(_filter).FontSize(8).FontColor("#4B5563");
                    }

                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(52); // Date
                            c.ConstantColumn(70); // Ster/Cycle
                            c.ConstantColumn(34); // LOT
                            c.ConstantColumn(22); // Daily
                            c.ConstantColumn(28); // Implant
                            c.ConstantColumn(26); // Qty
                            c.ConstantColumn(62); // Exposure
                            c.ConstantColumn(44); // Incub
                            c.ConstantColumn(70); // Time in
                            c.ConstantColumn(70); // Time out
                            c.ConstantColumn(40); // Proc 24m
                            c.ConstantColumn(40); // Proc 24h
                            c.ConstantColumn(40); // Ctrl 24m
                            c.ConstantColumn(40); // Ctrl 24h
                            c.RelativeColumn(1.0f); // Operator
                            c.RelativeColumn(1.4f); // Comments
                        });

                        static QContainer Head(QContainer x) =>
                            x.Border(1).BorderColor(QColors.Black).Background("#F5F5F5").Padding(3);
                        static QContainer Cell(QContainer x) =>
                            x.Border(1).BorderColor(QColors.Black).Padding(3);

                        static string Mark2(bool? v) => v switch { true => "√", false => "X", null => "" };

                        t.Header(h =>
                        {
                            h.Cell().Element(Head).Text("Date").SemiBold();
                            h.Cell().Element(Head).Text("Sterilizer No.\nCycle No.").SemiBold();
                            h.Cell().Element(Head).Text("LOT NO").SemiBold();
                            h.Cell().Element(Head).AlignCenter().Text("Daily").SemiBold();
                            h.Cell().Element(Head).AlignCenter().Text("Contain\nimplant").SemiBold();
                            h.Cell().Element(Head).AlignRight().Text("Qty of items\nin the load").SemiBold();
                            h.Cell().Element(Head).Text("Exposure time\n/ Temp").SemiBold();
                            h.Cell().Element(Head).Text("Incubator temp\n(e.g. 60°C +12)").SemiBold();
                            h.Cell().Element(Head).Text("Time In / Initials").SemiBold();
                            h.Cell().Element(Head).Text("Time Out / Initials").SemiBold();
                            h.Cell().Element(Head).AlignCenter().Text("BI processed\n24 MIN").SemiBold();
                            h.Cell().Element(Head).AlignCenter().Text("BI processed\n24 HRS").SemiBold();
                            h.Cell().Element(Head).AlignCenter().Text("BI control\n24 MIN").SemiBold();
                            h.Cell().Element(Head).AlignCenter().Text("BI control\n24 HRS").SemiBold();
                            h.Cell().Element(Head).Text("Operator").SemiBold();
                            h.Cell().Element(Head).Text("Comments\nResult").SemiBold();
                        });

                        foreach (var it in _rows)
                        {
                            t.Cell().Element(Cell).Text(HsmsDeploymentTimeZone.FormatInDeploymentZone(it.CycleDateTimeUtc, "yyyy-MM-dd", CultureInfo.InvariantCulture));
                            t.Cell().Element(Cell).Text(it.SterilizerCycleNoDisplay ?? "");
                            t.Cell().Element(Cell).Text(it.BiLotNo ?? "");
                            t.Cell().Element(Cell).AlignCenter().Text(Mark2(it.BiDaily));
                            t.Cell().Element(Cell).AlignCenter().Text(Mark2(it.Implants));
                            t.Cell().Element(Cell).AlignRight().Text(it.LoadQty?.ToString(CultureInfo.InvariantCulture) ?? "");
                            t.Cell().Element(Cell).Text(it.ExposureTimeTempDisplay ?? "");
                            t.Cell().Element(Cell).AlignCenter().Text(Mark2(it.BiIncubatorChecked));

                            var tin = string.IsNullOrWhiteSpace(it.BiTimeInTimeText) ? "__:__" : it.BiTimeInTimeText.Trim();
                            var tout = string.IsNullOrWhiteSpace(it.BiTimeOutTimeText) ? "__:__" : it.BiTimeOutTimeText.Trim();
                            t.Cell().Element(Cell).Text(tin);
                            t.Cell().Element(Cell).Text(tout);

                            t.Cell().Element(Cell).AlignCenter().Text(it.BiProcessedResult24m ?? "");
                            t.Cell().Element(Cell).AlignCenter().Text(it.BiProcessedResult24h ?? "");
                            t.Cell().Element(Cell).AlignCenter().Text(it.BiControlResult24m ?? "");
                            t.Cell().Element(Cell).AlignCenter().Text(it.BiControlResult24h ?? "");
                            t.Cell().Element(Cell).Text(it.OperatorName ?? "");
                            t.Cell().Element(Cell).Text(it.Notes ?? "");
                        }
                    });
                });

                page.Footer().PaddingTop(8).AlignLeft().Text(txt =>
                {
                    txt.Span("Printed: ").SemiBold();
                    txt.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                    txt.Span(" • Total rows: ").SemiBold();
                    txt.Span(_rows.Count.ToString(CultureInfo.InvariantCulture));
                });
            });
        }
    }

    private static void ShowPrintPlaceholder()
    {
        var owner = System.Windows.Application.Current.MainWindow;
        var msg = "Reporting coordinator is not registered for this session. Please reopen HSMS.";
        if (owner is not null)
        {
            MessageBox.Show(owner, msg, "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(msg, "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>F9 quick-print hook: pick the report based on the active screen and dispatch via the print pipeline.</summary>
    private async Task QuickPrintCurrentScreenAsync()
    {
        if (_printCoordinator is null) { ShowPrintPlaceholder(); return; }
        var request = BuildPrintRequestForActiveScreen();
        if (request is null) return;
        await _printCoordinator.QuickPrintAsync(request, this);
    }

    /// <summary>Print button hook: same as F9 but always shows the preview modal.</summary>
    private async Task PrintCurrentScreenAsync()
    {
        if (_printCoordinator is null) { ShowPrintPlaceholder(); return; }
        var request = BuildPrintRequestForActiveScreen();
        if (request is null) return;
        await _printCoordinator.ShowPreviewAsync(request, this);
    }

    private ReportRenderRequestDto? BuildPrintRequestForActiveScreen()
    {
        var clientMachine = Environment.MachineName;

        if (BiLogSheetsView is not null && BiLogSheetsView.Visibility == Visibility.Visible)
        {
            var fromLocal = BiFromDate?.SelectedDate ?? HsmsDeploymentTimeZone.NowInDeploymentZone().Date.AddDays(-7);
            var toLocal = BiToDate?.SelectedDate ?? HsmsDeploymentTimeZone.NowInDeploymentZone().Date;
            var fromUtc = HsmsDeploymentTimeZone.DeploymentCalendarDayStartUtc(fromLocal);
            var toUtc = HsmsDeploymentTimeZone.DeploymentCalendarDayEndUtc(toLocal);
            string? typeFilter = null;
            if (BiTypeCombo?.SelectedItem is string s && !string.Equals(s, "(All)", StringComparison.Ordinal))
            {
                typeFilter = s;
            }
            return new ReportRenderRequestDto
            {
                ReportType = ReportType.BILogSheet,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                SterilizationTypeFilter = typeFilter,
                IncludeReceiptImages = false,
                ClientMachine = clientMachine
            };
        }

        if (_currentSterilizationId is int id)
        {
            return new ReportRenderRequestDto
            {
                ReportType = ReportType.LoadRecord,
                SterilizationId = id,
                IncludeReceiptImages = true,
                ClientMachine = clientMachine
            };
        }

        MessageBox.Show(this, "Open a cycle first, or switch to the BI log sheet, before pressing Print / F9.",
            "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }

    private void ProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var win = new ProfileWindow(_session, _auth) { Owner = this };
        if (win.ShowDialog() == true)
        {
            var display = $"{_session.Profile?.FirstName} {_session.Profile?.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(display))
            {
                display = _session.Username;
            }

            SetAccountHeader(display);
        }
    }

    private void SetAccountHeader(string display) => AccountLabel.Text = $"Account: {display}";

    private void LogoutButton_OnClick(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(this, "Log out and return to the staff portal (sign in or create an account)?", "HSMS",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (r == MessageBoxResult.Yes)
        {
            ReturnToPortal = true;
            Close();
        }
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        OperatorTextBox.Text = GetSignedInDisplayName();
        RegisterPcsCombo.Text = "1";
        RegisterQtyCombo.Text = "1";
        await SafeRunAsync(CheckSchemaHealthAsync);
        await SafeRunAsync(LoadSterilizersAsync);
        await SafeRunAsync(LoadDepartmentAndDoctorOptionsAsync);
        await SafeRunAsync(EnsureCycleNoAsync);
        await SafeRunAsync(RefreshHomeDashboardAsync);
        HomeButton.Focus();
    }

    private async Task EnsureCycleNoAsync()
    {
        if (!string.IsNullOrWhiteSpace(CycleNoTextBox.Text))
        {
            return;
        }

        var (next, err) = await _data.GetNextCycleNoAsync();
        if (err is not null)
        {
            SetStatus(err);
            return;
        }

        CycleNoTextBox.Text = next ?? "";
    }

    private async Task CheckSchemaHealthAsync()
    {
        var (health, err) = await _data.GetSchemaHealthAsync();
        if (err is not null || health is null) return;
        if (health.IsOk || _schemaWarningShown) return;

        _schemaWarningShown = true;
        var details = health.MissingItems.Count == 0
            ? health.Message
            : $"{health.Message}\n\nMissing:\n- {string.Join("\n- ", health.MissingItems)}";
        MessageBox.Show(this, details, "HSMS schema check", MessageBoxButton.OK, MessageBoxImage.Warning);
        SetStatus("Schema check warning: run latest ddl scripts.");
    }

    private async Task LoadSterilizersAsync()
    {
        var (list, error) = await _data.GetSterilizersAsync();
        if (error is not null)
        {
            SetStatus(error);
            MessageBox.Show(this, $"Could not load sterilizers.\n\n{error}", "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var activeList = list.Where(x => x.IsActive).ToList();
        SterilizerCombo.ItemsSource = activeList;
        var first = activeList.FirstOrDefault();
        if (first is not null)
        {
            SterilizerCombo.SelectedValue = first.SterilizerId;
        }

        SetStatus(activeList.Count > 0 ? $"Ready — {activeList.Count} sterilizer(s) on file." : "Ready — add sterilizers in Maintenance.");
    }

    private async Task LoadDepartmentAndDoctorOptionsAsync()
    {
        var (departments, depErr) = await _data.GetDepartmentsAsync();
        if (depErr is not null)
        {
            SetStatus($"Department master unavailable ({depErr}).");
            DepartmentOptions.Clear();
            return;
        }

        DepartmentOptions.Clear();
        foreach (var d in departments.Where(x => x.IsActive).Select(x => x.DepartmentName))
        {
            DepartmentOptions.Add(d);
        }

        var (doctors, docErr) = await _data.GetDoctorRoomsAsync();
        if (docErr is not null)
        {
            SetStatus($"Doctor/room master unavailable ({docErr}).");
            DoctorRoomOptions.Clear();
            return;
        }

        _doctorRooms.Clear();
        _doctorRooms.AddRange(doctors.Where(x => x.IsActive));
        DoctorRoomOptions.Clear();
        foreach (var d in _doctorRooms
                     .Select(x => x.DoctorName)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            DoctorRoomOptions.Add(d);
        }

        // ItemsSource is bound in XAML; keep options collection updated only.

        var (departmentItems, itemErr) = await _data.GetDepartmentItemsAsync();
        if (itemErr is not null)
        {
            SetStatus($"Department items master unavailable ({itemErr}).");
            _departmentItems.Clear();
            return;
        }

        _departmentItems.Clear();
        _departmentItems.AddRange(departmentItems);

        ItemDescriptionOptions.Clear();
        foreach (var name in _departmentItems
                     .Select(x => x.ItemName)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x))
        {
            ItemDescriptionOptions.Add(name);
        }

        ApplySelectedItemDefaults();

        foreach (var row in _items)
        {
            RefreshRowItemOptions(row);
        }
    }

    private static string? GetRegisterItemDescriptionDisplayText(ComboBox combo)
    {
        var typed = combo.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(typed))
        {
            return typed;
        }

        return combo.SelectedItem?.ToString()?.Trim();
    }

    private DepartmentItemListItemDto? FindDepartmentItemDefaultsForDescription(string itemDescription)
    {
        if (string.IsNullOrWhiteSpace(itemDescription))
        {
            return null;
        }

        var dept = string.IsNullOrWhiteSpace(DepartmentCombo.Text) ? null : DepartmentCombo.Text.Trim();
        List<DepartmentItemListItemDto> candidates = _departmentItems
            .Where(x => string.Equals(x.ItemName, itemDescription, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(dept))
        {
            var forDept = candidates.FirstOrDefault(x =>
                string.Equals(x.DepartmentName, dept, StringComparison.OrdinalIgnoreCase));
            if (forDept is not null)
            {
                return forDept;
            }
        }

        return candidates
                   .Where(x => x.DefaultPcs is > 0 && x.DefaultQty is > 0)
                   .OrderByDescending(x => x.DeptItemId)
                   .FirstOrDefault()
               ?? candidates.OrderByDescending(x => x.DeptItemId).FirstOrDefault();
    }

    private void ApplySelectedItemDefaults()
    {
        var selected = GetRegisterItemDescriptionDisplayText(RegisterItemCombo);
        if (string.IsNullOrWhiteSpace(selected))
        {
            return;
        }

        var match = FindDepartmentItemDefaultsForDescription(selected);
        if (match is null) return;

        if (match.DefaultPcs is > 0)
        {
            RegisterPcsCombo.Text = match.DefaultPcs.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (match.DefaultQty is > 0)
        {
            RegisterQtyCombo.Text = match.DefaultQty.Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private async Task SafeRunAsync(Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            var msg = FormatUiException(ex);
            SetStatus(msg);
            MessageBox.Show(this, msg, "HSMS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string FormatUiException(Exception ex)
    {
        static string Deepest(Exception e)
        {
            var cur = e;
            while (cur.InnerException is not null) cur = cur.InnerException;
            return string.IsNullOrWhiteSpace(cur.Message) ? e.Message : cur.Message;
        }

        var inner = Deepest(ex).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (inner.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
        {
            return "Database schema is missing a required table. Run the latest scripts in hsms-db/ddl on the HSMS database, then try again. Detail: " +
                   (inner.Length > 220 ? inner[..220] + "…" : inner);
        }

        if (inner.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase))
        {
            return "Database schema is out of date for this app version. Run the latest scripts in hsms-db/ddl on the HSMS database, then try again. Detail: " +
                   (inner.Length > 220 ? inner[..220] + "…" : inner);
        }

        return string.IsNullOrWhiteSpace(inner) ? "Unexpected error." : inner;
    }

    private void SetStatus(string message)
    {
        StatusText.Text = $"{DateTime.Now:HH:mm:ss} — {message}";
    }

    private int GetSelectedSterilizerId()
    {
        var v = SterilizerCombo.SelectedValue;
        if (v is int i) return i;
        if (v is long l) return (int)l;
        if (v is not null && int.TryParse(v.ToString(), out var p)) return p;

        if (SterilizerCombo.ItemsSource is IEnumerable<SterilizerListItemDto> list)
        {
            var f = list.FirstOrDefault();
            if (f is not null) return f.SterilizerId;
        }

        return 1;
    }

    private void SelectSterilizationType(string? value)
    {
        if (IsLowTemperatureLabel(value ?? string.Empty))
        {
            SterTempLowRadio.IsChecked = true;
        }
        else
        {
            SterTempHighRadio.IsChecked = true;
        }
    }

    private static bool IsLowTemperatureLabel(string value) =>
        value.Contains("low", StringComparison.OrdinalIgnoreCase);

    /// <summary>Dialog title when save fails: clarifies invalid input vs other errors (e.g. concurrency).</summary>
    private static string SaveFailureDialogTitle(string message)
    {
        if (message.StartsWith("Cannot save", StringComparison.OrdinalIgnoreCase))
        {
            return "Cannot save — invalid input";
        }

        if (message.Contains("Press F5", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return "Cannot save";
        }

        return "Save failed";
    }

    private string GetSterilizationType() =>
        SterTempLowRadio.IsChecked == true ? TemperatureModeLow : TemperatureModeHigh;

    private void SelectCycleProgram(string? value)
    {
        var target = string.IsNullOrWhiteSpace(value) ? CycleProgramChoices[0] : value.Trim();
        foreach (var item in CycleProgramCombo.Items)
        {
            if (string.Equals(item?.ToString(), target, StringComparison.OrdinalIgnoreCase))
            {
                CycleProgramCombo.SelectedItem = item;
                return;
            }
        }

        CycleProgramCombo.SelectedIndex = 0;
    }

    private string GetCycleProgram() =>
        CycleProgramCombo.SelectedItem?.ToString()?.Trim() ?? CycleProgramChoices[0];

    private void SyncBiIndicatorUi()
    {
        var no = BiIndicatorNoRadio.IsChecked == true;
        BiResultCombo.IsEnabled = !no;
        BiResultCombo.Visibility = no ? Visibility.Collapsed : Visibility.Visible;

        // Implants choice is only relevant when BI is used (per workflow request).
        ImplantsOptionsPanel.Visibility = no ? Visibility.Collapsed : Visibility.Visible;
        BiLotPanel.Visibility = no ? Visibility.Collapsed : Visibility.Visible;
        if (no)
        {
            ImplantsNoRadio.IsChecked = true;
            BiLotNoTextBox.Text = "";
        }

        if (no)
        {
            SelectComboByText(BiResultCombo, "N/A", "N/A");
            return;
        }

        var cur = GetSelectedComboText(BiResultCombo);
        if (string.IsNullOrWhiteSpace(cur) || string.Equals(cur, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            SelectComboByText(BiResultCombo, "Pending", "Pending");
        }
    }

    private void ApplyBiResultToRadios(string? biResult)
    {
        var text = biResult?.Trim();
        if (string.IsNullOrWhiteSpace(text) || string.Equals(text, "N/A", StringComparison.OrdinalIgnoreCase))
        {
            BiIndicatorNoRadio.IsChecked = true;
        }
        else
        {
            BiIndicatorYesRadio.IsChecked = true;
            SelectComboByText(BiResultCombo, text, "Pending");
        }

        SyncBiIndicatorUi();
    }

    private void ApplyImplantsToRadios(bool implants)
    {
        if (implants)
        {
            ImplantsYesRadio.IsChecked = true;
        }
        else
        {
            ImplantsNoRadio.IsChecked = true;
        }
    }

    private bool GetImplantsForSave() => ImplantsYesRadio.IsChecked == true;

    private void SterTempHighRadio_OnChecked(object sender, RoutedEventArgs e) => SyncHighTempPresetUi();

    private void SterTempLowRadio_OnChecked(object sender, RoutedEventArgs e) => SyncHighTempPresetUi();

    private void SyncHighTempPresetUi()
    {
        if (HighTempPresetPanel is null)
        {
            return;
        }

        var isHigh = SterTempHighRadio.IsChecked == true;
        HighTempPresetPanel.Visibility = isHigh ? Visibility.Visible : Visibility.Collapsed;

        // Low temperature cycles do not use the steam presets; keep these fields empty.
        if (!isHigh)
        {
            TemperatureTextBox.Text = "";
            ExposureMinutesTextBox.Text = "";
        }
    }

    private void Preset134Button_OnClick(object sender, RoutedEventArgs e) => ApplyTempExposurePreset(134, 4);

    private void Preset121Button_OnClick(object sender, RoutedEventArgs e) => ApplyTempExposurePreset(121, 20);

    private void ApplyTempExposurePreset(int tempC, int exposureMin)
    {
        TemperatureTextBox.Text = tempC.ToString(CultureInfo.InvariantCulture);
        ExposureMinutesTextBox.Text = exposureMin.ToString(CultureInfo.InvariantCulture);
        ExposureMinutesTextBox.CaretIndex = ExposureMinutesTextBox.Text.Length;
        ExposureMinutesTextBox.Focus();
    }

    private DateTime CycleDateTimeUtcForSave()
    {
        var nowAbu = HsmsDeploymentTimeZone.NowInDeploymentZone();
        var calendarDay = (CycleEntryDatePicker.SelectedDate ?? nowAbu.Date).Date;
        var wallClock = calendarDay.Add(nowAbu.TimeOfDay);
        return HsmsDeploymentTimeZone.DeploymentWallToUtc(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified));
    }

    private string? GetBiResultForSave() =>
        BiIndicatorNoRadio.IsChecked == true ? "N/A" : GetSelectedComboText(BiResultCombo, "Pending");

    private async Task HandleCycleNoEnterAsync()
    {
        // Cycle number is auto-assigned now.
        await EnsureCycleNoAsync();

        var cycleNo = CycleNoTextBox.Text.Trim();
        var (existing, err) = await _data.SearchCyclesAsync(cycleNo);
        if (err is not null)
        {
            SetStatus(err);
            MessageBox.Show(this, err, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var match = existing.FirstOrDefault(x => x.CycleNo.Equals(cycleNo, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            var (detail, loadErr) = await _data.GetCycleAsync(match.SterilizationId);
            if (loadErr is not null)
            {
                SetStatus(loadErr);
                MessageBox.Show(this, loadErr, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (detail is not null)
            {
                _currentSterilizationId = detail.SterilizationId;
                _currentRowVersion = detail.RowVersion;
                ApplyRegisterLoadEditMode(detail.CreatedByAccountId);
                SelectSterilizationType(detail.SterilizationType);
                OperatorTextBox.Text = detail.OperatorName;
                SterilizerCombo.SelectedValue = detail.SterilizerId;
                CycleEntryDatePicker.SelectedDate = HsmsDeploymentTimeZone.UtcToDeployment(detail.CycleDateTimeUtc).Date;
                SelectCycleProgram(detail.CycleProgram);
                TemperatureTextBox.Text = detail.TemperatureC?.ToString("0.##", CultureInfo.InvariantCulture) ?? string.Empty;
                ExposureMinutesTextBox.Text = detail.ExposureTimeMinutes?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                _loadedCyclePressure = detail.Pressure;
                NotesTextBox.Text = detail.Notes ?? string.Empty;
                ApplyImplantsToRadios(detail.Implants);
                SelectDoctorRoom(detail.DoctorRoomId);
                SelectComboByText(CycleStatusCombo, detail.CycleStatus, "Draft");
                ApplyBiResultToRadios(detail.BiResult);
                BiLotNoTextBox.Text = detail.BiLotNo ?? string.Empty;
                RegisterPendingItems.Clear();
                foreach (var it in detail.Items)
                {
                    RegisterPendingItems.Add(new RegisterPendingItemLine
                    {
                        ItemName = it.ItemName ?? "",
                        Pcs = it.Pcs < 1 ? 1 : it.Pcs,
                        Qty = it.Qty < 1 ? 1 : it.Qty,
                        DepartmentName = it.DepartmentName,
                        DoctorOrRoom = it.DoctorOrRoom
                    });
                }

                RegisterItemCombo.SelectedIndex = -1;
                RegisterItemCombo.Text = "";
                RegisterPcsCombo.Text = "1";
                RegisterQtyCombo.Text = "1";
                var firstDept = detail.Items.FirstOrDefault()?.DepartmentName;
                DepartmentCombo.Text = string.IsNullOrWhiteSpace(firstDept) ? "" : firstDept;

                SetStatus($"Loaded cycle {detail.CycleNo}.");
            }
        }
        else
        {
            _currentSterilizationId = null;
            _currentRowVersion = null;
            _loadedCyclePressure = null;
            ApplyRegisterLoadEditMode(createdByAccountId: null, isNewRecord: true);
            CycleEntryDatePicker.SelectedDate = HsmsDeploymentTimeZone.NowInDeploymentZone().Date;
            SterTempHighRadio.IsChecked = true;
            SelectCycleProgram(null);
            TemperatureTextBox.Text = string.Empty;
            ExposureMinutesTextBox.Text = string.Empty;
            NotesTextBox.Text = string.Empty;
            ImplantsNoRadio.IsChecked = true;
            DoctorRoomCombo.SelectedIndex = -1;
            DepartmentCombo.Text = "";
            SelectComboByText(CycleStatusCombo, "Draft", "Draft");
            BiIndicatorYesRadio.IsChecked = true;
            SelectComboByText(BiResultCombo, "Pending", "Pending");
            SyncBiIndicatorUi();
            RegisterPendingItems.Clear();
            RegisterItemCombo.SelectedIndex = -1;
            RegisterItemCombo.Text = "";
            RegisterPcsCombo.Text = "1";
            RegisterQtyCombo.Text = "1";
            BiLotNoTextBox.Text = "";
            SetStatus($"New cycle '{cycleNo}' — choose sterilizer and type, add lines, then Save.");
        }

        SterilizerCombo.Focus();
    }

    private void ApplyRegisterLoadEditMode(int? createdByAccountId, bool isNewRecord = false)
    {
        var canEdit = isNewRecord || CanEditSterilizationRow(createdByAccountId);

        // Keep navigation + print/refresh usable, but lock the data entry panels + Save.
        RegisterLoadView.IsEnabled = canEdit;
        RegisterLoadStatusDoctorNotesView.IsEnabled = canEdit;
        RegisterLoadItemInputView.IsEnabled = canEdit;
        SaveButton.IsEnabled = canEdit;

        if (!canEdit)
        {
            SetStatus("View-only: you can only edit records that you created (unless you are an administrator).");
        }
    }

    private void RegisterPendingItemRemove_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: RegisterPendingItemLine line })
        {
            RegisterPendingItems.Remove(line);
        }
    }

    private void RegisterAddItemLineButton_OnClick(object sender, RoutedEventArgs e)
    {
        var itemName = string.IsNullOrWhiteSpace(RegisterItemCombo.Text)
            ? RegisterItemCombo.SelectedItem?.ToString()
            : RegisterItemCombo.Text;
        if (string.IsNullOrWhiteSpace(itemName))
        {
            HsmsAlertWindow.ShowInfo(this, "Enter an item description, then click Add to list.", "Missing field");
            RegisterItemCombo.Focus();
            return;
        }

        if (!int.TryParse(RegisterPcsCombo.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pcs) || pcs < 1)
        {
            HsmsAlertWindow.ShowInfo(this, "Pcs must be a number ≥ 1.", "Invalid value");
            RegisterPcsCombo.Focus();
            return;
        }

        if (!int.TryParse(RegisterQtyCombo.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty < 1)
        {
            HsmsAlertWindow.ShowInfo(this, "Qty must be a number ≥ 1.", "Invalid value");
            RegisterQtyCombo.Focus();
            return;
        }

        var dept = string.IsNullOrWhiteSpace(DepartmentCombo.Text) ? null : DepartmentCombo.Text.Trim();
        var doctorRaw = string.IsNullOrWhiteSpace(DoctorRoomCombo.Text)
            ? DoctorRoomCombo.SelectedItem?.ToString()
            : DoctorRoomCombo.Text;
        var doctorName = string.IsNullOrWhiteSpace(doctorRaw) ? null : ExtractDoctorName(doctorRaw);
        var trimmedName = itemName.Trim();

        var duplicate = RegisterPendingItems.FirstOrDefault(x =>
            string.Equals((x.ItemName ?? "").Trim(), trimmedName, StringComparison.OrdinalIgnoreCase));
        if (duplicate is not null)
        {
            duplicate.Pcs = pcs;
            duplicate.Qty = qty;
            duplicate.DepartmentName = dept;
            duplicate.DoctorOrRoom = string.IsNullOrWhiteSpace(doctorName) ? null : doctorName.Trim();

            RegisterItemCombo.SelectedIndex = -1;
            RegisterItemCombo.Text = "";
            RegisterPcsCombo.Text = "1";
            RegisterQtyCombo.Text = "1";
            RegisterItemCombo.Focus();
            SetStatus($"Updated line for '{trimmedName}' — PCS/QTY kept from your entry ({RegisterPendingItems.Count} on this load).");
            return;
        }

        RegisterPendingItems.Add(new RegisterPendingItemLine
        {
            ItemName = trimmedName,
            Pcs = pcs,
            Qty = qty,
            DepartmentName = dept,
            DoctorOrRoom = string.IsNullOrWhiteSpace(doctorName) ? null : doctorName.Trim()
        });

        RegisterItemCombo.SelectedIndex = -1;
        RegisterItemCombo.Text = "";
        RegisterPcsCombo.Text = "1";
        RegisterQtyCombo.Text = "1";
        RegisterItemCombo.Focus();
        SetStatus($"Added line ({RegisterPendingItems.Count} on this load).");
    }

    private (List<SterilizationItemDto>? items, string? errorMessage, UIElement? focusTarget) BuildSterilizationItemDtosForSave()
    {
        if (RegisterPendingItems.Count > 0)
        {
            var list = new List<SterilizationItemDto>();
            for (var i = 0; i < RegisterPendingItems.Count; i++)
            {
                var l = RegisterPendingItems[i];
                if (string.IsNullOrWhiteSpace(l.ItemName))
                {
                    return (null, $"Line {i + 1}: Item description is required.", RegisterPendingItemsGrid);
                }

                if (l.Pcs < 1)
                {
                    return (null, $"Line {i + 1}: Pcs must be a number ≥ 1.", RegisterPendingItemsGrid);
                }

                if (l.Qty < 1)
                {
                    return (null, $"Line {i + 1}: Qty must be a number ≥ 1.", RegisterPendingItemsGrid);
                }

                list.Add(new SterilizationItemDto
                {
                    ItemName = l.ItemName.Trim(),
                    DepartmentName = string.IsNullOrWhiteSpace(l.DepartmentName) ? null : l.DepartmentName.Trim(),
                    DoctorOrRoom = string.IsNullOrWhiteSpace(l.DoctorOrRoom) ? null : l.DoctorOrRoom.Trim(),
                    Pcs = l.Pcs,
                    Qty = l.Qty
                });
            }

            return (list, null, null);
        }

        var itemName = string.IsNullOrWhiteSpace(RegisterItemCombo.Text)
            ? RegisterItemCombo.SelectedItem?.ToString()
            : RegisterItemCombo.Text;
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return (null, "Item Description is required.", RegisterItemCombo);
        }

        if (!int.TryParse(RegisterPcsCombo.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pcs) || pcs < 1)
        {
            return (null, "Pcs must be a number ≥ 1.", RegisterPcsCombo);
        }

        if (!int.TryParse(RegisterQtyCombo.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var qty) || qty < 1)
        {
            return (null, "Qty must be a number ≥ 1.", RegisterQtyCombo);
        }

        var deptText = string.IsNullOrWhiteSpace(DepartmentCombo.Text) ? null : DepartmentCombo.Text.Trim();
        var doctorText = string.IsNullOrWhiteSpace(DoctorRoomCombo.Text)
            ? DoctorRoomCombo.SelectedItem?.ToString()
            : DoctorRoomCombo.Text;
        doctorText = string.IsNullOrWhiteSpace(doctorText) ? null : ExtractDoctorName(doctorText);

        return (new List<SterilizationItemDto>
        {
            new()
            {
                ItemName = itemName.Trim(),
                DepartmentName = deptText,
                DoctorOrRoom = string.IsNullOrWhiteSpace(doctorText) ? null : doctorText.Trim(),
                Pcs = pcs,
                Qty = qty
            }
        }, null, null);
    }

    private async Task SaveCycleAsync()
    {
        await EnsureCycleNoAsync();

        if (BiIndicatorYesRadio.IsChecked == true && string.IsNullOrWhiteSpace(BiLotNoTextBox.Text))
        {
            HsmsAlertWindow.ShowInfo(this, "BI Lot# is required when Biological indicator is Yes.", "Missing field");
            BiLotNoTextBox.Focus();
            return;
        }

        var (itemDtos, itemsErr, itemsFocus) = BuildSterilizationItemDtosForSave();
        if (itemsErr is not null || itemDtos is null)
        {
            HsmsAlertWindow.ShowInfo(this, itemsErr ?? "Add at least one item line.", "Missing field");
            (itemsFocus ?? RegisterItemCombo).Focus();
            return;
        }

        var payload = new SterilizationUpsertDto
        {
            RowVersion = _currentRowVersion,
            CycleNo = CycleNoTextBox.Text.Trim(),
            SterilizationType = GetSterilizationType(),
            CycleProgram = GetCycleProgram(),
            OperatorName = string.IsNullOrWhiteSpace(OperatorTextBox.Text) ? _signedInUsername : OperatorTextBox.Text.Trim(),
            CycleDateTimeUtc = CycleDateTimeUtcForSave(),
            SterilizerId = GetSelectedSterilizerId(),
            TemperatureC = ParseNullableDecimal(TemperatureTextBox.Text),
            Pressure = _currentSterilizationId is null ? null : _loadedCyclePressure,
            ExposureTimeMinutes = ParseNullableInt(ExposureMinutesTextBox.Text),
            BiLotNo = string.IsNullOrWhiteSpace(BiLotNoTextBox.Text) ? null : BiLotNoTextBox.Text.Trim(),
            BiResult = GetBiResultForSave(),
            CycleStatus = GetSelectedComboText(CycleStatusCombo, "Draft") ?? "Draft",
            DoctorRoomId = GetSelectedDoctorRoomId(),
            Implants = GetImplantsForSave(),
            Notes = string.IsNullOrWhiteSpace(NotesTextBox.Text) ? null : NotesTextBox.Text.Trim(),
            Items = itemDtos,
            ClientMachine = Environment.MachineName
        };

        if (SterilizationUpsertValidator.Validate(payload) is { } validationMessage)
        {
            SetStatus(validationMessage);
            HsmsAlertWindow.ShowWarning(this, validationMessage, "Cannot save — invalid input");
            if (validationMessage.Contains("Temperature", StringComparison.OrdinalIgnoreCase))
            {
                TemperatureTextBox.Focus();
            }

            return;
        }

        if (_currentSterilizationId is { } sid)
        {
            var (ok, saveErr) = await _data.UpdateCycleAsync(sid, payload);
            if (!ok)
            {
                var err = saveErr ?? "Save failed.";
                SetStatus(err);
                HsmsAlertWindow.ShowWarning(this, err, SaveFailureDialogTitle(err));
                return;
            }

            SetStatus("Record updated.");
            HsmsAlertWindow.ShowSuccess(this, "Record updated successfully.");
        }
        else
        {
            var (ok, saveErr) = await _data.CreateCycleAsync(payload);
            if (!ok)
            {
                var err = saveErr ?? "Save failed.";
                // If another client created the same auto cycle no, retry once with a fresh number.
                if (err.Contains("Cycle number already exists", StringComparison.OrdinalIgnoreCase))
                {
                    CycleNoTextBox.Text = "";
                    await EnsureCycleNoAsync();
                    payload.CycleNo = CycleNoTextBox.Text.Trim();
                    var (ok2, err2) = await _data.CreateCycleAsync(payload);
                    if (ok2)
                    {
                        SetStatus("Record saved.");
                        HsmsAlertWindow.ShowSuccess(this, "Record saved successfully.");
                        return;
                    }

                    err = err2 ?? err;
                }
                SetStatus(err);
                HsmsAlertWindow.ShowWarning(this, err, SaveFailureDialogTitle(err));
                return;
            }

            SetStatus("Record created.");
            HsmsAlertWindow.ShowSuccess(this, "Record saved successfully.");
        }

        foreach (var dto in itemDtos)
        {
            await EnsureDepartmentOptionAsync(dto.DepartmentName);
        }

        foreach (var dto in itemDtos)
        {
            await EnsureDoctorRoomOptionAsync(dto.DoctorOrRoom, dto.DepartmentName);
        }

        foreach (var dto in itemDtos)
        {
            await EnsureItemDescriptionOptionAsync(dto.DepartmentName, dto.ItemName, dto.Pcs, dto.Qty);
        }

        ClearRegisterLoadForm();
    }

    private async Task EnsureDepartmentOptionAsync(string? departmentName)
    {
        if (string.IsNullOrWhiteSpace(departmentName)) return;

        var exists = DepartmentOptions.Any(x =>
            string.Equals(x?.Trim(), departmentName, StringComparison.OrdinalIgnoreCase));

        if (exists) return;

        // Only add to master list after a successful save.
        var (created, err) = await _data.CreateDepartmentAsync(new DepartmentUpsertDto
        {
            DepartmentName = departmentName
        });

        if (err is not null)
        {
            // If it already exists in the DB (unique constraint), just refresh the list.
            if (err.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                var (departments2, depErr2) = await _data.GetDepartmentsAsync();
                if (depErr2 is null)
                {
                    DepartmentOptions.Clear();
                    foreach (var d in departments2
                                 .Select(x => x.DepartmentName)
                                 .Where(x => !string.IsNullOrWhiteSpace(x))
                                 .Distinct(StringComparer.OrdinalIgnoreCase)
                                 .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    {
                        DepartmentOptions.Add(d);
                    }
                }
                return;
            }

            SetStatus($"Saved, but couldn't add new department '{departmentName}' to options ({err}).");
            return;
        }

        // Refresh options so the new one is selectable immediately.
        var (departments, depErr) = await _data.GetDepartmentsAsync();
        if (depErr is not null)
        {
            // Fall back to just appending the new string.
            DepartmentOptions.Add(departmentName);
            return;
        }

        DepartmentOptions.Clear();
        foreach (var d in departments
                     .Select(x => x.DepartmentName)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            DepartmentOptions.Add(d);
        }
    }

    private async Task EnsureDoctorRoomOptionAsync(string? doctorOrRoomDisplay, string? doctorLinkedDepartmentName = null)
    {
        if (string.IsNullOrWhiteSpace(doctorOrRoomDisplay)) return;

        var doctorName = ExtractDoctorName(doctorOrRoomDisplay);
        var exists = _doctorRooms.Any(x =>
            string.Equals(x.DoctorName?.Trim(), doctorName, StringComparison.OrdinalIgnoreCase));
        if (exists) return;

        var deptForRoom = string.IsNullOrWhiteSpace(doctorLinkedDepartmentName)
            ? (string.IsNullOrWhiteSpace(DepartmentCombo.Text) ? null : DepartmentCombo.Text.Trim())
            : doctorLinkedDepartmentName.Trim();

        var (created, err) = await _data.CreateDoctorRoomAsync(new DoctorRoomUpsertDto
        {
            DoctorName = doctorName,
            // NOTE: In Admin Panel "Doctors/Departments", the Department column is persisted in DoctorRoom.Room.
            Room = string.IsNullOrWhiteSpace(deptForRoom) ? null : deptForRoom
        });

        if (err is not null)
        {
            SetStatus($"Saved, but couldn't add new doctor/room '{doctorOrRoomDisplay}' to options ({err}).");
            return;
        }

        var (doctorRooms, docErr) = await _data.GetDoctorRoomsAsync();
        if (docErr is not null)
        {
            DoctorRoomOptions.Add(doctorName.Trim());
            return;
        }

        _doctorRooms.Clear();
        _doctorRooms.AddRange(doctorRooms);

        DoctorRoomOptions.Clear();
        foreach (var d in doctorRooms
                     .Select(x => x.DoctorName)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            DoctorRoomOptions.Add(d);
        }
    }

    private void SyncDepartmentFromDoctorSelection()
    {
        var raw = string.IsNullOrWhiteSpace(DoctorRoomCombo.Text)
            ? DoctorRoomCombo.SelectedItem?.ToString()
            : DoctorRoomCombo.Text;
        if (string.IsNullOrWhiteSpace(raw)) return;

        var doctorName = ExtractDoctorName(raw);
        if (string.IsNullOrWhiteSpace(doctorName)) return;

        var match = _doctorRooms.FirstOrDefault(x =>
            x.IsActive &&
            string.Equals(x.DoctorName, doctorName, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(x.Room));

        if (match is null) return;

        // Room holds "Department" for this master table (see Admin Panel tab label).
        DepartmentCombo.Text = match.Room!.Trim();
    }

    private static string ExtractDoctorName(string raw)
    {
        // Allow either "Doctor" or "Doctor / Dept" (DisplayName format).
        var s = raw.Trim();
        var slash = s.IndexOf('/');
        return slash >= 0 ? s[..slash].Trim() : s;
    }

    private async Task EnsureItemDescriptionOptionAsync(string? departmentName, string? itemName, int pcs, int qty,
        bool updateExistingMasterDefaults = true)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return;

        var trimmedItem = itemName.Trim();
        var depName = (departmentName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(depName))
        {
            // If the user didn't choose a department, still try to persist into Dept Items using their profile department.
            depName = (_session.Profile?.Department ?? "").Trim();
        }

        // Always keep the dropdown option list friendly (even if master insert fails).
        if (!ItemDescriptionOptions.Any(x => string.Equals(x?.Trim(), trimmedItem, StringComparison.OrdinalIgnoreCase)))
        {
            ItemDescriptionOptions.Add(trimmedItem);
        }

        // If we still don't have a department, we cannot persist to Dept Items (it requires DepartmentId).
        if (string.IsNullOrWhiteSpace(depName))
        {
            return;
        }

        pcs = Math.Max(1, pcs);
        qty = Math.Max(1, qty);

        // Ensure department exists first.
        await EnsureDepartmentOptionAsync(depName);

        var (departments, depErr) = await _data.GetDepartmentsAsync();
        if (depErr is not null)
        {
            SetStatus($"Saved, but couldn't refresh departments to add item '{trimmedItem}' ({depErr}).");
            ItemDescriptionOptions.Add(trimmedItem);
            return;
        }

        var depId = departments.FirstOrDefault(x =>
            string.Equals(x.DepartmentName, depName, StringComparison.OrdinalIgnoreCase))?.DepartmentId;

        if (depId is null or <= 0)
        {
            return;
        }

        // If it already exists for this department, update defaults so Admin Panel shows the pcs/qty you used.
        // If it doesn't exist, create it.
        var existing = _departmentItems.FirstOrDefault(x =>
            string.Equals(x.DepartmentName, depName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ItemName, trimmedItem, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            // Instrument Check and similar flows must not clobber PCS/QTY saved from Register Load / Maintenance.
            if (updateExistingMasterDefaults)
            {
                var shouldUpdate = existing.DefaultPcs != pcs || existing.DefaultQty != qty;
                if (shouldUpdate)
                {
                    var updateErr = await _data.UpdateDepartmentItemAsync(existing.DeptItemId, new DepartmentItemUpsertDto
                    {
                        DepartmentId = depId.Value,
                        ItemName = trimmedItem,
                        DefaultPcs = pcs,
                        DefaultQty = qty
                    });

                    if (updateErr is not null && !updateErr.Contains("not found", StringComparison.OrdinalIgnoreCase))
                    {
                        SetStatus($"Saved, but couldn't update item '{trimmedItem}' defaults in master ({updateErr}).");
                    }
                }
            }
        }
        else
        {
            var (_, err) = await _data.CreateDepartmentItemAsync(new DepartmentItemUpsertDto
            {
                DepartmentId = depId.Value,
                ItemName = trimmedItem,
                DefaultPcs = pcs,
                DefaultQty = qty
            });

            if (err is not null && !err.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"Saved, but couldn't add new item '{trimmedItem}' to master ({err}).");
                return;
            }
        }

        // Refresh Department Items so defaults and dropdown are consistent.
        var (departmentItems, itemErr) = await _data.GetDepartmentItemsAsync();
        if (itemErr is null)
        {
            _departmentItems.Clear();
            _departmentItems.AddRange(departmentItems);
        }

        ItemDescriptionOptions.Clear();
        foreach (var name in _departmentItems
                     .Select(x => x.ItemName)
                     .Where(x => !string.IsNullOrWhiteSpace(x))
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x))
        {
            ItemDescriptionOptions.Add(name);
        }
    }

    private async Task RefreshCycleAsync()
    {
        await LoadSterilizersAsync();
        await LoadDepartmentAndDoctorOptionsAsync();
        await HandleCycleNoEnterAsync();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            e.Handled = true;
            if (HomeView.Visibility == Visibility.Visible)
            {
                _ = SafeRunAsync(RefreshHomeDashboardAsync);
            }
            else
            {
                _ = SafeRunAsync(RefreshCycleAsync);
            }

            return;
        }

        if (e.Key == Key.F9)
        {
            e.Handled = true;
            _ = SafeRunAsync(QuickPrintCurrentScreenAsync);
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            SetStatus("Tip: Esc does not close HSMS (avoids accidents). Use Log out when you are finished.");
            return;
        }

        if (e.Key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            _ = SafeRunAsync(SaveCycleAsync);
        }
    }

    private static decimal? ParseNullableDecimal(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static int? ParseNullableInt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private void ClearRegisterLoadForm()
    {
        _currentSterilizationId = null;
        _currentRowVersion = null;
        _loadedCyclePressure = null;

        CycleNoTextBox.Text = "";
        CycleEntryDatePicker.SelectedDate = HsmsDeploymentTimeZone.NowInDeploymentZone().Date;

        SterTempHighRadio.IsChecked = true;
        SelectCycleProgram(null);

        OperatorTextBox.Text = GetSignedInDisplayName();

        TemperatureTextBox.Text = "";
        ExposureMinutesTextBox.Text = "";

        BiIndicatorYesRadio.IsChecked = true;
        SelectComboByText(BiResultCombo, "Pending", "Pending");
        BiLotNoTextBox.Text = "";
        SyncBiIndicatorUi();

        ImplantsNoRadio.IsChecked = true;
        DoctorRoomCombo.SelectedIndex = -1;
        DepartmentCombo.Text = "";
        SelectComboByText(CycleStatusCombo, "Draft", "Draft");
        NotesTextBox.Text = "";

        RegisterPendingItems.Clear();
        RegisterItemCombo.SelectedIndex = -1;
        RegisterItemCombo.Text = "";
        RegisterPcsCombo.Text = "1";
        RegisterQtyCombo.Text = "1";

        SetStatus("Ready — new cycle entry.");
        _ = SafeRunAsync(EnsureCycleNoAsync);
        SterilizerCombo.Focus();
    }

    private string GetSignedInDisplayName()
    {
        var first = (_session.Profile?.FirstName ?? "").Trim();
        var last = (_session.Profile?.LastName ?? "").Trim();
        var full = $"{first} {last}".Trim();
        return string.IsNullOrWhiteSpace(full) ? _signedInUsername : full;
    }

    private static string? GetSelectedComboText(ComboBox combo, string? fallback = null)
    {
        if (combo.SelectedItem is ComboBoxItem cbi)
        {
            return cbi.Content?.ToString();
        }

        var txt = combo.SelectedItem?.ToString();
        return string.IsNullOrWhiteSpace(txt) ? fallback : txt.Trim();
    }

    private static void SelectComboByText(ComboBox combo, string? value, string fallback)
    {
        var target = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        foreach (var item in combo.Items)
        {
            var text = item is ComboBoxItem cbi ? cbi.Content?.ToString() : item?.ToString();
            if (string.Equals(text, target, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        foreach (var item in combo.Items)
        {
            var text = item is ComboBoxItem cbi ? cbi.Content?.ToString() : item?.ToString();
            if (string.Equals(text, fallback, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private int? GetSelectedDoctorRoomId()
    {
        var selectedDisplay = string.IsNullOrWhiteSpace(DoctorRoomCombo.Text)
            ? DoctorRoomCombo.SelectedItem?.ToString()
            : DoctorRoomCombo.Text;
        if (string.IsNullOrWhiteSpace(selectedDisplay))
        {
            return null;
        }

        var doctorName = ExtractDoctorName(selectedDisplay);
        return _doctorRooms.FirstOrDefault(x =>
                string.Equals(x.DoctorName, doctorName, StringComparison.OrdinalIgnoreCase))
            ?.DoctorRoomId;
    }

    private void SelectDoctorRoom(int? doctorRoomId)
    {
        if (doctorRoomId is null)
        {
            DoctorRoomCombo.SelectedIndex = -1;
            return;
        }

        var match = _doctorRooms.FirstOrDefault(x => x.DoctorRoomId == doctorRoomId.Value);
        DoctorRoomCombo.SelectedItem = match?.DoctorName;
    }

    private void MoveFocusOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is ComboBox combo && combo.IsDropDownOpen) return;
        if (sender is not UIElement current) return;

        e.Handled = true;
        current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    // Register Load quick-entry navigation is implemented via QuickEntryNavigationBehavior (attached behavior),
    // so we don't keep per-control handlers here anymore.

    // (Removed) line-item grid keyboard handling — Load Records is list-only now.

    // No multi-row line-items editing in this version.

    private void RowOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CycleItemRow row) return;

        if (e.PropertyName == nameof(CycleItemRow.Department))
        {
            RefreshRowItemOptions(row);
            if (!row.ItemOptions.Contains(row.ItemName))
            {
                row.ItemName = string.Empty;
            }
            return;
        }

        if (e.PropertyName == nameof(CycleItemRow.ItemName))
        {
            var match = _departmentItems.FirstOrDefault(x =>
                string.Equals(x.DepartmentName, row.Department, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ItemName, row.ItemName, StringComparison.OrdinalIgnoreCase));
            if (match?.DefaultPcs is > 0)
            {
                row.Pcs = match.DefaultPcs.Value;
            }

            if (match?.DefaultQty is > 0)
            {
                row.Qty = match.DefaultQty.Value;
            }
        }
    }

    private void RefreshRowItemOptions(CycleItemRow row)
    {
        row.ItemOptions.Clear();
        if (string.IsNullOrWhiteSpace(row.Department))
        {
            return;
        }

        foreach (var item in _departmentItems
                     .Where(x => string.Equals(x.DepartmentName, row.Department, StringComparison.OrdinalIgnoreCase))
                     .Select(x => x.ItemName)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(x => x))
        {
            row.ItemOptions.Add(item);
        }
    }
}
