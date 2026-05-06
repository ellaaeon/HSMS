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
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using HSMS.Application.Services;
using HSMS.Desktop.Controls;
using HSMS.Desktop.Models;
using HSMS.Desktop.Ui;
using HSMS.Shared.Contracts;
using HSMS.Shared.Time;

namespace HSMS.Desktop;

public partial class MainWindow : Window
{
    private static string? _biLogStickyInitials;
    /// <summary>Click position normalized to <see cref="DataGridCell"/> size (display mode); used after swap to edit template.</summary>
    private double? _biLogMergeNx;
    private double? _biLogMergeNy;
    private BiLogSheetUpdatePayload? _biLogEditSnapshot;
    private readonly SemaphoreSlim _biLogResultSaveMutex = new(1, 1);

    private static bool _schemaWarningShown;
    private const string TemperatureModeHigh = "High temperature";
    private const string TemperatureModeLow = "Low temperature";
    private static readonly string[] CycleProgramChoices = ["Instruments", "Bowie Dick", "Leak test", "Warm up"];
    private static readonly string[] BiLogTemperatureFilters = ["(All)", TemperatureModeHigh, TemperatureModeLow];
    private static readonly string[] HourChoices = ["(Any)", "00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12", "13", "14", "15", "16", "17", "18", "19", "20", "21", "22", "23"];

    private readonly ObservableCollection<CycleItemRow> _items = [];
    private readonly ObservableCollection<SterilizationSearchItemDto> _loadRecords = [];
    private readonly ObservableCollection<SterilizationSearchItemDto> _homeRecentCycles = [];
    private readonly ObservableCollection<BiLogSheetRowDto> _biRows = [];
    public ObservableCollection<string> ItemDescriptionOptions { get; } = [];
    private readonly List<DepartmentItemListItemDto> _departmentItems = [];
    private readonly List<DoctorRoomListItemDto> _doctorRooms = [];
    private readonly IHsmsDataService _data;
    private readonly HsmsAuthService _auth;
    private readonly LoginResponseDto _session;
    private readonly string _signedInUsername;
    private int? _currentSterilizationId;
    private string? _currentRowVersion;
    /// <summary>Pressure is not edited on Register load; on update we send this so the DB value is not cleared.</summary>
    private decimal? _loadedCyclePressure;

