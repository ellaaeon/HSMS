using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HSMS.Application.Services;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop.Views;

public partial class AdvancedQaFilterWindow : Window
{
    private readonly IHsmsDataService _data;

    public sealed record LookupItem(int? Id, string Label);

    public SterilizationQaRecordQueryDto ResultQuery { get; private set; } = new();

    public AdvancedQaFilterWindow(IHsmsDataService data, SterilizationQaRecordQueryDto current)
    {
        _data = data;
        InitializeComponent();
        Loaded += async (_, _) => await LoadLookupsAsync();

        // Seed UI
        FromDatePicker.SelectedDate = current.FromUtc?.ToLocalTime().Date;
        ToDatePicker.SelectedDate = current.ToUtc?.ToLocalTime().Date;
        FailedOnlyCheck.IsChecked = current.FailedOnly;
        PendingOnlyCheck.IsChecked = current.PendingOnly;
        TechnicianBox.Text = current.Technician ?? "";

        if (!string.IsNullOrWhiteSpace(current.Department))
        {
            DepartmentCombo.Items.Add(current.Department);
            DepartmentCombo.SelectedItem = current.Department;
        }

        // Status selection
        if (current.Status is { } s)
        {
            foreach (var item in StatusCombo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Content as string, s.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    StatusCombo.SelectedItem = item;
                    break;
                }
            }
        }

        ResultQuery = current;
    }

    private async System.Threading.Tasks.Task LoadLookupsAsync()
    {
        try
        {
            // Sterilizers
            var (sters, sErr) = await _data.GetSterilizersAsync();
            if (sErr is null)
            {
                var sterItems = new List<LookupItem> { new(null, "(all)") };
                sterItems.AddRange(sters.Select(x => new LookupItem(x.SterilizerId, x.SterilizerNo)));
                SterilizerCombo.ItemsSource = sterItems;
                SterilizerCombo.SelectedValue = ResultQuery.SterilizerId;
            }

            // Departments
            var (depts, dErr) = await _data.GetDepartmentsAsync();
            if (dErr is null)
            {
                DepartmentCombo.Items.Clear();
                DepartmentCombo.Items.Add("(all)");
                foreach (var d in depts.Select(x => x.DepartmentName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                {
                    DepartmentCombo.Items.Add(d);
                }
                DepartmentCombo.SelectedItem = string.IsNullOrWhiteSpace(ResultQuery.Department) ? "(all)" : ResultQuery.Department;
            }

            // Reviewers (accounts)
            var (accounts, aErr) = await _data.GetAccountsAsync();
            if (aErr is null)
            {
                var reviewerItems = new List<LookupItem> { new(null, "(all)") };
                reviewerItems.AddRange(accounts.Select(a => new LookupItem(a.AccountId, $"{a.Username} (#{a.AccountId})")));
                ReviewerCombo.ItemsSource = reviewerItems;
                ReviewerCombo.SelectedValue = ResultQuery.ReviewerAccountId;
            }
        }
        catch
        {
            // best-effort: dialog can still be used with text fields
        }
    }

    private void Reset_OnClick(object sender, RoutedEventArgs e)
    {
        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
        StatusCombo.SelectedIndex = 0;
        SterilizerCombo.SelectedIndex = 0;
        DepartmentCombo.SelectedIndex = 0;
        ReviewerCombo.SelectedIndex = 0;
        TechnicianBox.Text = "";
        FailedOnlyCheck.IsChecked = false;
        PendingOnlyCheck.IsChecked = false;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void Apply_OnClick(object sender, RoutedEventArgs e)
    {
        var fromLocal = FromDatePicker.SelectedDate?.Date;
        var toLocal = ToDatePicker.SelectedDate?.Date;

        var statusRaw = (StatusCombo.SelectedItem as ComboBoxItem)?.Content as string;
        SterilizationQaWorkflowStatus? status = null;
        if (!string.IsNullOrWhiteSpace(statusRaw) && statusRaw != "(all)" &&
            Enum.TryParse<SterilizationQaWorkflowStatus>(statusRaw, ignoreCase: true, out var st))
        {
            status = st;
        }

        var dept = DepartmentCombo.SelectedItem as string;
        if (string.Equals(dept, "(all)", StringComparison.OrdinalIgnoreCase)) dept = null;

        var tech = TechnicianBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(tech)) tech = null;

        var sterId = SterilizerCombo.SelectedValue as int?;
        var reviewerId = ReviewerCombo.SelectedValue as int?;

        ResultQuery = new SterilizationQaRecordQueryDto
        {
            FromUtc = fromLocal?.ToUniversalTime(),
            ToUtc = toLocal?.AddDays(1).ToUniversalTime(),
            Status = status,
            SterilizerId = sterId,
            Department = dept,
            Technician = tech,
            ReviewerAccountId = reviewerId,
            FailedOnly = FailedOnlyCheck.IsChecked == true,
            PendingOnly = PendingOnlyCheck.IsChecked == true,
            // keep other fields untouched (category/search/reviewqueue/take) from previous
            Category = ResultQuery.Category,
            Search = ResultQuery.Search,
            ReviewQueue = ResultQuery.ReviewQueue,
            Take = ResultQuery.Take
        };

        DialogResult = true;
        Close();
    }
}

