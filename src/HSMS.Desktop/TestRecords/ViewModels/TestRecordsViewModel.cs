using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using HSMS.Application.Services;
using HSMS.Application.Exports;
using HSMS.Desktop.Mvvm;
using HSMS.Desktop.Reporting;
using HSMS.Desktop.Views;
using HSMS.Shared.Contracts;
using HSMS.Shared.Contracts.Reporting;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using System.Windows.Data;
using System.ComponentModel;

namespace HSMS.Desktop.TestRecords.ViewModels;

public sealed class TestRecordsViewModel : ObservableObject
{
    private readonly IHsmsDataService _data;
    private readonly IExcelWorkbookExportService? _workbookExport;
    private readonly PrintReportCoordinator? _print;

    private SterilizationQaCategory _activeCategory = SterilizationQaCategory.Dashboard;
    private CancellationTokenSource? _refreshCts;
    private int _refreshVersion;
    private DateTime _fromLocal = DateTime.Today.AddDays(-30);
    private DateTime _toLocal = DateTime.Today;
    private string _quickSearch = "";
    private SterilizationQaWorkflowStatus? _status;
    private bool _failedOnly;
    private bool _pendingOnly;
    private bool _reviewQueue;
    private bool _groupBySterilizer;
    private int? _sterilizerId;
    private string? _department;
    private string? _technician;
    private int? _reviewerAccountId;
    private bool _isLoading;
    private SterilizationQaRecordListItemDto? _selected;
    private SterilizationSearchItemDto? _selectedLoad;
    private SterilizationQaDashboardDto? _dashboard;
    private bool _isDashboardLoading;
    private bool _isDetailsLoading;
    private int _bulkSelectionCount;
    private List<SterilizationQaRecordListItemDto> _bulkSelection = new();
    private readonly ObservableCollection<SterilizationQaPresetListItemDto> _presets = new();
    private int? _selectedPresetId;
    private bool _arePresetsAvailable = true;
    private string _presetsStatusText = "";

    public TestRecordsViewModel(IHsmsDataService data, IExcelWorkbookExportService? workbookExport, PrintReportCoordinator? printCoordinator)
    {
        _data = data;
        _workbookExport = workbookExport;
        _print = printCoordinator;
        Records = new ObservableCollection<SterilizationQaRecordListItemDto>();
        RecordsView = CollectionViewSource.GetDefaultView(Records);
        Loads = new ObservableCollection<SterilizationSearchItemDto>();
        LoadsView = CollectionViewSource.GetDefaultView(Loads);
        Timeline = new ObservableCollection<SterilizationQaTimelineEventDto>();
        Evidence = new ObservableCollection<SterilizationQaAttachmentListItemDto>();
        TrendSeries = new ObservableCollection<ISeries>();
        TrendXAxes = new Axis[] { new Axis { LabelsRotation = 0, TextSize = 11 } };
        TrendYAxes = new Axis[] { new Axis { TextSize = 11 } };
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => !IsLoading);
        NewRecordCommand = new AsyncRelayCommand(NewRecordAsync, () => !IsLoading && ActiveCategory != SterilizationQaCategory.Dashboard);
        NewFromLoadCommand = new AsyncRelayCommand(NewFromLoadAsync, () => !IsLoading && ActiveCategory != SterilizationQaCategory.Dashboard);
        ApproveCommand = new AsyncRelayCommand(ApproveAsync, () => !IsLoading && SelectedRecord is not null);
        FailCommand = new AsyncRelayCommand(MarkFailedAsync, () => !IsLoading && SelectedRecord is { IsLegacyQaTest: false });
        AddEvidenceCommand = new AsyncRelayCommand(AddEvidenceAsync, () => !IsLoading && SelectedRecord is { IsLegacyQaTest: false });
        OpenEvidenceCommand = new RelayCommand<SterilizationQaAttachmentListItemDto>(OpenEvidence);
        SubmitForReviewCommand = new AsyncRelayCommand(SubmitForReviewAsync, () => !IsLoading && SelectedRecord is { IsLegacyQaTest: false });
        RejectToDraftCommand = new AsyncRelayCommand(RejectToDraftAsync, () => !IsLoading && SelectedRecord is { IsLegacyQaTest: false });
        RetestRequiredCommand = new AsyncRelayCommand(RetestRequiredAsync, () => !IsLoading && SelectedRecord is { IsLegacyQaTest: false });
        ArchiveCommand = new AsyncRelayCommand(ArchiveAsync, () => !IsLoading && SelectedRecord is not null);

