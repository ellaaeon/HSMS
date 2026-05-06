using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HSMS.Desktop.Controls;

public partial class BiPlusMinusSignToggle : UserControl
{
    private static readonly SolidColorBrush PlusBg = new(Color.FromRgb(0xDC, 0xFC, 0xE7));
    private static readonly SolidColorBrush PlusBorder = new(Color.FromRgb(0x22, 0xC5, 0x5E));
    private static readonly SolidColorBrush MinusBg = new(Color.FromRgb(0xFE, 0xE2, 0xE2));
    private static readonly SolidColorBrush MinusBorder = new(Color.FromRgb(0xEF, 0x44, 0x44));
    private static readonly SolidColorBrush IdleBg = new(Color.FromRgb(0xF1, 0xF5, 0xF9));
    private static readonly SolidColorBrush IdleBorder = new(Color.FromRgb(0xCB, 0xD5, 0xE1));

    static BiPlusMinusSignToggle()
    {
        PlusBg.Freeze();
        PlusBorder.Freeze();
        MinusBg.Freeze();
        MinusBorder.Freeze();
        IdleBg.Freeze();
        IdleBorder.Freeze();
    }

    public static readonly DependencyProperty SignProperty = DependencyProperty.Register(
        nameof(Sign),
        typeof(string),
        typeof(BiPlusMinusSignToggle),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSignChanged));

    public BiPlusMinusSignToggle()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshChrome();
    }

    public string Sign
    {
        get => (string)GetValue(SignProperty);
        set => SetValue(SignProperty, value);
    }

    public void FocusFirst()
    {
        ToggleBtn.Focus();
    }

    private static void OnSignChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BiPlusMinusSignToggle s)
        {
            s.RefreshChrome();
        }
    }

    private void RefreshChrome()
    {
        var s = (Sign ?? "").Trim();
        if (s != "+" && s != "-")
        {
            ToggleBtn.Content = "±";
            ToggleBtn.Background = IdleBg;
            ToggleBtn.BorderBrush = IdleBorder;
            return;
        }

        // Use U+2212 (true minus) for better visibility than hyphen-minus.
        ToggleBtn.Content = s == "+" ? "+" : "−";
        ToggleBtn.Background = s == "+" ? PlusBg : MinusBg;
        ToggleBtn.BorderBrush = s == "+" ? PlusBorder : MinusBorder;
    }

    private void ToggleBtn_OnClick(object sender, RoutedEventArgs e)
    {
        var s = (Sign ?? "").Trim();
        Sign = s == "+" ? "-" : s == "-" ? "" : "+";
    }

    private void Sign_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Add:
            case Key.OemPlus:
                Sign = "+";
                e.Handled = true;
                break;
            case Key.Subtract:
            case Key.OemMinus:
                Sign = "-";
                e.Handled = true;
                break;
            case Key.Space:
                ToggleBtn_OnClick(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Enter:
                (sender as Control)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
                break;
        }
    }
}

