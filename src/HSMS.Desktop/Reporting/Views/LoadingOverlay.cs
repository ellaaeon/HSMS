using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HSMS.Desktop.Reporting.Views;

/// <summary>
/// Lightweight modal "Generating report…" overlay. Owner-modal, doesn't pull in any extra dependencies.
/// </summary>
public sealed class LoadingOverlay
{
    private readonly Window _window;

    private LoadingOverlay(Window window)
    {
        _window = window;
    }

    public static LoadingOverlay Show(Window? owner, string message)
    {
        var grid = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0))
        };

        var card = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(24, 18, 24, 18),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold
        });
        stack.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Height = 6,
            Margin = new Thickness(0, 8, 0, 0),
            Width = 220
        });
        card.Child = stack;
        grid.Children.Add(card);

        var window = new Window
        {
            Owner = owner,
            Width = 280,
            Height = 110,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            Topmost = true,
            Content = grid
        };

        window.Show();
        return new LoadingOverlay(window);
    }

    public void Close()
    {
        if (!_window.Dispatcher.CheckAccess())
        {
            _window.Dispatcher.Invoke(() => _window.Close());
        }
        else
        {
            _window.Close();
        }
    }
}
