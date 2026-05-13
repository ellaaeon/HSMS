using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using HSMS.Application.Services;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop;

public partial class AnalyticsDrilldownWindow : Window
{
    private readonly IHsmsDataService _data;
    private readonly AnalyticsDrilldownRequestDto _request;
    private readonly ObservableCollection<SterilizationSearchItemDto> _rows = [];

    private const int PageSize = 150;
    private int _page = 0;

    public AnalyticsDrilldownWindow(IHsmsDataService dataService, AnalyticsDrilldownRequestDto request)
    {
        _data = dataService;
        _request = request;
        InitializeComponent();

        RowsGrid.ItemsSource = _rows;
        CloseButton.Click += (_, _) => Close();
        PrevButton.Click += async (_, _) => await LoadPageAsync(_page - 1);
        NextButton.Click += async (_, _) => await LoadPageAsync(_page + 1);

        Loaded += async (_, _) =>
        {
            ApplyHeader();
            await LoadPageAsync(0);
        };
    }

    private void ApplyHeader()
    {
        var title = _request.Context?.Title;
        BreadcrumbText.Text = string.IsNullOrWhiteSpace(title) ? "Analytics > Drill-down" : $"Analytics > {title}";

        var f = _request.Filter ?? new AnalyticsFilterDto();
        var from = f.FromUtc.HasValue ? f.FromUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "—";
        var to = f.ToUtc.HasValue ? f.ToUtc.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "—";
        var chips = BuildFilterChips(f);
        RangeText.Text = $"Range (CreatedAt UTC): {from} to {to}" + (chips.Length == 0 ? "" : $" • {chips}");
    }

    private static string BuildFilterChips(AnalyticsFilterDto f)
    {
        var parts = new List<string>(12);
        if (f.SterilizerId is int sid) parts.Add($"SterilizerId={sid}");
        if (!string.IsNullOrWhiteSpace(f.LoadStatus)) parts.Add($"Status={f.LoadStatus}");
        if (f.Implants is true) parts.Add("Implants=Yes");
        if (f.Implants is false) parts.Add("Implants=No");
        if (!string.IsNullOrWhiteSpace(f.BiResult)) parts.Add($"BI={f.BiResult}");
        if (!string.IsNullOrWhiteSpace(f.Department)) parts.Add($"Dept={f.Department}");
        if (f.DoctorRoomId is int dr) parts.Add($"DoctorRoomId={dr}");
        if (!string.IsNullOrWhiteSpace(f.OperatorName)) parts.Add($"Operator={f.OperatorName}");
        if (!string.IsNullOrWhiteSpace(f.GlobalSearch)) parts.Add($"Search=\"{f.GlobalSearch}\"");
        return string.Join(" • ", parts);
    }

    private async Task LoadPageAsync(int page)
    {
        if (page < 0) page = 0;
        PrevButton.IsEnabled = false;
        NextButton.IsEnabled = false;
        PagerText.Text = "Loading…";

        var skip = page * PageSize;
        var (items, err) = await _data.AnalyticsDrilldownAsync(_request.Filter ?? new AnalyticsFilterDto(), skip, PageSize);
        if (err is not null)
        {
            MessageBox.Show(this, err, "HSMS — Drill-down", MessageBoxButton.OK, MessageBoxImage.Warning);
            PagerText.Text = "Failed to load.";
            PrevButton.IsEnabled = page > 0;
            NextButton.IsEnabled = false;
            return;
        }

        _rows.Clear();
        foreach (var it in items)
        {
            _rows.Add(it);
        }

        _page = page;
        PagerText.Text = items.Count == 0 && page == 0
            ? "No rows match the selected filters."
            : $"Page {_page + 1} • Showing {_rows.Count:N0} rows";

        PrevButton.IsEnabled = _page > 0;
        NextButton.IsEnabled = items.Count == PageSize;
    }
}

