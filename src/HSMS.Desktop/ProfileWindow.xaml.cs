using System.Windows;
using HSMS.Application.Services;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop;

public partial class ProfileWindow : Window
{
    private readonly LoginResponseDto _session;
    private readonly HsmsAuthService _auth;

    public ProfileWindow(LoginResponseDto session, HsmsAuthService auth)
    {
        _session = session;
        _auth = auth;
        InitializeComponent();

        var p = session.Profile ?? new StaffProfileDto();
        var display = $"{p.FirstName} {p.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(display))
        {
            display = session.Username;
        }

        Title = $"Profile — {display}";
        SubtitleText.Text = $"Signed in as {session.Username} ({session.Role}). Edit your details and save.";
        UsernameDisplay.Text = session.Username;
        RoleDisplay.Text = session.Role;

        FirstNameBox.Text = p.FirstName ?? string.Empty;
        LastNameBox.Text = p.LastName ?? string.Empty;
        EmployeeIdBox.Text = p.EmployeeId ?? string.Empty;
        DepartmentBox.Text = p.Department ?? string.Empty;
        JobTitleBox.Text = p.JobTitle ?? string.Empty;
        EmailBox.Text = p.Email ?? string.Empty;
        PhoneBox.Text = p.Phone ?? string.Empty;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private async void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        SaveButton.IsEnabled = false;
        var prev = SaveButton.Content;
        SaveButton.Content = "Saving…";
        var saved = false;
        try
        {
            var dto = new StaffProfileDto
            {
                FirstName = FirstNameBox.Text,
                LastName = LastNameBox.Text,
                Email = EmailBox.Text,
                Phone = PhoneBox.Text,
                Department = DepartmentBox.Text,
                JobTitle = JobTitleBox.Text,
                EmployeeId = EmployeeIdBox.Text
            };

            var (ok, err) = await _auth.UpdateMyProfileAsync(_session.AccountId, dto);
            if (!ok)
            {
                ShowError(err ?? "Could not save profile.");
                return;
            }

            _session.Profile ??= new StaffProfileDto();
            _session.Profile.FirstName = dto.FirstName?.Trim();
            _session.Profile.LastName = dto.LastName?.Trim();
            _session.Profile.Email = dto.Email?.Trim();
            _session.Profile.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
            _session.Profile.Department = string.IsNullOrWhiteSpace(dto.Department) ? null : dto.Department.Trim();
            _session.Profile.JobTitle = string.IsNullOrWhiteSpace(dto.JobTitle) ? null : dto.JobTitle.Trim();
            _session.Profile.EmployeeId = string.IsNullOrWhiteSpace(dto.EmployeeId) ? null : dto.EmployeeId.Trim();

            saved = true;
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SaveButton.Content = prev;
            SaveButton.IsEnabled = true;
        }

        if (saved)
        {
            DialogResult = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