        BulkApproveCommand = new AsyncRelayCommand(BulkApproveAsync, () => !IsLoading && BulkSelectionCount > 0);
        BulkRejectCommand = new AsyncRelayCommand(BulkRejectAsync, () => !IsLoading && BulkSelectionCount > 0);

        ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => !IsLoading && (IsLoadRecordsView ? Loads.Count > 0 : Records.Count > 0));
        ExportXlsxCommand = new AsyncRelayCommand(ExportXlsxAsync, () => !IsLoading && (IsLoadRecordsView ? Loads.Count > 0 : Records.Count > 0));
        ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync, () => !IsLoading && SelectedRecord is not null);
        ExportPdfBatchCommand = new AsyncRelayCommand(ExportPdfBatchAsync, () => !IsLoading && BulkSelectionCount > 0);
        ExportTablePdfCommand = new AsyncRelayCommand(ExportTablePdfAsync, () => !IsLoading && IsLoadRecordsView && Loads.Count > 0);
        PrintCommand = new AsyncRelayCommand(PrintAsync, () => !IsLoading && _print is not null && SelectedLoad is not null);
        AddCommand = new AsyncRelayCommand(AddAsync, () => !IsLoading);

        RefreshPresetsCommand = new AsyncRelayCommand(LoadPresetsAsync, () => !IsLoading);
        ApplyPresetCommand = new AsyncRelayCommand(ApplySelectedPresetAsync, () => !IsLoading && ArePresetsAvailable && SelectedPresetId.HasValue);
        SavePresetCommand = new AsyncRelayCommand(SavePresetAsync, () => !IsLoading && ArePresetsAvailable);
        SetDefaultPresetCommand = new AsyncRelayCommand(SetDefaultPresetAsync, () => !IsLoading && ArePresetsAvailable && SelectedPresetId.HasValue);
        RenamePresetCommand = new AsyncRelayCommand(RenamePresetAsync, () => !IsLoading && ArePresetsAvailable && SelectedPresetId.HasValue);
        DeletePresetCommand = new AsyncRelayCommand(DeletePresetAsync, () => !IsLoading && ArePresetsAvailable && SelectedPresetId.HasValue);
        AdvancedFilterCommand = new AsyncRelayCommand(OpenAdvancedFilterAsync, () => !IsLoading);

        _ = LoadPresetsAsync();
        _ = ApplyDefaultPresetIfAnyAsync();
    }

    public ObservableCollection<SterilizationQaRecordListItemDto> Records { get; }
    public ICollectionView RecordsView { get; }
    public bool HasRecords => Records.Count > 0;

    public ObservableCollection<SterilizationSearchItemDto> Loads { get; }
    public ICollectionView LoadsView { get; }
    public bool HasLoads => Loads.Count > 0;

    public bool IsLoadRecordsView =>
        ActiveCategory is SterilizationQaCategory.BowieDick
            or SterilizationQaCategory.LeakTest
            or SterilizationQaCategory.WarmUpTest
            or SterilizationQaCategory.InstrumentTests
            or SterilizationQaCategory.BiologicalIndicator;

    private bool IsBiLoadsView => ActiveCategory == SterilizationQaCategory.BiologicalIndicator;

    private static string? CycleProgramFilterForCategory(SterilizationQaCategory category) =>
        category switch
        {
            SterilizationQaCategory.BowieDick => "Bowie",
            SterilizationQaCategory.LeakTest => "Leak",
            SterilizationQaCategory.WarmUpTest => "Warm",
            SterilizationQaCategory.InstrumentTests => "Instrument",
            _ => null
        };

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand NewRecordCommand { get; }
    public AsyncRelayCommand NewFromLoadCommand { get; }
    public AsyncRelayCommand ApproveCommand { get; }
    public AsyncRelayCommand FailCommand { get; }
    public AsyncRelayCommand AddEvidenceCommand { get; }
    public RelayCommand<SterilizationQaAttachmentListItemDto> OpenEvidenceCommand { get; }
    public AsyncRelayCommand SubmitForReviewCommand { get; }
    public AsyncRelayCommand RejectToDraftCommand { get; }
    public AsyncRelayCommand RetestRequiredCommand { get; }
    public AsyncRelayCommand ArchiveCommand { get; }
    public AsyncRelayCommand BulkApproveCommand { get; }
    public AsyncRelayCommand BulkRejectCommand { get; }
    public AsyncRelayCommand ExportCsvCommand { get; }
    public AsyncRelayCommand ExportXlsxCommand { get; }
    public AsyncRelayCommand ExportPdfCommand { get; }
    public AsyncRelayCommand ExportPdfBatchCommand { get; }
    public AsyncRelayCommand ExportTablePdfCommand { get; }
    public AsyncRelayCommand PrintCommand { get; }
    public AsyncRelayCommand AddCommand { get; }

    public AsyncRelayCommand RefreshPresetsCommand { get; }
    public AsyncRelayCommand ApplyPresetCommand { get; }
    public AsyncRelayCommand SavePresetCommand { get; }
    public AsyncRelayCommand SetDefaultPresetCommand { get; }
    public AsyncRelayCommand RenamePresetCommand { get; }
    public AsyncRelayCommand DeletePresetCommand { get; }
    public AsyncRelayCommand AdvancedFilterCommand { get; }

    public ObservableCollection<SterilizationQaPresetListItemDto> Presets => _presets;

    public bool ArePresetsAvailable
    {
        get => _arePresetsAvailable;
        private set
        {
            if (SetProperty(ref _arePresetsAvailable, value))
            {
                ApplyPresetCommand.RaiseCanExecuteChanged();
                SavePresetCommand.RaiseCanExecuteChanged();
                SetDefaultPresetCommand.RaiseCanExecuteChanged();
                RenamePresetCommand.RaiseCanExecuteChanged();
                DeletePresetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string PresetsStatusText
    {
        get => _presetsStatusText;
        private set => SetProperty(ref _presetsStatusText, value ?? "");
    }

    public int? SelectedPresetId
    {
        get => _selectedPresetId;
        set
        {
            if (SetProperty(ref _selectedPresetId, value))
            {
                ApplyPresetCommand.RaiseCanExecuteChanged();
                SetDefaultPresetCommand.RaiseCanExecuteChanged();
                RenamePresetCommand.RaiseCanExecuteChanged();
                DeletePresetCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int BulkSelectionCount
    {
        get => _bulkSelectionCount;
        private set
        {
            if (SetProperty(ref _bulkSelectionCount, value))
            {
                BulkApproveCommand.RaiseCanExecuteChanged();
                BulkRejectCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(ComplianceSummary));
            }
        }
    }

    public SterilizationQaCategory ActiveCategory
    {
        get => _activeCategory;
        set
        {
            if (SetProperty(ref _activeCategory, value))
            {
                RaisePropertyChanged(nameof(IsDashboardActive));
                RaisePropertyChanged(nameof(IsLoadRecordsView));
                ResetWorkspaceCollectionsForActiveCategory();
                if (IsDashboardActive)
                {
                    _ = RefreshDashboardAsync();
                }
                else
                {
                    _ = RefreshAsync();
                }
                NewRecordCommand.RaiseCanExecuteChanged();
                NewFromLoadCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsDashboardActive => ActiveCategory == SterilizationQaCategory.Dashboard;

    private void ResetWorkspaceCollectionsForActiveCategory()
    {
        // When switching tabs, proactively clear the *inactive* workspace to avoid stale rows/selection
        // and to keep empty-state visibility consistent while the refresh runs.
        if (IsLoadRecordsView)
        {
            SelectedRecord = null;
            Records.Clear();
            RaisePropertyChanged(nameof(HasRecords));
            RaisePropertyChanged(nameof(ComplianceSummary));
        }
        else
        {
            SelectedLoad = null;
            Loads.Clear();
            RaisePropertyChanged(nameof(HasLoads));
            RaisePropertyChanged(nameof(SelectedSummary));
        }
    }

    public string ComplianceSummary
    {
        get
        {
            if (IsDashboardActive)
            {
                var d = Dashboard;
                if (d is null) return "Loading compliance…";
                var critical = d.Alerts.Count(a => a.Severity == SterilizationQaAlertSeverity.Critical);
                var pending = d.PendingReview;
                return critical > 0
                    ? $"Critical alerts: {critical} • Pending review: {pending}"
                    : $"Pending review: {pending} • Alerts: {d.Alerts.Count}";
            }

            var pendingLocal = Records.Count(x => x.Status == SterilizationQaWorkflowStatus.PendingReview);
            var failedLocal = Records.Count(x => x.Status == SterilizationQaWorkflowStatus.Failed);
            var bulk = BulkSelectionCount > 0 ? $" • Selected: {BulkSelectionCount}" : "";
            return $"Pending: {pendingLocal} • Failed: {failedLocal}{bulk}";
        }
    }

    public IReadOnlyList<string> ActiveFilterChips
    {
        get
        {
            var chips = new List<string>();
            if (ReviewQueue) chips.Add("Review queue (oldest-first)");
            if (PendingOnly) chips.Add("Pending only");
            if (FailedOnly) chips.Add("Failed only");
            if (StatusFilter is { } s) chips.Add($"Status: {s}");
            if (!string.IsNullOrWhiteSpace(QuickSearch)) chips.Add($"Search: {QuickSearch.Trim()}");
            if (SterilizerIdFilter is { } sid) chips.Add($"SterilizerId: {sid}");
            if (!string.IsNullOrWhiteSpace(DepartmentFilter)) chips.Add($"Dept: {DepartmentFilter!.Trim()}");
            if (!string.IsNullOrWhiteSpace(TechnicianFilter)) chips.Add($"Tech: {TechnicianFilter!.Trim()}");
            if (ReviewerAccountIdFilter is { } rid) chips.Add($"Reviewer: #{rid}");
            chips.Add($"From: {FromLocal:yyyy-MM-dd}");
            chips.Add($"To: {ToLocal:yyyy-MM-dd}");
            return chips;
        }
    }

    public DateTime FromLocal
    {
        get => _fromLocal;
        set
        {
            if (SetProperty(ref _fromLocal, value))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public DateTime ToLocal
    {
        get => _toLocal;
        set
        {
            if (SetProperty(ref _toLocal, value))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public string QuickSearch
    {
        get => _quickSearch;
        set
        {
            if (SetProperty(ref _quickSearch, value ?? ""))
            {
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public SterilizationQaWorkflowStatus? StatusFilter
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public bool FailedOnly
    {
        get => _failedOnly;
        set
        {
            if (SetProperty(ref _failedOnly, value))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public bool PendingOnly
    {
        get => _pendingOnly;
        set
        {
            if (SetProperty(ref _pendingOnly, value))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public bool ReviewQueue
    {
        get => _reviewQueue;
        set
        {
            if (SetProperty(ref _reviewQueue, value))
            {
                if (_reviewQueue)
                {
                    PendingOnly = true;
                }
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public int? SterilizerIdFilter
    {
        get => _sterilizerId;
        set
        {
            if (SetProperty(ref _sterilizerId, value))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public string? DepartmentFilter
    {
        get => _department;
        set
        {
            if (SetProperty(ref _department, string.IsNullOrWhiteSpace(value) ? null : value.Trim()))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public string? TechnicianFilter
    {
        get => _technician;
        set
        {
            if (SetProperty(ref _technician, string.IsNullOrWhiteSpace(value) ? null : value.Trim()))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public int? ReviewerAccountIdFilter
    {
        get => _reviewerAccountId;
        set
        {
            if (SetProperty(ref _reviewerAccountId, value))
            {
                _ = RefreshAsync();
                RaisePropertyChanged(nameof(ActiveFilterChips));
            }
        }
    }

    public bool GroupBySterilizer
    {
        get => _groupBySterilizer;
        set
        {
            if (SetProperty(ref _groupBySterilizer, value))
            {
                ApplyGrouping();
            }
        }
    }

    private void ApplyGrouping()
    {
        RecordsView.GroupDescriptions.Clear();
        if (GroupBySterilizer)
        {
            RecordsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SterilizationQaRecordListItemDto.SterilizerNo)));
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                NewRecordCommand.RaiseCanExecuteChanged();
                NewFromLoadCommand.RaiseCanExecuteChanged();
                PrintCommand.RaiseCanExecuteChanged();
                AddCommand.RaiseCanExecuteChanged();
                ApproveCommand.RaiseCanExecuteChanged();
                FailCommand.RaiseCanExecuteChanged();
                ExportCsvCommand.RaiseCanExecuteChanged();
                ExportXlsxCommand.RaiseCanExecuteChanged();
                ExportTablePdfCommand.RaiseCanExecuteChanged();
                ExportPdfCommand.RaiseCanExecuteChanged();
                ExportPdfBatchCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(HasRecords));
            }
        }
    }

    public bool IsDashboardLoading
    {
        get => _isDashboardLoading;
        private set => SetProperty(ref _isDashboardLoading, value);
    }

    public bool IsDetailsLoading
    {
        get => _isDetailsLoading;
        private set => SetProperty(ref _isDetailsLoading, value);
    }

    public SterilizationQaDashboardDto? Dashboard
    {
        get => _dashboard;
        private set
        {
            if (SetProperty(ref _dashboard, value))
            {
                RaisePropertyChanged(nameof(DashboardSummaryLine));
            }
        }
    }

    public string DashboardSummaryLine
    {
        get
        {
            var d = Dashboard;
            if (d is null) return "—";
            var legacy = d.LegacyQaTotal == 0 ? "" : $" • Legacy QA: {d.LegacyQaTotal} (pending {d.LegacyPendingApproval})";
            var lastFail = d.LastFailedSterilizerNo is null ? "" : $" • Last failure: {d.LastFailedSterilizerNo} @ {d.LastFailureAtUtc:yyyy-MM-dd HH:mm} UTC";
            return $"Total {d.Total} • Approved {d.Approved} • Failed {d.Failed} • Pending {d.PendingReview}{legacy}{lastFail}";
        }
    }

    public ObservableCollection<ISeries> TrendSeries { get; }
    public Axis[] TrendXAxes { get; private set; }
    public Axis[] TrendYAxes { get; private set; }

    public ObservableCollection<SterilizationQaTimelineEventDto> Timeline { get; }
    public ObservableCollection<SterilizationQaAttachmentListItemDto> Evidence { get; }

    public SterilizationQaRecordListItemDto? SelectedRecord
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                ApproveCommand.RaiseCanExecuteChanged();
                FailCommand.RaiseCanExecuteChanged();
                AddEvidenceCommand.RaiseCanExecuteChanged();
                SubmitForReviewCommand.RaiseCanExecuteChanged();
                RejectToDraftCommand.RaiseCanExecuteChanged();
                RetestRequiredCommand.RaiseCanExecuteChanged();
                ArchiveCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedSummary));
                RaisePropertyChanged(nameof(ComplianceSummary));
                _ = LoadDetailsAsync();
            }
        }
    }

    public SterilizationSearchItemDto? SelectedLoad
    {
        get => _selectedLoad;
        set
        {
            if (SetProperty(ref _selectedLoad, value))
            {
                PrintCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(SelectedSummary));
            }
        }
    }

    public void SetBulkSelectionCount(int count)
    {
        BulkSelectionCount = Math.Max(0, count);
    }

    public void SetBulkSelection(IEnumerable<SterilizationQaRecordListItemDto> rows)
    {
        _bulkSelection = rows?.Where(r => r is not null).ToList() ?? new List<SterilizationQaRecordListItemDto>();
        BulkSelectionCount = _bulkSelection.Count;
        ExportPdfBatchCommand.RaiseCanExecuteChanged();
    }

    public string SelectedSummary
    {
        get
        {
            if (IsLoadRecordsView)
            {
                if (SelectedLoad is null) return "Select a load record to create a test record from it.";
                var l = SelectedLoad;
                return
                    $"Load record\n" +
                    $"#{l.SterilizationId} • Cycle {l.CycleNo} • {l.SterilizerNo}\n" +
                    $"{l.RegisteredAtUtc:yyyy-MM-dd HH:mm} UTC • Status: {l.CycleStatus}";
            }

            if (SelectedRecord is null) return "Select a record to preview workflow actions.";
            var row = SelectedRecord;
            var legacyHint = row.IsLegacyQaTest ? "Legacy test record" : "Test record";
            return
                $"{legacyHint}\n" +
                $"#{row.RecordId} • {row.Category} • {row.Status}\n" +
                $"{(string.IsNullOrWhiteSpace(row.CycleNo) ? "" : $"Cycle {row.CycleNo} • ")}{row.SterilizerNo}\n" +
                $"{row.TestDateTimeUtc:yyyy-MM-dd HH:mm} UTC • Result: {row.ResultLabel ?? "—"}";
        }
    }

    private async Task RefreshAsync()
    {
        // Cancel any in-flight refresh so rapid tab switches don't apply out-of-date results.
        _refreshCts?.Cancel();
        _refreshCts?.Dispose();
        _refreshCts = new CancellationTokenSource();
        var token = _refreshCts.Token;
        var version = Interlocked.Increment(ref _refreshVersion);
        var categoryAtStart = ActiveCategory;

        IsLoading = true;
        try
        {
            var fromUtc = DateTime.SpecifyKind(FromLocal.Date, DateTimeKind.Local).ToUniversalTime();
            var toUtc = DateTime.SpecifyKind(ToLocal.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

            if (IsLoadRecordsView)
            {
                var search = string.IsNullOrWhiteSpace(QuickSearch) ? null : QuickSearch.Trim();

                (IReadOnlyList<SterilizationSearchItemDto> loadItems, string? loadErr) =
                    IsBiLoadsView
                        ? await _data.ListLoadsWithBiAsync(search, fromUtc, toUtc, token)
                        : await _data.ListLoadsByCycleProgramAsync(
                            cycleProgramContains: CycleProgramFilterForCategory(categoryAtStart) ?? "",
                            searchQuery: search,
                            fromUtc: fromUtc,
                            toUtc: toUtc,
                            cancellationToken: token);
                if (loadErr is not null) throw new InvalidOperationException(loadErr);
                if (token.IsCancellationRequested || version != _refreshVersion || ActiveCategory != categoryAtStart) return;

                Loads.Clear();
                foreach (var r in loadItems) Loads.Add(r);
                RaisePropertyChanged(nameof(HasLoads));
                RaisePropertyChanged(nameof(SelectedSummary));
                return;
            }

            var q = new SterilizationQaRecordQueryDto
            {
                Category = categoryAtStart == SterilizationQaCategory.Dashboard ? null : categoryAtStart,
                FromUtc = fromUtc,
                ToUtc = toUtc,
                Status = StatusFilter,
                FailedOnly = FailedOnly,
                PendingOnly = PendingOnly,
                ReviewQueue = ReviewQueue,
                Search = string.IsNullOrWhiteSpace(QuickSearch) ? null : QuickSearch.Trim(),
                SterilizerId = SterilizerIdFilter,
                Department = DepartmentFilter,
                Technician = TechnicianFilter,
                ReviewerAccountId = ReviewerAccountIdFilter,
                Take = 600
            };

            var (qaItems, qaErr) = await _data.ListSterilizationQaRecordsAsync(q);
            if (qaErr is not null) throw new InvalidOperationException(qaErr);
            if (token.IsCancellationRequested || version != _refreshVersion || ActiveCategory != categoryAtStart) return;

            Records.Clear();
            foreach (var r in qaItems) Records.Add(r);
            RaisePropertyChanged(nameof(HasRecords));
            RaisePropertyChanged(nameof(ComplianceSummary));
            ExportCsvCommand.RaiseCanExecuteChanged();
            ExportXlsxCommand.RaiseCanExecuteChanged();
            RaisePropertyChanged(nameof(ActiveFilterChips));
            ApplyGrouping();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ExportCsvAsync()
    {
        var exportLabel = ExportLabelForActiveTab();
        var dlg = new SaveFileDialog
        {
            Title = "Export Records (CSV)",
            Filter = "CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            FileName = $"hsms-{exportLabel}-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog() != true) return;

        var csv = IsLoadRecordsView ? BuildLoadsCsv(Loads) : BuildCsv(Records);
        File.WriteAllText(dlg.FileName, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        _ = _data.AppendSterilizationQaAuditAsync(new SterilizationQaAuditEventDto
        {
            Action = HSMS.Application.Audit.AuditActions.SterilizationQaExportCsv,
            Format = "CSV",
            RecordCount = Records.Count,
            Filter = new { ActiveCategory, FromLocal, ToLocal, StatusFilter, FailedOnly, PendingOnly, ReviewQueue, QuickSearch },
            ClientMachine = Environment.MachineName
        });
        await Task.CompletedTask;
    }

    private async Task ExportXlsxAsync()
    {
        if (_workbookExport is null)
        {
            throw new InvalidOperationException("Excel export service is not initialized.");
        }

        var exportLabel = ExportLabelForActiveTab();
        var dlg = new SaveFileDialog
        {
            Title = "Export Records (XLSX)",
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            FileName = $"hsms-{exportLabel}-{DateTime.Now:yyyyMMdd-HHmm}.xlsx",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog() != true) return;

        await using var fs = File.Open(dlg.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
        _workbookExport.WriteWorkbook(wb =>
        {
            if (IsLoadRecordsView)
            {
                var ws = wb.Worksheets.Add("Load Records");
                var headers = new[] { "SterilizationId","CycleNo","CycleProgram","SterilizerNo","Operator","Status","RegisteredUtc","Pcs","Qty" };
                for (var c = 0; c < headers.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = headers[c];
                    ws.Cell(1, c + 1).Style.Font.Bold = true;
                }

                var r = 2;
                foreach (var row in Loads)
                {
                    ws.Cell(r, 1).Value = row.SterilizationId;
                    ws.Cell(r, 2).Value = row.CycleNo ?? "";
                    ws.Cell(r, 3).Value = row.CycleProgram ?? "";
                    ws.Cell(r, 4).Value = row.SterilizerNo ?? "";
                    ws.Cell(r, 5).Value = row.OperatorName ?? "";
                    ws.Cell(r, 6).Value = row.CycleStatus ?? "";
                    ws.Cell(r, 7).Value = row.RegisteredAtUtc;
                    ws.Cell(r, 7).Style.NumberFormat.Format = "yyyy-mm-dd hh:mm";
                    ws.Cell(r, 8).Value = row.TotalPcs;
                    ws.Cell(r, 9).Value = row.TotalQty;
                    r++;
                }
                ws.Columns().AdjustToContents();
                ws.SheetView.FreezeRows(1);
                ws.RangeUsed()?.SetAutoFilter();
                return;
            }

            var ws2 = wb.Worksheets.Add("Test Records");
            var headers2 = new[] { "RecordId","Category","CycleNo","SterilizerNo","WhenUtc","Status","Result","Technician","Department","HasAttachments","Legacy" };
            for (var c = 0; c < headers2.Length; c++)
            {
                ws2.Cell(1, c + 1).Value = headers2[c];
                ws2.Cell(1, c + 1).Style.Font.Bold = true;
            }

            var rr = 2;
            foreach (var row in Records)
            {
                ws2.Cell(rr, 1).Value = row.RecordId;
                ws2.Cell(rr, 2).Value = row.Category.ToString();
                ws2.Cell(rr, 3).Value = row.CycleNo ?? "";
                ws2.Cell(rr, 4).Value = row.SterilizerNo ?? "";
                ws2.Cell(rr, 5).Value = row.TestDateTimeUtc;
                ws2.Cell(rr, 5).Style.NumberFormat.Format = "yyyy-mm-dd hh:mm";
                ws2.Cell(rr, 6).Value = row.Status.ToString();
                ws2.Cell(rr, 7).Value = row.ResultLabel ?? "";
                ws2.Cell(rr, 8).Value = row.Technician ?? "";
                ws2.Cell(rr, 9).Value = row.Department ?? "";
                ws2.Cell(rr, 10).Value = row.HasAttachments;
                ws2.Cell(rr, 11).Value = row.IsLegacyQaTest;
                rr++;
            }

            ws2.Columns().AdjustToContents();
            ws2.SheetView.FreezeRows(1);
            ws2.RangeUsed()?.SetAutoFilter();
        }, fs);

        _ = _data.AppendSterilizationQaAuditAsync(new SterilizationQaAuditEventDto
        {
            Action = HSMS.Application.Audit.AuditActions.SterilizationQaExportXlsx,
            Format = "XLSX",
            RecordCount = Records.Count,
            Filter = new { ActiveCategory, FromLocal, ToLocal, StatusFilter, FailedOnly, PendingOnly, ReviewQueue, QuickSearch },
            ClientMachine = Environment.MachineName
        });
    }

    private async Task ExportTablePdfAsync()
    {
        if (!IsLoadRecordsView || Loads.Count == 0) return;

        var exportLabel = ExportLabelForActiveTab();
        var dlg = new SaveFileDialog
        {
            Title = "Export Records (PDF)",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"hsms-{exportLabel}-{DateTime.Now:yyyyMMdd-HHmm}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog() != true) return;

        var title = "STERILIZATION LOAD RECORDS";
        var subtitle = $"{ActiveCategory} • {FromLocal:yyyy-MM-dd} to {ToLocal:yyyy-MM-dd}";
        var pdf = LoadRecordsPdfBuilder.BuildPdfBytes(title, subtitle, Loads.ToList());
        await File.WriteAllBytesAsync(dlg.FileName, pdf);
        try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }
    }

    private string ExportLabelForActiveTab()
    {
        if (IsLoadRecordsView)
        {
            // Prefer the program filter (maps to Instruments/Bowie/Leak/WarmUp)
            var p = CycleProgramFilterForCategory(ActiveCategory);
            if (!string.IsNullOrWhiteSpace(p))
            {
                return p.Trim().ToLowerInvariant();
            }
        }

        // Fallback for non-load tabs.
        var cat = ActiveCategory == SterilizationQaCategory.Dashboard ? "records" : ActiveCategory.ToString();
        return cat.Trim().ToLowerInvariant();
    }

    private async Task PrintAsync()
    {
        try
        {
            if (_print is null)
            {
                System.Windows.MessageBox.Show(
                    "Printing is not available (print coordinator not initialized).",
                    "HSMS — Print",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            if (SelectedLoad is null)
            {
                System.Windows.MessageBox.Show(
                    "Select a row first, then click Print.",
                    "HSMS — Print",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
                return;
            }

            var req = new ReportRenderRequestDto
            {
                ReportType = ReportType.LoadRecord,
                SterilizationId = SelectedLoad.SterilizationId,
                Copies = 1,
                ClientMachine = Environment.MachineName
            };

            // Prefer the active window as the dialog owner (this screen),
            // so modal overlays/preview don't appear "behind" the current UI.
            var owner = System.Windows.Application.Current?.Windows
                .OfType<System.Windows.Window>()
                .FirstOrDefault(w => w.IsActive);

            await _print.ShowPreviewAsync(req, owner);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                $"Print failed.\n\n{ex.Message}",
                "HSMS — Print",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    private async Task AddAsync()
    {
        // Minimal: open the existing "select load cycle" picker (read-only) so user can quickly jump to a record.
        // This keeps behavior consistent with the rest of the module without reintroducing workflow actions.
        var picker = new SelectCycleWindow(_data, FromLocal, ToLocal)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        picker.ShowDialog();
        await Task.CompletedTask;
    }

    private static string BuildLoadsCsv(IEnumerable<SterilizationSearchItemDto> rows)
    {
        static string Esc(string? s)
        {
            var t = s ?? "";
            if (t.Contains('"') || t.Contains(',') || t.Contains('\n') || t.Contains('\r'))
            {
                return "\"" + t.Replace("\"", "\"\"") + "\"";
            }
            return t;
        }

        var sb = new StringBuilder();
        sb.AppendLine("SterilizationId,CycleNo,CycleProgram,SterilizerNo,Operator,CycleStatus,RegisteredUtc,TotalPcs,TotalQty");
        foreach (var r in rows)
        {
            sb.Append(r.SterilizationId).Append(',')
              .Append(Esc(r.CycleNo)).Append(',')
              .Append(Esc(r.CycleProgram)).Append(',')
              .Append(Esc(r.SterilizerNo)).Append(',')
              .Append(Esc(r.OperatorName)).Append(',')
              .Append(Esc(r.CycleStatus)).Append(',')
              .Append(Esc(r.RegisteredAtUtc.ToString("yyyy-MM-dd HH:mm"))).Append(',')
              .Append(r.TotalPcs).Append(',')
              .Append(r.TotalQty)
              .AppendLine();
        }
        return sb.ToString();
    }

    private async Task ExportPdfAsync()
    {
        if (SelectedRecord is null) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export QA Record (PDF)",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"hsms-qa-record-{SelectedRecord.RecordId}-{DateTime.Now:yyyyMMdd-HHmm}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog() != true) return;

        var rows = new List<SterilizationQaRecordListItemDto> { SelectedRecord };
        var rid = SelectedRecord.RecordId;
        var (t, terr) = await _data.GetSterilizationQaTimelineAsync(rid);
        var (a, aerr) = await _data.ListSterilizationQaAttachmentsAsync(rid);
        var timeline = terr is null && t is not null ? t.Events : [];
        var evidence = aerr is null ? a : [];

        var pdf = SterilizationQaPdfBuilder.BuildPdfBytes(
            printedBy: Environment.UserName,
            printedAtLocal: DateTime.Now,
            records: rows,
            timelineFor: _ => timeline,
            evidenceFor: _ => evidence);

        await File.WriteAllBytesAsync(dlg.FileName, pdf);
        try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }

        _ = _data.AppendSterilizationQaAuditAsync(new SterilizationQaAuditEventDto
        {
            Action = HSMS.Application.Audit.AuditActions.SterilizationQaExportPdf,
            Format = "PDF",
            RecordCount = 1,
            Filter = new { SelectedRecord.RecordId },
            ClientMachine = Environment.MachineName
        });
    }

    private async Task ExportPdfBatchAsync()
    {
        if (_bulkSelection.Count == 0) return;
        var dlg = new SaveFileDialog
        {
            Title = "Export QA Records (Batch PDF)",
            Filter = "PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            FileName = $"hsms-qa-records-batch-{DateTime.Now:yyyyMMdd-HHmm}.pdf",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
        };
        if (dlg.ShowDialog() != true) return;

        // For batch, fetch timeline/evidence per record (so batch report is complete).
        var timelineMap = new Dictionary<long, IReadOnlyList<SterilizationQaTimelineEventDto>>();
        var evidenceMap = new Dictionary<long, IReadOnlyList<SterilizationQaAttachmentListItemDto>>();
        foreach (var r in _bulkSelection)
        {
            var (t, terr) = await _data.GetSterilizationQaTimelineAsync(r.RecordId);
            timelineMap[r.RecordId] = terr is null && t is not null ? t.Events : [];
            var (a, aerr) = await _data.ListSterilizationQaAttachmentsAsync(r.RecordId);
            evidenceMap[r.RecordId] = aerr is null ? a : [];
        }

        var pdf = SterilizationQaPdfBuilder.BuildPdfBytes(
            printedBy: Environment.UserName,
            printedAtLocal: DateTime.Now,
            records: _bulkSelection,
            timelineFor: rid => timelineMap.GetValueOrDefault(rid) ?? [],
            evidenceFor: rid => evidenceMap.GetValueOrDefault(rid) ?? []);

        await File.WriteAllBytesAsync(dlg.FileName, pdf);
        try { Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true }); } catch { }

        _ = _data.AppendSterilizationQaAuditAsync(new SterilizationQaAuditEventDto
        {
            Action = HSMS.Application.Audit.AuditActions.SterilizationQaExportPdf,
            Format = "PDF",
            RecordCount = _bulkSelection.Count,
            Filter = new { BulkSelectionCount = _bulkSelection.Count, ReviewQueue },
            ClientMachine = Environment.MachineName
        });
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var (items, err) = await _data.ListSterilizationQaPresetsAsync();
            if (err is not null)
            {
                // If DB scripts haven't been applied yet, avoid crashing the UI.
                // Common case: "Invalid object name 'qa_test_presets'."
                _presets.Clear();
                ArePresetsAvailable = false;
                PresetsStatusText = err.Contains("qa_test_presets", StringComparison.OrdinalIgnoreCase)
                    ? "Saved filters unavailable (apply DB script 023_hsms_qa_presets.sql)"
                    : "Saved filters unavailable";
                return;
            }

            _presets.Clear();
            foreach (var p in items) _presets.Add(p);
            ArePresetsAvailable = true;
            PresetsStatusText = "";
        }
        catch
        {
            // Best-effort: presets are optional; keep module functional even if presets fail.
            _presets.Clear();
            ArePresetsAvailable = false;
            PresetsStatusText = "Saved filters unavailable";
        }
    }

    private async Task ApplyDefaultPresetIfAnyAsync()
    {
        var (preset, err) = await _data.GetDefaultSterilizationQaPresetAsync();
        if (err is not null) return;
        if (preset is null) return;
        ApplyQueryToUi(preset.Query);
        SelectedPresetId = preset.PresetId;
        await RefreshAsync();
    }

    private async Task ApplySelectedPresetAsync()
    {
        if (!SelectedPresetId.HasValue) return;
        var (preset, err) = await _data.GetSterilizationQaPresetAsync(SelectedPresetId.Value);
        if (err is not null) throw new InvalidOperationException(err);
        if (preset is null) return;
        ApplyQueryToUi(preset.Query);
        await RefreshAsync();
    }

    private async Task SavePresetAsync()
    {
        if (!ArePresetsAvailable)
        {
            System.Windows.MessageBox.Show(
                "Saved filters are not available because the database table is missing.\n\nApply: hsms-db/ddl/023_hsms_qa_presets.sql",
                "HSMS — Saved Filters",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        // Minimal: save as "QA Preset <timestamp>" (we'll add rename dialog next).
        var name = $"QA Preset {DateTime.Now:yyyy-MM-dd HHmm}";
        var payload = new SterilizationQaPresetUpsertDto
        {
            Name = name,
            Query = CaptureCurrentQuery(),
            SetAsDefault = false
        };
        try
        {
            var (saved, err) = await _data.UpsertSterilizationQaPresetAsync(null, payload);
            if (err is not null) throw new InvalidOperationException(err);
            await LoadPresetsAsync();
            SelectedPresetId = saved?.PresetId;
        }
        catch (Exception ex)
        {
            // Keep app running; presets are optional.
            System.Windows.MessageBox.Show(
                $"Could not save filter.\n\n{ex.Message}",
                "HSMS — Saved Filters",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private async Task SetDefaultPresetAsync()
    {
        if (!SelectedPresetId.HasValue) return;
        var err = await _data.SetDefaultSterilizationQaPresetAsync(SelectedPresetId.Value);
        if (err is not null) throw new InvalidOperationException(err);
        await LoadPresetsAsync();
    }

    private async Task RenamePresetAsync()
    {
        if (!SelectedPresetId.HasValue) return;
        var (preset, err) = await _data.GetSterilizationQaPresetAsync(SelectedPresetId.Value);
        if (err is not null) throw new InvalidOperationException(err);
        if (preset is null) return;

        var win = new TextPromptWindow("HSMS — Rename filter", "Enter a new name for this saved filter:", preset.Name)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (win.ShowDialog() != true) return;
        var newName = win.ResultText;
        if (string.IsNullOrWhiteSpace(newName)) return;

        var (saved, upErr) = await _data.UpsertSterilizationQaPresetAsync(preset.PresetId, new SterilizationQaPresetUpsertDto
        {
            Name = newName,
            Query = preset.Query,
            SetAsDefault = preset.IsDefault
        });
        if (upErr is not null) throw new InvalidOperationException(upErr);
        await LoadPresetsAsync();
        SelectedPresetId = saved?.PresetId ?? preset.PresetId;
    }

    private async Task DeletePresetAsync()
    {
        if (!SelectedPresetId.HasValue) return;
        var id = SelectedPresetId.Value;
        var result = System.Windows.MessageBox.Show(
            "Delete this saved filter?\n\nThis cannot be undone.",
            "HSMS — Saved Filters",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);
        if (result != System.Windows.MessageBoxResult.Yes) return;

        var err = await _data.DeleteSterilizationQaPresetAsync(id);
        if (err is not null) throw new InvalidOperationException(err);
        SelectedPresetId = null;
        await LoadPresetsAsync();
    }

    private SterilizationQaRecordQueryDto CaptureCurrentQuery()
    {
        var fromUtc = DateTime.SpecifyKind(FromLocal.Date, DateTimeKind.Local).ToUniversalTime();
        var toUtc = DateTime.SpecifyKind(ToLocal.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();
        return new SterilizationQaRecordQueryDto
        {
            Category = ActiveCategory == SterilizationQaCategory.Dashboard ? null : ActiveCategory,
            FromUtc = fromUtc,
            ToUtc = toUtc,
            Status = StatusFilter,
            FailedOnly = FailedOnly,
            PendingOnly = PendingOnly,
            ReviewQueue = ReviewQueue,
            Search = string.IsNullOrWhiteSpace(QuickSearch) ? null : QuickSearch.Trim(),
            SterilizerId = SterilizerIdFilter,
            Department = DepartmentFilter,
            Technician = TechnicianFilter,
            ReviewerAccountId = ReviewerAccountIdFilter,
            Take = 600
        };
    }

    private void ApplyQueryToUi(SterilizationQaRecordQueryDto q)
    {
        // Note: keep dates in local for UI; stored query is UTC.
        // `ToUtc` is stored as an exclusive upper bound (start of the *next* day in local time, converted to UTC).
        // When mapping back to UI, show the inclusive local end date (exclusive - 1 day) to avoid "drifting" forward.
        if (q.FromUtc.HasValue) FromLocal = q.FromUtc.Value.ToLocalTime().Date;
        if (q.ToUtc.HasValue)
        {
            var exclusiveToLocalDate = q.ToUtc.Value.ToLocalTime().Date;
            ToLocal = exclusiveToLocalDate.AddDays(-1);
        }
        FailedOnly = q.FailedOnly;
        PendingOnly = q.PendingOnly;
        ReviewQueue = q.ReviewQueue;
        StatusFilter = q.Status;
        QuickSearch = q.Search ?? "";
        SterilizerIdFilter = q.SterilizerId;
        DepartmentFilter = q.Department;
        TechnicianFilter = q.Technician;
        ReviewerAccountIdFilter = q.ReviewerAccountId;
    }

    private async Task OpenAdvancedFilterAsync()
    {
        var current = CaptureCurrentQuery();
        var win = new AdvancedQaFilterWindow(_data, current)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (win.ShowDialog() != true) return;

        ApplyQueryToUi(win.ResultQuery);
        await Task.CompletedTask;
    }

    private static string BuildCsv(IEnumerable<SterilizationQaRecordListItemDto> rows)
    {
        static string Esc(string? s)
        {
            var t = s ?? "";
            if (t.Contains('"') || t.Contains(',') || t.Contains('\n') || t.Contains('\r'))
            {
                return "\"" + t.Replace("\"", "\"\"") + "\"";
            }
            return t;
        }

        var sb = new StringBuilder();
        sb.AppendLine("RecordId,Category,CycleNo,SterilizerNo,WhenUtc,Status,Result,Technician,Department,HasAttachments,Legacy");
        foreach (var r in rows)
        {
            sb.Append(r.RecordId).Append(',')
              .Append(Esc(r.Category.ToString())).Append(',')
              .Append(Esc(r.CycleNo)).Append(',')
              .Append(Esc(r.SterilizerNo)).Append(',')
              .Append(Esc(r.TestDateTimeUtc.ToString("yyyy-MM-dd HH:mm"))).Append(',')
              .Append(Esc(r.Status.ToString())).Append(',')
              .Append(Esc(r.ResultLabel)).Append(',')
              .Append(Esc(r.Technician)).Append(',')
              .Append(Esc(r.Department)).Append(',')
              .Append(r.HasAttachments ? "true" : "false").Append(',')
              .Append(r.IsLegacyQaTest ? "true" : "false")
              .AppendLine();
        }
        return sb.ToString();
    }

    private async Task LoadDetailsAsync()
    {
        Timeline.Clear();
        Evidence.Clear();
        if (SelectedRecord is null) return;

        IsDetailsLoading = true;
        try
        {
            var (timeline, terr) = await _data.GetSterilizationQaTimelineAsync(SelectedRecord.RecordId);
            if (terr is null && timeline is not null)
            {
                foreach (var e in timeline.Events) Timeline.Add(e);
            }

            var (attachments, aerr) = await _data.ListSterilizationQaAttachmentsAsync(SelectedRecord.RecordId);
            if (aerr is null)
            {
                foreach (var a in attachments) Evidence.Add(a);
            }
        }
        finally
        {
            IsDetailsLoading = false;
        }
    }

    private async Task RefreshDashboardAsync()
    {
        IsDashboardLoading = true;
        try
        {
            var fromUtc = DateTime.SpecifyKind(FromLocal.Date, DateTimeKind.Local).ToUniversalTime();
            var toUtc = DateTime.SpecifyKind(ToLocal.Date.AddDays(1), DateTimeKind.Local).ToUniversalTime();

            var (d, err) = await _data.GetSterilizationQaDashboardAsync(new SterilizationQaDashboardQueryDto
            {
                FromUtc = fromUtc,
                ToUtc = toUtc
            });
            if (err is not null) throw new InvalidOperationException(err);
            Dashboard = d;
            BuildTrendSeries(d);
            RaisePropertyChanged(nameof(ComplianceSummary));
        }
        finally
        {
            IsDashboardLoading = false;
        }
    }

    private void BuildTrendSeries(SterilizationQaDashboardDto? d)
    {
        TrendSeries.Clear();
        if (d is null || d.ByDay.Count == 0)
        {
            TrendXAxes = [new Axis { Labels = [], TextSize = 11 }];
            RaisePropertyChanged(nameof(TrendXAxes));
            return;
        }

        var labels = d.ByDay.Select(x => x.DayUtc.ToString("MM-dd")).ToArray();
        TrendXAxes = [new Axis { Labels = labels, LabelsRotation = 0, TextSize = 11 }];
        RaisePropertyChanged(nameof(TrendXAxes));

        var approved = d.ByDay.Select(x => (double)x.Approved).ToArray();
        var failed = d.ByDay.Select(x => (double)x.Failed).ToArray();
        var pending = d.ByDay.Select(x => (double)x.PendingReview).ToArray();

        TrendSeries.Add(new LineSeries<double>
        {
            Name = "Approved",
            Values = approved,
            GeometrySize = 6,
            Stroke = new SolidColorPaint(new SKColor(6, 95, 70), 3),
            Fill = null
        });
        TrendSeries.Add(new LineSeries<double>
        {
            Name = "Failed",
            Values = failed,
            GeometrySize = 6,
            Stroke = new SolidColorPaint(new SKColor(153, 27, 27), 3),
            Fill = null
        });
        TrendSeries.Add(new LineSeries<double>
        {
            Name = "Pending",
            Values = pending,
            GeometrySize = 6,
            Stroke = new SolidColorPaint(new SKColor(146, 64, 14), 3),
            Fill = null
        });
    }

    private async Task NewRecordAsync()
    {
        var payload = new SterilizationQaRecordCreateDto
        {
            Category = ActiveCategory,
            TestDateTimeUtc = DateTime.UtcNow,
            Summary = $"New {ActiveCategory} record",
            ClientMachine = Environment.MachineName
        };
        var (_, _, err) = await _data.CreateSterilizationQaRecordAsync(payload);
        if (err is not null) throw new InvalidOperationException(err);
        await RefreshAsync();
    }

    private async Task NewFromLoadAsync()
    {
        if (IsLoadRecordsView && SelectedLoad is not null)
        {
            var selectedLoad = SelectedLoad;
            var (loadDetail, loadDetailErr) = await _data.GetCycleAsync(selectedLoad.SterilizationId);
            if (loadDetailErr is not null) throw new InvalidOperationException(loadDetailErr);

            var deptFromLoad = loadDetail?.Items?.Select(i => i.DepartmentName).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();
            var createFromSelectedPayload = new SterilizationQaRecordCreateDto
            {
                Category = ActiveCategory,
                SterilizationId = selectedLoad.SterilizationId,
                SterilizerId = loadDetail?.SterilizerId,
                TestDateTimeUtc = DateTime.UtcNow,
                Technician = loadDetail?.OperatorName,
                Department = deptFromLoad,
                Summary = $"{ActiveCategory} — Cycle {loadDetail?.CycleNo ?? selectedLoad.CycleNo} ({selectedLoad.SterilizerNo})",
                Notes = loadDetail?.Notes,
                ClientMachine = Environment.MachineName
            };

            var (_, _, createFromSelectedErr) = await _data.CreateSterilizationQaRecordAsync(createFromSelectedPayload);
            if (createFromSelectedErr is not null) throw new InvalidOperationException(createFromSelectedErr);
            await RefreshAsync();
            return;
        }

        var picker = new SelectCycleWindow(_data, FromLocal, ToLocal)
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };
        if (picker.ShowDialog() != true) return;
        var sel = picker.SelectedCycle;
        if (sel is null) return;

        var (detail, derr) = await _data.GetCycleAsync(sel.SterilizationId);
        if (derr is not null) throw new InvalidOperationException(derr);

        var dept = detail?.Items?.Select(i => i.DepartmentName).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();

        var payload = new SterilizationQaRecordCreateDto
        {
            Category = ActiveCategory,
            SterilizationId = sel.SterilizationId,
            SterilizerId = detail?.SterilizerId,
            TestDateTimeUtc = DateTime.UtcNow,
            Technician = detail?.OperatorName,
            Department = dept,
            Summary = $"{ActiveCategory} — Cycle {detail?.CycleNo ?? sel.CycleNo} ({sel.SterilizerNo})",
            Notes = detail?.Notes,
            ClientMachine = Environment.MachineName
        };

        var (_, _, err) = await _data.CreateSterilizationQaRecordAsync(payload);
        if (err is not null) throw new InvalidOperationException(err);
        await RefreshAsync();
    }

    private async Task ApproveAsync()
    {
        if (SelectedRecord is null) return;
        var row = SelectedRecord;

        if (row.IsLegacyQaTest)
        {
            var qaId = (int)Math.Abs(row.RecordId);
            var (rv, err) = await _data.ApproveQaTestAsync(qaId, row.RowVersion, remarks: null, clientMachine: Environment.MachineName);
            if (err is not null) throw new InvalidOperationException(err);
            row.RowVersion = rv ?? row.RowVersion;
        }
        else
        {
            var patch = new SterilizationQaRecordStatusPatchDto
            {
                RowVersion = row.RowVersion,
                NewStatus = SterilizationQaWorkflowStatus.Approved,
                ClientMachine = Environment.MachineName
            };
            var (rv, err) = await _data.PatchSterilizationQaStatusAsync(row.RecordId, patch);
            if (err is not null) throw new InvalidOperationException(err);
            row.RowVersion = rv ?? row.RowVersion;
        }

        await RefreshAsync();
    }

    private async Task BulkApproveAsync()
    {
        var rows = _bulkSelection.Count > 0 ? _bulkSelection : (SelectedRecord is null ? [] : new List<SterilizationQaRecordListItemDto> { SelectedRecord });
        if (rows.Count == 0) return;

        foreach (var row in rows)
        {
            if (row.IsLegacyQaTest)
            {
                var qaId = (int)Math.Abs(row.RecordId);
                var (rv, err) = await _data.ApproveQaTestAsync(qaId, row.RowVersion, remarks: null, clientMachine: Environment.MachineName);
                if (err is not null) throw new InvalidOperationException(err);
                row.RowVersion = rv ?? row.RowVersion;
                continue;
            }

            var patch = new SterilizationQaRecordStatusPatchDto
            {
                RowVersion = row.RowVersion,
                NewStatus = SterilizationQaWorkflowStatus.Approved,
                Comment = "Bulk approve (review queue).",
                ClientMachine = Environment.MachineName
            };
            var (newRv, pErr) = await _data.PatchSterilizationQaStatusAsync(row.RecordId, patch);
            if (pErr is not null) throw new InvalidOperationException(pErr);
            row.RowVersion = newRv ?? row.RowVersion;
        }

        await RefreshAsync();
    }

    private async Task BulkRejectAsync()
    {
        var rows = _bulkSelection.Count > 0 ? _bulkSelection : (SelectedRecord is null ? [] : new List<SterilizationQaRecordListItemDto> { SelectedRecord });
        if (rows.Count == 0) return;

        foreach (var row in rows)
        {
            if (row.IsLegacyQaTest)
            {
                continue; // no reject state in legacy QA
            }

            var patch = new SterilizationQaRecordStatusPatchDto
            {
                RowVersion = row.RowVersion,
                NewStatus = SterilizationQaWorkflowStatus.Draft,
                Comment = "Bulk reject to draft (review queue).",
                ClientMachine = Environment.MachineName
            };
            var (newRv, pErr) = await _data.PatchSterilizationQaStatusAsync(row.RecordId, patch);
            if (pErr is not null) throw new InvalidOperationException(pErr);
            row.RowVersion = newRv ?? row.RowVersion;
        }

        await RefreshAsync();
    }

    private async Task MarkFailedAsync()
    {
        if (SelectedRecord is null || SelectedRecord.IsLegacyQaTest) return;
        var row = SelectedRecord;
        var patch = new SterilizationQaRecordStatusPatchDto
        {
            RowVersion = row.RowVersion,
            NewStatus = SterilizationQaWorkflowStatus.Failed,
            Comment = "Marked failed from Test Records module.",
            ClientMachine = Environment.MachineName
        };
        var (rv, err) = await _data.PatchSterilizationQaStatusAsync(row.RecordId, patch);
        if (err is not null) throw new InvalidOperationException(err);
        row.RowVersion = rv ?? row.RowVersion;
        await RefreshAsync();
    }

    private async Task SubmitForReviewAsync()
    {
        if (SelectedRecord is null || SelectedRecord.IsLegacyQaTest) return;
        var row = SelectedRecord;
        var patch = new SterilizationQaRecordStatusPatchDto
        {
            RowVersion = row.RowVersion,
            NewStatus = SterilizationQaWorkflowStatus.PendingReview,
            Comment = "Submitted for review.",
            ClientMachine = Environment.MachineName
        };
        var (rv, err) = await _data.PatchSterilizationQaStatusAsync(row.RecordId, patch);
        if (err is not null) throw new InvalidOperationException(err);
        row.RowVersion = rv ?? row.RowVersion;
        await RefreshAsync();
        await LoadDetailsAsync();
    }

    private async Task RejectToDraftAsync()
    {
        if (SelectedRecord is null || SelectedRecord.IsLegacyQaTest) return;
        var row = SelectedRecord;
        var patch = new SterilizationQaRecordStatusPatchDto
        {
            RowVersion = row.RowVersion,
            NewStatus = SterilizationQaWorkflowStatus.Draft,
            Comment = "Rejected back to draft (please complete documentation).",
            ClientMachine = Environment.MachineName
        };
        var (rv, err) = await _data.PatchSterilizationQaStatusAsync(row.RecordId, patch);
        if (err is not null) throw new InvalidOperationException(err);
        row.RowVersion = rv ?? row.RowVersion;
        await RefreshAsync();
        await LoadDetailsAsync();
    }

    private async Task RetestRequiredAsync()
    {
        if (SelectedRecord is null || SelectedRecord.IsLegacyQaTest) return;
        var row = SelectedRecord;
        var patch = new SterilizationQaRecordStatusPatchDto
        {
            RowVersion = row.RowVersion,
            NewStatus = SterilizationQaWorkflowStatus.RetestRequired,
            Comment = "Retest required.",
            ClientMachine = Environment.MachineName
        };
        var (rv, err) = await _data.PatchSterilizationQaStatusAsync(row.RecordId, patch);
        if (err is not null) throw new InvalidOperationException(err);
        row.RowVersion = rv ?? row.RowVersion;
        await RefreshAsync();
        await LoadDetailsAsync();
    }

    private async Task ArchiveAsync()
    {
        if (SelectedRecord is null) return;
        if (SelectedRecord.IsLegacyQaTest)
        {
            // Legacy QA doesn't have archive state; encourage using enterprise record categories.
            return;
        }
        var row = SelectedRecord;
        var patch = new SterilizationQaRecordStatusPatchDto
        {
            RowVersion = row.RowVersion,
            NewStatus = SterilizationQaWorkflowStatus.Archived,
            Comment = "Archived.",
            ClientMachine = Environment.MachineName
        };
        var (rv, err) = await _data.PatchSterilizationQaStatusAsync(row.RecordId, patch);
        if (err is not null) throw new InvalidOperationException(err);
        row.RowVersion = rv ?? row.RowVersion;
        await RefreshAsync();
        await LoadDetailsAsync();
    }

    private async Task AddEvidenceAsync()
    {
        if (SelectedRecord is null || SelectedRecord.IsLegacyQaTest) return;

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Add evidence / attachment",
            Filter = "All supported|*.pdf;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.txt;*.csv|PDF (*.pdf)|*.pdf|Images (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp|All files (*.*)|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog() != true) return;

        foreach (var file in dlg.FileNames)
        {
            var (_, _, err) = await _data.AddSterilizationQaAttachmentAsync(
                SelectedRecord.RecordId,
                new SterilizationQaAttachmentAddDto { SourceFilePath = file, ClientMachine = Environment.MachineName });
            if (err is not null) throw new InvalidOperationException(err);
        }

        await LoadDetailsAsync();
        await RefreshAsync();
    }

    private static void OpenEvidence(SterilizationQaAttachmentListItemDto? item)
    {
        if (item is null) return;
        if (string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath)) return;
        Process.Start(new ProcessStartInfo(item.FilePath) { UseShellExecute = true });
    }
}

