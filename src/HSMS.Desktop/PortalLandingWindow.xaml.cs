using System.Windows;

namespace HSMS.Desktop;

public enum PortalOutcome
{
    Cancelled,
    SignIn,
    CreateAccount
}

public partial class PortalLandingWindow : Window
{
    public PortalOutcome Outcome { get; private set; } = PortalOutcome.Cancelled;

    public PortalLandingWindow(bool allowSelfRegistration)
    {
        InitializeComponent();

        if (!allowSelfRegistration)
        {
            CreateAccountButton.Visibility = Visibility.Collapsed;
            RegistrationDisabledText.Visibility = Visibility.Visible;
        }

        SignInButton.Click += (_, _) =>
        {
            Outcome = PortalOutcome.SignIn;
            DialogResult = true;
            Close();
        };

        CreateAccountButton.Click += (_, _) =>
        {
            Outcome = PortalOutcome.CreateAccount;
            DialogResult = true;
            Close();
        };
    }
}