    /// <summary>Set when user confirms log out to return to the staff portal (sign-in / create account).</summary>
    public bool ReturnToPortal { get; private set; }
    public ObservableCollection<string> DepartmentOptions { get; } = [];
    public ObservableCollection<string> DoctorRoomOptions { get; } = [];

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
            tinInit = afterRaw.BiTimeInUtc is null ? null : initials;
            row.BiTimeInInitials = tinInit;
        }

        if (timeOutChanged)
        {
            toutInit = afterRaw.BiTimeOutUtc is null ? null : initials;
            row.BiTimeOutInitials = toutInit;
        }

        return afterRaw with
        {
            BiTimeInInitials = tinInit,
            BiTimeOutInitials = toutInit
        };
    }

    public MainWindow(IHsmsDataService dataService, LoginResponseDto session, HsmsAuthService authService)
    {
        _data = dataService;
        _auth = authService;
        _session = session;
        _signedInUsername = session.Username;
        InitializeComponent();
        DataContext = this;
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
        HomeRecentCyclesGrid.ItemsSource = _homeRecentCycles;
        LoadRecordsRefreshButton.Click += async (_, _) => await SafeRunAsync(RefreshLoadRecordsAsync);

        SaveButton.Click += async (_, _) => await SafeRunAsync(SaveCycleAsync);
        RefreshButton.Click += async (_, _) => await SafeRunAsync(RefreshCycleAsync);
        PrintButton.Click += (_, _) => ShowPrintPlaceholder();

        SterTempHighRadio.KeyDown += MoveFocusOnEnter;
        SterTempLowRadio.KeyDown += MoveFocusOnEnter;
        CycleEntryDatePicker.KeyDown += MoveFocusOnEnter;
        CycleProgramCombo.KeyDown += MoveFocusOnEnter;
        SterilizerCombo.KeyDown += MoveFocusOnEnter;
        BiIndicatorYesRadio.KeyDown += MoveFocusOnEnter;
        BiIndicatorNoRadio.KeyDown += MoveFocusOnEnter;
        ImplantsYesRadio.KeyDown += MoveFocusOnEnter;
        ImplantsNoRadio.KeyDown += MoveFocusOnEnter;
        AttachRegisterLoadTextNav(OperatorTextBox);
        AttachRegisterLoadTextNav(TemperatureTextBox);
        BiResultCombo.KeyDown += MoveFocusOnEnter;
        AttachRegisterLoadTextNav(BiLotNoTextBox);
        CycleStatusCombo.KeyDown += MoveFocusOnEnter;
        AttachRegisterLoadComboNav(DoctorRoomCombo);
        AttachRegisterLoadComboNav(DepartmentCombo);
        AttachRegisterLoadTextNav(NotesTextBox);
        AttachRegisterLoadComboNav(RegisterItemCombo);
        AttachRegisterLoadComboNav(RegisterPcsCombo);
        AttachRegisterLoadComboNav(RegisterQtyCombo);
        RegisterItemCombo.SelectionChanged += (_, _) => ApplySelectedItemDefaults();
        DoctorRoomCombo.SelectionChanged += (_, _) => SyncDepartmentFromDoctorSelection();
        DoctorRoomCombo.LostKeyboardFocus += (_, _) => SyncDepartmentFromDoctorSelection();
        // no item grid in Load Records

        BiLogGrid.ItemsSource = _biRows;
        _biRows.CollectionChanged += (_, _) => UpdateBiLogGridHeight();
        foreach (var t in BiLogTemperatureFilters)
        {
            BiTypeCombo.Items.Add(t);
        }

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
            await SafeRunAsync(LoadBiLogSheetAsync);
        };
        BiPrintButton.Click += (_, _) => PrintBiLogSheet();
        BiExportButton.Click += (_, _) => ExportBiLogSheetCsv();
    }

    private void UpdateBiLogGridHeight()
    {
        var grid = BiLogGrid;
        if (grid is null)
        {
            return;
        }

        // We want the page (outer ScrollViewer) to scroll, not the DataGrid.
        // So we size the DataGrid to fit all rows.
        var header = double.IsNaN(grid.ColumnHeaderHeight) || grid.ColumnHeaderHeight <= 0 ? 42 : grid.ColumnHeaderHeight;
        var row = grid.RowHeight <= 0 ? 30 : grid.RowHeight;
        var count = grid.Items.Count;

        var border = grid.BorderThickness.Top + grid.BorderThickness.Bottom;
        var chrome = 8; // small buffer for internal padding/lines
        var target = header + (count * row) + border + chrome;

        // Avoid tiny heights when empty.
        grid.Height = Math.Max(target, 200);
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
        // Default to showing the load the user is currently working on (prevents a huge list).
        if (!string.IsNullOrWhiteSpace(CycleNoTextBox.Text))
        {
            LoadRecordsSearchBox.Text = CycleNoTextBox.Text.Trim();
        }
        _ = SafeRunAsync(RefreshLoadRecordsAsync);
    }

    private void SetStyleNav(Button active)
    {
        var buttons = new List<Button>
        {
            HomeButton,
            RegisterLoadButton,
            InstrumentsCheckButton,
            LoadRecordsButton,
            BiLogSheetsButton,
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
        var win = new MaintenanceWindow(_data) { Owner = this };
        win.ShowDialog();
        await SafeRunAsync(LoadSterilizersAsync);
        await SafeRunAsync(LoadDepartmentAndDoctorOptionsAsync);
    }

    private void ShowHome()
    {
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Visible;

        RegisterLoadView.Visibility = Visibility.Collapsed;
        RegisterLoadQuickHelp.Visibility = Visibility.Collapsed;
        RegisterLoadMetricsView.Visibility = Visibility.Collapsed;
        RegisterLoadStatusDoctorNotesView.Visibility = Visibility.Collapsed;
        RegisterLoadButtonsView.Visibility = Visibility.Collapsed;
        RegisterLoadItemInputView.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Visible;
    }

    private void ShowRegisterLoad()
    {
        RegisterLoadTitle.Visibility = Visibility.Visible;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;

        RegisterLoadView.Visibility = Visibility.Visible;
        RegisterLoadQuickHelp.Visibility = Visibility.Visible;
        RegisterLoadMetricsView.Visibility = Visibility.Visible;
        RegisterLoadStatusDoctorNotesView.Visibility = Visibility.Visible;
        RegisterLoadButtonsView.Visibility = Visibility.Visible;
        RegisterLoadItemInputView.Visibility = Visibility.Visible;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;
    }

    private void ShowLoadRecords()
    {
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Visible;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;

        RegisterLoadView.Visibility = Visibility.Collapsed;
        RegisterLoadQuickHelp.Visibility = Visibility.Collapsed;
        RegisterLoadMetricsView.Visibility = Visibility.Collapsed;
        RegisterLoadStatusDoctorNotesView.Visibility = Visibility.Collapsed;
        RegisterLoadButtonsView.Visibility = Visibility.Collapsed;
        RegisterLoadItemInputView.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Visible;
        BiLogSheetsView.Visibility = Visibility.Collapsed;
        HomeView.Visibility = Visibility.Collapsed;
    }

    private void ShowBiLogSheets()
    {
        RegisterLoadTitle.Visibility = Visibility.Collapsed;
        LoadRecordsTitle.Visibility = Visibility.Collapsed;
        HomeDashboardTitle.Visibility = Visibility.Collapsed;

        RegisterLoadView.Visibility = Visibility.Collapsed;
        RegisterLoadQuickHelp.Visibility = Visibility.Collapsed;
        RegisterLoadMetricsView.Visibility = Visibility.Collapsed;
        RegisterLoadStatusDoctorNotesView.Visibility = Visibility.Collapsed;
        RegisterLoadButtonsView.Visibility = Visibility.Collapsed;
        RegisterLoadItemInputView.Visibility = Visibility.Collapsed;

        LoadRecordsView.Visibility = Visibility.Collapsed;
        BiLogSheetsView.Visibility = Visibility.Visible;
        HomeView.Visibility = Visibility.Collapsed;
    }

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

    private async Task RefreshLoadRecordsAsync()
    {
        var q = string.IsNullOrWhiteSpace(LoadRecordsSearchBox.Text) ? "" : LoadRecordsSearchBox.Text.Trim();
        var (rows, err) = await _data.SearchCyclesAsync(q);
        if (err is not null)
        {
            MessageBox.Show(this, err, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _loadRecords.Clear();
        foreach (var r in rows)
        {
            _loadRecords.Add(r);
        }

        if (_loadRecords.Count > 0)
        {
            LoadRecordsGrid.SelectedIndex = 0;
        }

        SetStatus($"Load records — {rows.Count} load(s).");
    }

    // Load Records is intentionally read-only now (items captured during Register Load).

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

        SetStatus($"BI log sheet — {rows.Count} row(s).");
        _ = Dispatcher.BeginInvoke(new Action(UpdateBiLogGridHeight), DispatcherPriority.Loaded);
    }

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
                "IncubatorTemp",
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
                    Csv(r.BiIncubatorTemp),
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

    private void PrintBiLogSheet()
    {
        try
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;
            dlg.PrintVisual(BiLogSheetsView, "HSMS - BI Log Sheet");
            SetStatus("Sent BI Log Sheet to printer.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void ShowPrintPlaceholder()
    {
        var owner = System.Windows.Application.Current.MainWindow;
        if (owner is not null)
        {
            MessageBox.Show(owner, "Official RDLC reports (Load record, BI log, Leak, Bowie–Dick) will open here after report wiring.", "HSMS",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Official RDLC reports (Load record, BI log, Leak, Bowie–Dick) will open here after report wiring.", "HSMS",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
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
        OperatorTextBox.Text = _signedInUsername;
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
        foreach (var d in _doctorRooms.Select(x => x.DisplayName))
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

    private void ApplySelectedItemDefaults()
    {
        var selected = RegisterItemCombo.SelectedItem?.ToString();
        if (string.IsNullOrWhiteSpace(selected)) return;

        var match = _departmentItems.FirstOrDefault(x =>
            string.Equals(x.ItemName, selected, StringComparison.OrdinalIgnoreCase));
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
            SetStatus(ex.Message);
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
                var firstItem = detail.Items.FirstOrDefault();
                RegisterItemCombo.SelectedItem = firstItem?.ItemName;
                RegisterPcsCombo.Text = (firstItem?.Pcs ?? 1).ToString(CultureInfo.InvariantCulture);
                RegisterQtyCombo.Text = (firstItem?.Qty ?? 1).ToString(CultureInfo.InvariantCulture);
                DepartmentCombo.Text = firstItem?.DepartmentName ?? "";

                SetStatus($"Loaded cycle {detail.CycleNo}.");
            }
        }
        else
        {
            _currentSterilizationId = null;
            _currentRowVersion = null;
            _loadedCyclePressure = null;
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
            RegisterItemCombo.SelectedIndex = -1;
            RegisterPcsCombo.Text = "1";
            RegisterQtyCombo.Text = "1";
            BiLotNoTextBox.Text = "";
            SetStatus($"New cycle '{cycleNo}' — choose sterilizer and type, add lines, then Save.");
        }

        SterilizerCombo.Focus();
    }

    private async Task SaveCycleAsync()
    {
        await EnsureCycleNoAsync();

        var itemName = string.IsNullOrWhiteSpace(RegisterItemCombo.Text)
            ? RegisterItemCombo.SelectedItem?.ToString()
            : RegisterItemCombo.Text;
        if (string.IsNullOrWhiteSpace(itemName))
        {
            HsmsAlertWindow.ShowInfo(this, "Item Description is required.", "Missing field");
            RegisterItemCombo.Focus();
            return;
        }

        if (BiIndicatorYesRadio.IsChecked == true && string.IsNullOrWhiteSpace(BiLotNoTextBox.Text))
        {
            HsmsAlertWindow.ShowInfo(this, "BI Lot# is required when Biological indicator is Yes.", "Missing field");
            BiLotNoTextBox.Focus();
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

        var deptText = string.IsNullOrWhiteSpace(DepartmentCombo.Text) ? null : DepartmentCombo.Text.Trim();
        var doctorText = string.IsNullOrWhiteSpace(DoctorRoomCombo.Text)
            ? DoctorRoomCombo.SelectedItem?.ToString()
            : DoctorRoomCombo.Text;
        doctorText = string.IsNullOrWhiteSpace(doctorText) ? null : doctorText.Trim();

        var itemDtos = new List<SterilizationItemDto>
        {
            new()
            {
                ItemName = itemName.Trim(),
                DepartmentName = deptText,
                DoctorOrRoom = doctorText,
                Pcs = pcs,
                Qty = qty
            }
        };

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

        await EnsureDepartmentOptionAsync(deptText);
        await EnsureDoctorRoomOptionAsync(doctorText);
        await EnsureItemDescriptionOptionAsync(deptText, itemName, pcs, qty);

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

    private async Task EnsureDoctorRoomOptionAsync(string? doctorOrRoomDisplay)
    {
        if (string.IsNullOrWhiteSpace(doctorOrRoomDisplay)) return;

        var doctorName = ExtractDoctorName(doctorOrRoomDisplay);
        var exists = _doctorRooms.Any(x =>
            string.Equals(x.DoctorName?.Trim(), doctorName, StringComparison.OrdinalIgnoreCase));
        if (exists) return;

        var (created, err) = await _data.CreateDoctorRoomAsync(new DoctorRoomUpsertDto
        {
            DoctorName = doctorName,
            // NOTE: In Admin Panel "Doctors/Departments", the Department column is persisted in DoctorRoom.Room.
            Room = string.IsNullOrWhiteSpace(DepartmentCombo.Text) ? null : DepartmentCombo.Text.Trim()
        });

        if (err is not null)
        {
            SetStatus($"Saved, but couldn't add new doctor/room '{doctorOrRoomDisplay}' to options ({err}).");
            return;
        }

        var (doctorRooms, docErr) = await _data.GetDoctorRoomsAsync();
        if (docErr is not null)
        {
            DoctorRoomOptions.Add(doctorOrRoomDisplay.Trim());
            return;
        }

        _doctorRooms.Clear();
        _doctorRooms.AddRange(doctorRooms);

        DoctorRoomOptions.Clear();
        foreach (var d in doctorRooms
                     .Select(x => x.DisplayName)
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

    private async Task EnsureItemDescriptionOptionAsync(string? departmentName, string? itemName, int pcs, int qty)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return;

        var trimmedItem = itemName.Trim();
        var exists = ItemDescriptionOptions.Any(x =>
            string.Equals(x?.Trim(), trimmedItem, StringComparison.OrdinalIgnoreCase));
        if (exists) return;

        // We only create a Dept Item if a department is provided (Dept Item master requires DepartmentId).
        if (string.IsNullOrWhiteSpace(departmentName))
        {
            ItemDescriptionOptions.Add(trimmedItem);
            return;
        }

        var depName = departmentName.Trim();

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
            ItemDescriptionOptions.Add(trimmedItem);
            return;
        }

        var (created, err) = await _data.CreateDepartmentItemAsync(new DepartmentItemUpsertDto
        {
            DepartmentId = depId.Value,
            ItemName = trimmedItem,
            DefaultPcs = pcs,
            DefaultQty = qty
        });

        if (err is not null)
        {
            SetStatus($"Saved, but couldn't add new item '{trimmedItem}' to master ({err}).");
            ItemDescriptionOptions.Add(trimmedItem);
            return;
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
            ShowPrintPlaceholder();
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

        OperatorTextBox.Text = _signedInUsername;

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

        RegisterItemCombo.SelectedIndex = -1;
        RegisterPcsCombo.Text = "1";
        RegisterQtyCombo.Text = "1";

        SetStatus("Ready — new cycle entry.");
        _ = SafeRunAsync(EnsureCycleNoAsync);
        SterilizerCombo.Focus();
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

        return _doctorRooms.FirstOrDefault(x =>
                string.Equals(x.DisplayName, selectedDisplay, StringComparison.OrdinalIgnoreCase))
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
        DoctorRoomCombo.SelectedItem = match?.DisplayName;
    }

    private void MoveFocusOnEnter(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (sender is ComboBox combo && combo.IsDropDownOpen) return;
        if (sender is not UIElement current) return;

        e.Handled = true;
        current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    private void RegisterLoadComboNav_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        // Routed event may originate from the inner editable TextBox.
        var combo = sender as ComboBox ?? FindAncestorComboBox(e.OriginalSource as DependencyObject);
        if (combo is null) return;

        var originTextBox = e.OriginalSource as TextBox;

        // If dropdown is open, Up/Down should navigate items as usual.
        if (combo.IsDropDownOpen)
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                // Accept match, close list, then move on.
                CommitComboTypeaheadMatch(combo);
                combo.IsDropDownOpen = false;
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(() =>
                    combo.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                    DispatcherPriority.Input);
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                // Accept current type-to-search match before moving on.
                CommitComboTypeaheadMatch(combo);
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(() =>
                    combo.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                    DispatcherPriority.Input);
                break;
            case Key.Down:
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(() =>
                    combo.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                    DispatcherPriority.Input);
                break;
            case Key.Up:
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(() =>
                    combo.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)),
                    DispatcherPriority.Input);
                break;
            case Key.Right:
                // Only hop fields if caret is already at the end (so Right still edits text normally).
                if (originTextBox is not null &&
                    (originTextBox.SelectionLength > 0 || originTextBox.CaretIndex < originTextBox.Text.Length))
                {
                    return;
                }
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(() =>
                    combo.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),
                    DispatcherPriority.Input);
                break;
            case Key.Left:
                // Only hop fields if caret is already at the start.
                if (originTextBox is not null &&
                    (originTextBox.SelectionLength > 0 || originTextBox.CaretIndex > 0))
                {
                    return;
                }
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(() =>
                    combo.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)),
                    DispatcherPriority.Input);
                break;
        }
    }

    private static void CommitComboTypeaheadMatch(ComboBox combo)
    {
        if (!combo.IsEditable) return;

        var typed = combo.Text ?? "";
        typed = typed.TrimStart();
        if (typed.Length == 0) return;

        // If the user already typed an exact item (case-insensitive), select it.
        foreach (var item in combo.Items)
        {
            var text = item is ComboBoxItem cbi ? cbi.Content?.ToString() : item?.ToString();
            if (string.Equals(text, combo.Text, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                combo.Text = text ?? combo.Text;
                return;
            }
        }

        // Otherwise, pick the first item that starts with what was typed.
        object? best = null;
        string? bestText = null;
        foreach (var item in combo.Items)
        {
            var text = item is ComboBoxItem cbi ? cbi.Content?.ToString() : item?.ToString();
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (!text.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) continue;
            best = item;
            bestText = text;
            break;
        }

        if (best is null || bestText is null) return;
        combo.SelectedItem = best;
        combo.Text = bestText;
    }

    private void AttachRegisterLoadComboNav(ComboBox combo)
    {
        // Attach at the routed-event level so Enter isn't swallowed by the editable TextBox.
        combo.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(RegisterLoadComboNav_OnPreviewKeyDown), true);
        combo.AddHandler(KeyDownEvent, new KeyEventHandler(RegisterLoadComboNav_OnPreviewKeyDown), true);
    }

    private void AttachRegisterLoadTextNav(TextBox tb)
    {
        tb.AddHandler(PreviewKeyDownEvent, new KeyEventHandler(RegisterLoadTextNav_OnPreviewKeyDown), true);
        tb.AddHandler(KeyDownEvent, new KeyEventHandler(RegisterLoadTextNav_OnPreviewKeyDown), true);
    }

    private void RegisterLoadTextNav_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (sender is not TextBox tb) return;

        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = Dispatcher.BeginInvoke(() => MoveNextOrSave(tb), DispatcherPriority.Input);
                break;

            case Key.Right:
                // Only hop fields if caret is at end and no selection (so text editing still works).
                if (tb.SelectionLength == 0 && tb.CaretIndex >= tb.Text.Length)
                {
                    e.Handled = true;
                    _ = Dispatcher.BeginInvoke(() => MoveNextOrSave(tb), DispatcherPriority.Input);
                }
                break;

            case Key.Left:
                // Only hop fields if caret is at start and no selection.
                if (tb.SelectionLength == 0 && tb.CaretIndex <= 0)
                {
                    e.Handled = true;
                    _ = Dispatcher.BeginInvoke(() =>
                        tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Previous)),
                        DispatcherPriority.Input);
                }
                break;
        }
    }

    private void MoveNextOrSave(UIElement current)
    {
        // If we can't move focus forward, treat Enter as "Save record" for speed.
        var moved = current.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        if (!moved || ReferenceEquals(Keyboard.FocusedElement, current))
        {
            _ = SafeRunAsync(SaveCycleAsync);
        }
    }

    private static ComboBox? FindAncestorComboBox(DependencyObject? start)
    {
        var cur = start;
        while (cur is not null)
        {
            if (cur is ComboBox cb) return cb;
            cur = VisualTreeHelper.GetParent(cur);
        }
        return null;
    }

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
