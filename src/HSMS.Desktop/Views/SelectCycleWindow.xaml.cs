using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using HSMS.Application.Services;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop.Views;

public partial class SelectCycleWindow : Window
{
    private readonly IHsmsDataService _data;
    private CancellationTokenSource? _cts;

    public SterilizationSearchItemDto? SelectedCycle { get; private set; }

    public SelectCycleWindow(IHsmsDataService data, DateTime fromLocal, DateTime toLocal)
    {
        _data = data;
        InitializeComponent();

        // Default behavior: show ALL load records so the picker doesn't look "empty"
        // just because the Test Records module is currently filtered to a narrow date range.
        // Users can still narrow by setting From/To.
        FromPicker.SelectedDate = null;
        ToPicker.SelectedDate = null;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        try
        {
            StatusText.Text = "Loading cycles…";

            var fromUtc = FromPicker.SelectedDate?.Date.ToUniversalTime();
            var toUtc = ToPicker.SelectedDate?.Date.AddDays(1).ToUniversalTime();
            var q = SearchBox.Text ?? "";

            var (items, err) = await _data.SearchCyclesFilteredAsync(
                searchQuery: q,
                fromUtc: fromUtc,
                toUtc: toUtc,
                cancellationToken: ct,
                matchCycleNoOnly: false);

            if (ct.IsCancellationRequested) return;

            if (err is not null)
            {
                Grid.ItemsSource = Array.Empty<SterilizationSearchItemDto>();
                StatusText.Text = err;
                return;
            }

            Grid.ItemsSource = items ?? Array.Empty<SterilizationSearchItemDto>();
            var cnt = ((IReadOnlyCollection<SterilizationSearchItemDto>?)items)?.Count ?? 0;
            var rangeHint = (fromUtc.HasValue || toUtc.HasValue) ? "" : " (all dates)";
            StatusText.Text = $"Loaded {cnt} cycle(s){rangeHint}.";
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception ex)
        {
            Grid.ItemsSource = Array.Empty<SterilizationSearchItemDto>();
            StatusText.Text = ex.Message;
        }
    }

    private async void Refresh_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void SearchBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        // lightweight debounce
        await Task.Delay(250);
        await RefreshAsync();
    }

    private async void DatePicker_OnChanged(object sender, SelectionChangedEventArgs e) => await RefreshAsync();

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Select_OnClick(object sender, RoutedEventArgs e)
    {
        SelectedCycle = Grid.SelectedItem as SterilizationSearchItemDto;
        if (SelectedCycle is null)
        {
            MessageBox.Show("Select a cycle first.", "HSMS — Select load cycle", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }
}

