using System.Windows;
using System.Windows.Media;

namespace HSMS.Desktop.Ui;

public partial class HsmsAlertWindow : Window
{
    public HsmsAlertWindow(HsmsAlertModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    private void OkButton_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    public static void ShowSuccess(Window owner, string message, string? title = null)
    {
        var win = new HsmsAlertWindow(new HsmsAlertModel
        {
            TitleText = title ?? "Saved",
            MessageText = message,
            IconGlyph = "✓",
            AccentBackground = new SolidColorBrush(Color.FromRgb(0xE7, 0xF7, 0xEE)),
            AccentForeground = (Brush)owner.FindResource("BrushSuccess")
        })
        { Owner = owner };
        win.ShowDialog();
    }

    public static void ShowInfo(Window owner, string message, string? title = null)
    {
        var win = new HsmsAlertWindow(new HsmsAlertModel
        {
            TitleText = title ?? "Info",
            MessageText = message,
            IconGlyph = "i",
            AccentBackground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFC)),
            AccentForeground = (Brush)owner.FindResource("BrushPrimary")
        })
        { Owner = owner };
        win.ShowDialog();
    }

    public static void ShowWarning(Window owner, string message, string? title = null)
    {
        var win = new HsmsAlertWindow(new HsmsAlertModel
        {
            TitleText = title ?? "Attention",
            MessageText = message,
            IconGlyph = "!",
            AccentBackground = new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xE5)),
            AccentForeground = new SolidColorBrush(Color.FromRgb(0xB5, 0x47, 0x08))
        })
        { Owner = owner };
        win.ShowDialog();
    }
}

public sealed class HsmsAlertModel
{
    public string TitleText { get; set; } = "HSMS";
    public string MessageText { get; set; } = string.Empty;
    public string IconGlyph { get; set; } = "i";
    public Brush AccentBackground { get; set; } = Brushes.LightGray;
    public Brush AccentForeground { get; set; } = Brushes.Black;
}

