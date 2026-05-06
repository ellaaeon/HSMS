using System.Windows;
using HSMS.Application.Services;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop;

public partial class CreateAccountWindow : Window
{
    private readonly HsmsAuthService _auth;

    public LoginResponseDto? RegisteredSession { get; private set; }

    public CreateAccountWindow(HsmsAuthService auth)
    {
        _auth = auth;
        InitializeComponent();
        BackButton.Click += (_, _) =>
        {
            DialogResult = false;
            Close();
        };
        RegisterButton.Click += async (_, _) => await RegisterAsync();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private async Task RegisterAsync()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        RegisterButton.IsEnabled = false;
        var prev = RegisterButton.Content;
        RegisterButton.Content = "Creating…";
        try
        {
            var req = new StaffRegistrationRequestDto
            {
                Username = UsernameBox.Text.Trim(),
                Password = PasswordBox.Password,
                ConfirmPassword = ConfirmPasswordBox.Password,
                FirstName = FirstNameBox.Text.Trim(),
                LastName = LastNameBox.Text.Trim(),
                Email = EmailBox.Text.Trim(),
                Phone = PhoneBox.Text.Trim(),
                Department = DepartmentBox.Text.Trim(),
                JobTitle = JobTitleBox.Text.Trim(),
                EmployeeId = EmployeeIdBox.Text.Trim(),
                ClientMachine = Environment.MachineName
            };

            var (ok, dto, err) = await _auth.RegisterStaffAsync(req);
            if (!ok || dto is null)
            {
                ShowError(err ?? "Registration failed.");
                return;
            }

            RegisteredSession = dto;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            RegisterButton.Content = prev;
            RegisterButton.IsEnabled = true;
        }
    }
}
