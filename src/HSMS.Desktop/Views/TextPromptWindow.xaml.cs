using System.Windows;

namespace HSMS.Desktop.Views;

public partial class TextPromptWindow : Window
{
    public string? ResultText { get; private set; }

    public TextPromptWindow(string title, string prompt, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialValue ?? "";
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        ResultText = InputBox.Text?.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

