using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using HSMS.Application.Services;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop;

public partial class LoginWindow : Window
{
    private readonly HsmsAuthService _auth;
    private readonly bool _allowSelfRegistration;
    private bool _suppressPasswordPlainTextChanged;

    public LoginResponseDto? Result { get; private set; }

    /// <summary>Optional username to pre-fill.</summary>
    public string? PrefillUsername { get; set; }

    public LoginWindow(HsmsAuthService auth, bool allowSelfRegistration = true)
    {
        InitializeComponent();
        _auth = auth;
        _allowSelfRegistration = allowSelfRegistration;
        SignInSubtitleText.Text = allowSelfRegistration
            ? "Use your username and password. New users: use Create account (under your password) before Sign in."
            : "Use your username and password. Contact an administrator if you need access.";
        if (!_allowSelfRegistration)
        {
            RegistrationFooter.Visibility = Visibility.Collapsed;
        }
        Loaded += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(PrefillUsername))
            {
                UsernameBox.Text = PrefillUsername;
                PasswordBox.Focus();
            }
            else
            {
                UsernameBox.Focus();
            }
        };
        LoginButton.Click += async (_, _) => await TryLoginAsync();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        };
    }

    private void CreateAccountButton_OnClick(object sender, RoutedEventArgs e)
    {
        var reg = new CreateAccountWindow(_auth) { Owner = this };
        if (reg.ShowDialog() == true && reg.RegisteredSession is not null)
        {
            Result = reg.RegisteredSession;
            DialogResult = true;
            Close();
        }
    }

    private string GetPasswordForLogin()
    {
        return ShowPasswordCheckBox.IsChecked == true
            ? PasswordPlainBox.Text
            : PasswordBox.Password;
    }

    private void ApplyShowPasswordUi()
    {
        var show = ShowPasswordCheckBox.IsChecked == true;
        if (show)
        {
            _suppressPasswordPlainTextChanged = true;
            try
            {
                PasswordPlainBox.Text = PasswordBox.Password;
            }
            finally
            {
                _suppressPasswordPlainTextChanged = false;
            }

            PasswordPlainBox.Visibility = Visibility.Visible;
            PasswordBox.Visibility = Visibility.Collapsed;
            PasswordPlainBox.Focus();
            PasswordPlainBox.CaretIndex = PasswordPlainBox.Text.Length;
        }
        else
        {
            PasswordBox.Password = PasswordPlainBox.Text;
            PasswordBox.Visibility = Visibility.Visible;
            PasswordPlainBox.Visibility = Visibility.Collapsed;
            PasswordBox.Focus();
        }
    }

    private void ShowPasswordCheckBox_OnCheckedChanged(object sender, RoutedEventArgs e) => ApplyShowPasswordUi();

    private void PasswordPlainBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPasswordPlainTextChanged)
        {
            return;
        }

        PasswordBox.Password = PasswordPlainBox.Text;
    }

    private async Task TryLoginAsync()
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var username = UsernameBox.Text.Trim();
        var password = GetPasswordForLogin();
        if (username.Length < 1 || password.Length < 1)
        {
            ShowError("Please enter username and password.");
            return;
        }

        LoginButton.IsEnabled = false;
        var previousButtonContent = LoginButton.Content;
        LoginButton.Content = "Signing in…";
        try
        {
            var (ok, dto, err) = await _auth.LoginAsync(new LoginRequestDto
            {
                Username = username,
                Password = password,
                ClientMachine = Environment.MachineName
            });

            if (!ok || dto is null)
            {
                ShowError(err ?? "Sign in failed.");
                return;
            }

            if (string.IsNullOrEmpty(dto.AccessToken))
            {
                ShowError("Invalid sign-in response.");
                return;
            }

            Result = dto;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            var detail = ex is DbUpdateException dbe && dbe.InnerException is not null
                ? dbe.InnerException.Message
                : ex.InnerException?.Message ?? ex.Message;
            ShowError($"Cannot complete sign-in. {detail}");
        }
        finally
        {
            LoginButton.Content = previousButtonContent;
            LoginButton.IsEnabled = true;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
