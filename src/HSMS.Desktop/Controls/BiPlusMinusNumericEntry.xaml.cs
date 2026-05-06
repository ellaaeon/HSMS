using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HSMS.Desktop.Controls;

public partial class BiPlusMinusNumericEntry : UserControl
{
    private static readonly SolidColorBrush IdleSignBg;
    private static readonly SolidColorBrush IdleSignBorder;
    private static readonly SolidColorBrush PlusActiveBg;
    private static readonly SolidColorBrush PlusActiveBorder;
    private static readonly SolidColorBrush MinusActiveBg;
    private static readonly SolidColorBrush MinusActiveBorder;

    private bool _syncText;

    static BiPlusMinusNumericEntry()
    {
        IdleSignBg = new SolidColorBrush(Color.FromRgb(0xEE, 0xF2, 0xF6));
        IdleSignBorder = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
        PlusActiveBg = new SolidColorBrush(Color.FromRgb(0xDC, 0xFC, 0xE7));
        PlusActiveBorder = new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E));
        MinusActiveBg = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
        MinusActiveBorder = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
        IdleSignBg.Freeze();
        IdleSignBorder.Freeze();
        PlusActiveBg.Freeze();
        PlusActiveBorder.Freeze();
        MinusActiveBg.Freeze();
        MinusActiveBorder.Freeze();
    }

    public static readonly DependencyProperty SignProperty = DependencyProperty.Register(
        nameof(Sign),
        typeof(string),
        typeof(BiPlusMinusNumericEntry),
        new FrameworkPropertyMetadata(
            "",
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSignChanged,
            CoerceSign));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(int?),
        typeof(BiPlusMinusNumericEntry),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged));

    public static readonly DependencyProperty MaxValueProperty = DependencyProperty.Register(
        nameof(MaxValue),
        typeof(int),
        typeof(BiPlusMinusNumericEntry),
        new PropertyMetadata(HSMS.Shared.Contracts.BiLogSheetUpdateValidator.MaxBiPlusMinusValue));

    public BiPlusMinusNumericEntry()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            RefreshSignChrome();
            RefreshPlaceholder();
        };
    }

    public string Sign
    {
        get => (string)GetValue(SignProperty);
        set => SetValue(SignProperty, value);
    }

    public int? Value
    {
        get => (int?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int MaxValue
    {
        get => (int)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public void FocusNumericField()
    {
        NumericText.Focus();
        NumericText.SelectAll();
    }

    private static object CoerceSign(DependencyObject _, object baseValue) => baseValue as string ?? "";

    private static void OnSignChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is BiPlusMinusNumericEntry c)
        {
            c.RefreshSignChrome();
        }
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BiPlusMinusNumericEntry c)
        {
            return;
        }

        c.PushValueToTextBox();
        c.RefreshPlaceholder();
    }

    private void RefreshSignChrome()
    {
        var s = (Sign ?? "").Trim();
        var sign = s == "-" ? "-" : "+";
        SignToggleButton.Content = sign;
        SetSignToggleVisual(activeMinus: sign == "-");
    }

    private void SetSignToggleVisual(bool activeMinus)
    {
        SignToggleButton.Background = activeMinus ? MinusActiveBg : PlusActiveBg;
        SignToggleButton.BorderBrush = activeMinus ? MinusActiveBorder : PlusActiveBorder;
    }

    private void PushValueToTextBox()
    {
        _syncText = true;
        try
        {
            NumericText.Text = Value is { } v ? v.ToString(CultureInfo.InvariantCulture) : "";
        }
        finally
        {
            _syncText = false;
        }
    }

    private void RefreshPlaceholder()
    {
        var show = string.IsNullOrEmpty(NumericText.Text) && !NumericText.IsKeyboardFocusWithin;
        PlaceholderZero.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SignToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        var s = (Sign ?? "").Trim();
        if (s != "-" && s != "+")
        {
            s = "+";
        }

        Sign = s == "-" ? "+" : "-";
    }

    private void ApplyDelta(int delta)
    {
        var max = MaxValue;
        var v = Value ?? 0;
        var next = v + delta;
        if (next < 0)
        {
            next = 0;
        }

        if (next > max)
        {
            next = max;
        }

        if (next == v)
        {
            return;
        }

        Value = next;
    }

    private void NumericText_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Length == 0 || !Regex.IsMatch(e.Text, "^[0-9]+$");
    }

    private void NumericText_OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        var t = e.DataObject.GetData(DataFormats.Text) as string ?? "";
        if (!Regex.IsMatch(t, "^[0-9]*$"))
        {
            e.CancelCommand();
        }
    }

    private void NumericText_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshPlaceholder();
        if (_syncText)
        {
            return;
        }

        var raw = NumericText.Text.Trim();
        if (raw.Length == 0)
        {
            Value = null;
            // No explicit clear button; clearing the number also clears sign.
            Sign = "";
            return;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            return;
        }

        var max = MaxValue;
        if (n > max)
        {
            n = max;
            _syncText = true;
            try
            {
                NumericText.Text = n.ToString(CultureInfo.InvariantCulture);
                NumericText.CaretIndex = NumericText.Text.Length;
            }
            finally
            {
                _syncText = false;
            }
        }

        Value = n;
        if (string.IsNullOrWhiteSpace(Sign))
        {
            Sign = "+";
        }
    }

    private void NumericText_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        PlaceholderZero.Visibility = Visibility.Collapsed;
        NumericText.SelectAll();
    }

    private void NumericText_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!NumericText.IsKeyboardFocusWithin)
        {
            e.Handled = true;
            NumericText.Focus();
            NumericText.SelectAll();
        }
    }

    private void NumericText_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        CommitNumericTextParse();
        RefreshPlaceholder();
    }

    private void CommitNumericTextParse()
    {
        var raw = NumericText.Text.Trim();
        if (raw.Length == 0)
        {
            Value = null;
            PushValueToTextBox();
            return;
        }

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
        {
            PushValueToTextBox();
            return;
        }

        var max = MaxValue;
        if (n < 0)
        {
            n = 0;
        }

        if (n > max)
        {
            n = max;
        }

        Value = n;
        PushValueToTextBox();
    }

    private void NumericText_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                ApplyDelta(1);
                e.Handled = true;
                break;
            case Key.Down:
                ApplyDelta(-1);
                e.Handled = true;
                break;
            case Key.Space:
                SignToggleButton_OnClick(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.OemMinus:
            case Key.Subtract:
                SignToggleButton_OnClick(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Enter:
                CommitNumericTextParse();
                _ = NumericText.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
                break;
        }
    }
}
