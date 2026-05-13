using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using HSMS.Application.Services;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop;

public partial class CreateAccountWindow : Window
{
    private readonly HsmsAuthService _auth;
    private bool _suppressPasswordPlainTextChanged;
    private bool _suppressConfirmPasswordPlainTextChanged;
    private DispatcherTimer? _capsLockTimer;

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

        Loaded += (_, _) =>
        {
            UsernameBox.Focus();
            ApplyShowPasswordUi();
            StartCapsLockWatcher();
            HookLiveValidation();
            ValidateForm();
        };
        Closed += (_, _) => StopCapsLockWatcher();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HookLiveValidation()
    {
        UsernameBox.TextChanged += (_, _) => ValidateForm();
        FirstNameBox.TextChanged += (_, _) => ValidateForm();
        LastNameBox.TextChanged += (_, _) => ValidateForm();
        EmailBox.TextChanged += (_, _) => ValidateForm();
        PhoneBox.TextChanged += (_, _) => ValidateForm();
        DepartmentBox.TextChanged += (_, _) => ValidateForm();
        JobTitleBox.TextChanged += (_, _) => ValidateForm();
        EmployeeIdBox.TextChanged += (_, _) => ValidateForm();
        PasswordBox.PasswordChanged += (_, _) => ValidateForm();
        ConfirmPasswordBox.PasswordChanged += (_, _) => ValidateForm();
        PasswordPlainBox.TextChanged += (_, _) => ValidateForm();
        ConfirmPasswordPlainBox.TextChanged += (_, _) => ValidateForm();
    }

    private static bool LooksLikeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        // Basic check: keeps UX friendly without being overly strict.
        var t = email.Trim();
        var at = t.IndexOf('@');
        if (at <= 0 || at != t.LastIndexOf('@'))
        {
            return false;
        }

        var dot = t.LastIndexOf('.');
        return dot > at + 1 && dot < t.Length - 1;
    }

    private string GetPassword() =>
        ShowPasswordCheckBox.IsChecked == true ? PasswordPlainBox.Text : PasswordBox.Password;

    private string GetConfirmPassword() =>
        ShowPasswordCheckBox.IsChecked == true ? ConfirmPasswordPlainBox.Text : ConfirmPasswordBox.Password;

    private void ApplyShowPasswordUi()
    {
        var show = ShowPasswordCheckBox.IsChecked == true;
        if (show)
        {
            _suppressPasswordPlainTextChanged = true;
            _suppressConfirmPasswordPlainTextChanged = true;
            try
            {
                PasswordPlainBox.Text = PasswordBox.Password;
                ConfirmPasswordPlainBox.Text = ConfirmPasswordBox.Password;
            }
            finally
            {
                _suppressPasswordPlainTextChanged = false;
                _suppressConfirmPasswordPlainTextChanged = false;
            }

            PasswordPlainBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            ConfirmPasswordPlainBox.Visibility = Visibility.Visible;
            ConfirmPasswordBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            PasswordBox.Password = PasswordPlainBox.Text;
            ConfirmPasswordBox.Password = ConfirmPasswordPlainBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordPlainBox.Visibility = Visibility.Collapsed;
            ConfirmPasswordBox.Visibility = Visibility.Visible;
            ConfirmPasswordPlainBox.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowPasswordCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e)
    {
        ApplyShowPasswordUi();
        ValidateForm();
    }

    private void PasswordPlainBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPasswordPlainTextChanged)
        {
            return;
        }

        PasswordBox.Password = PasswordPlainBox.Text;
    }

    private void ConfirmPasswordPlainBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressConfirmPasswordPlainTextChanged)
        {
            return;
        }

        ConfirmPasswordBox.Password = ConfirmPasswordPlainBox.Text;
    }

    private void StartCapsLockWatcher()
    {
        _capsLockTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _capsLockTimer.Tick -= CapsLockTimerOnTick;
        _capsLockTimer.Tick += CapsLockTimerOnTick;
        _capsLockTimer.Start();
        UpdateCapsLockUi();
    }

    private void StopCapsLockWatcher()
    {
        if (_capsLockTimer is null)
        {
            return;
        }

        _capsLockTimer.Tick -= CapsLockTimerOnTick;
        _capsLockTimer.Stop();
        _capsLockTimer = null;
    }

    private void CapsLockTimerOnTick(object? sender, EventArgs e) => UpdateCapsLockUi();

    private void UpdateCapsLockUi()
    {
        var caps = Keyboard.IsKeyToggled(Key.CapsLock);
        var inPasswordArea = PasswordBox.IsKeyboardFocusWithin
                             || ConfirmPasswordBox.IsKeyboardFocusWithin
                             || PasswordPlainBox.IsKeyboardFocusWithin
                             || ConfirmPasswordPlainBox.IsKeyboardFocusWithin;
        CapsLockText.Visibility = (caps && inPasswordArea) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ValidateForm()
    {
        // Keep server errors from sticking while the user is correcting input.
        if (ErrorText.Visibility == Visibility.Visible)
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }

        var username = UsernameBox.Text.Trim();
        var first = FirstNameBox.Text.Trim();
        var last = LastNameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var password = GetPassword();
        var confirm = GetConfirmPassword();

        var requiredOk =
            username.Length > 0 &&
            first.Length > 0 &&
            last.Length > 0 &&
            LooksLikeEmail(email) &&
            password.Length >= 8 &&
            confirm.Length >= 1;

        var matchOk = password.Length > 0 && password == confirm;

        var dangerBrush = (System.Windows.Media.Brush?)TryFindResource("BrushDanger")
                          ?? System.Windows.Media.Brushes.IndianRed;
        var mutedBrush = (System.Windows.Media.Brush?)TryFindResource("BrushMuted")
                         ?? System.Windows.SystemColors.GrayTextBrush;

        if (!requiredOk)
        {
            PasswordStatusText.Foreground = (password.Length > 0 && password.Length < 8)
                ? dangerBrush
                : mutedBrush;
            PasswordStatusText.Text = "Use at least 8 characters. Make sure both passwords match.";
        }
        else if (!matchOk)
        {
            PasswordStatusText.Foreground = dangerBrush;
            PasswordStatusText.Text = "Passwords do not match.";
        }
        else
        {
            PasswordStatusText.Foreground = mutedBrush;
            PasswordStatusText.Text = "Looks good.";
        }

        RegisterButton.IsEnabled = requiredOk && matchOk;
        UpdateCapsLockUi();
    }

    private async Task RegisterAsync()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        ValidateForm();
        if (!RegisterButton.IsEnabled)
        {
            ShowError("Please complete the required fields and make sure passwords match.");
            return;
        }

        RegisterButton.IsEnabled = false;
        var prev = RegisterButton.Content;
        RegisterButton.Content = "Creating…";
        try
        {
            var req = new StaffRegistrationRequestDto
            {
                Username = UsernameBox.Text.Trim(),
                Password = GetPassword(),
                ConfirmPassword = GetConfirmPassword(),
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
