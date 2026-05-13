using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Color = System.Windows.Media.Color;
using HSMS.Shared.Contracts;

namespace HSMS.Desktop.Controls;

/// <summary>Fixed four-slot (H1 H2 M1 M2) 24-hour time entry; formatting on blur only.</summary>
public class BiMasked24hTimeTextBox : TextBox
{
    public static readonly DependencyProperty ValueHmProperty = DependencyProperty.Register(
        nameof(ValueHm),
        typeof(string),
        typeof(BiMasked24hTimeTextBox),
        new FrameworkPropertyMetadata(
            "",
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueHmChanged));

    public static readonly DependencyProperty IsTimeOutProperty = DependencyProperty.Register(
        nameof(IsTimeOut),
        typeof(bool),
        typeof(BiMasked24hTimeTextBox),
        new PropertyMetadata(false));

    /// <summary>When true, Up/Down adjust hours or minutes (by caret segment); PageUp/PageDown do not bubble to parent scrollers.</summary>
    public static readonly DependencyProperty CycleEndArrowAdjustmentEnabledProperty = DependencyProperty.Register(
        nameof(CycleEndArrowAdjustmentEnabled),
        typeof(bool),
        typeof(BiMasked24hTimeTextBox),
        new PropertyMetadata(false));

    /// <summary>Four fixed digit positions; '\0' = empty. No packing or shifting.</summary>
    private readonly char[] _slots = ['\0', '\0', '\0', '\0'];

    /// <summary>0–4: active slot or “after” last digit for caret.</summary>
    private int _cursorSlot;
    private bool _suppressDpSync;
    private int _mouseCaretSyncGeneration;

    public BiMasked24hTimeTextBox()
    {
        FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New");
        FontSize = 12;
        Padding = new Thickness(2, 0, 2, 0);
        MinWidth = 0;
        MinHeight = 28;
        VerticalContentAlignment = VerticalAlignment.Center;
        HorizontalContentAlignment = HorizontalAlignment.Center;
        IsReadOnly = true;
        MaxLength = 14;
        TabIndex = 0;
        BorderThickness = new Thickness(1);
        Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
        BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xD2, 0xE5));
        CaretBrush = Brushes.DodgerBlue;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;
    }

    public string ValueHm
    {
        get => (string)GetValue(ValueHmProperty);
        set => SetValue(ValueHmProperty, value);
    }

    public bool IsTimeOut
    {
        get => (bool)GetValue(IsTimeOutProperty);
        set => SetValue(IsTimeOutProperty, value);
    }

    public bool CycleEndArrowAdjustmentEnabled
    {
        get => (bool)GetValue(CycleEndArrowAdjustmentEnabledProperty);
        set => SetValue(CycleEndArrowAdjustmentEnabledProperty, value);
    }

    /// <summary>Blur-commit mask digits and push <see cref="ValueHm"/> to the source (DataGrid may end the cell before LostFocus).</summary>
    public void CommitToSource()
    {
        FinalizeForBinding();
        BindingOperations.GetBindingExpression(this, ValueHmProperty)?.UpdateSource();
    }

    private static void OnValueHmChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not BiMasked24hTimeTextBox tb)
        {
            return;
        }

        tb._suppressDpSync = true;
        try
        {
            tb.LoadFromHm(e.NewValue as string ?? "");
        }
        finally
        {
            tb._suppressDpSync = false;
        }
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        _mouseCaretSyncGeneration++;
        _cursorSlot = FirstEmptyOrEnd();
        RefreshText();
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        if (!IsLoaded || string.IsNullOrEmpty(Text))
        {
            return;
        }

        var gen = ++_mouseCaretSyncGeneration;
        var caretAtClick = CaretIndex;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (gen != _mouseCaretSyncGeneration || !IsLoaded)
                {
                    return;
                }

                var mapped = SlotIndexFromCaretPosition(Text, caretAtClick);
                _cursorSlot = ClampCursor(mapped);
                RefreshText();
            },
            DispatcherPriority.Input);
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        FinalizeForBinding();
        ClearLegacyCompanionFields();
        UpdateValidationChrome();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (AppendDigit(e)) return;
        if (HandleBackspace(e)) return;
        if (CycleEndArrowAdjustmentEnabled)
        {
            if (HandleArrowIncrement(e)) return;
            if (SuppressPagingKeys(e)) return;
        }

        if (HandleCaretMove(e)) return;
        if (HandleEnter(e)) return;

        if (IsNavigationSteal(e.Key))
        {
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private bool HandleArrowIncrement(KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down) || Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        _mouseCaretSyncGeneration++;
        e.Handled = true;
        var delta = e.Key == Key.Up ? 1 : -1;

        var totalMin = TryGetCurrentMinutesFromSlots(out var parsed) ? parsed : 0;
        var segment = Math.Clamp(_cursorSlot >= 4 ? 3 : _cursorSlot, 0, 3);
        if (segment <= 1)
        {
            totalMin += delta * 60;
        }
        else
        {
            totalMin += delta;
        }

        while (totalMin < 0)
        {
            totalMin += 24 * 60;
        }

        totalMin %= 24 * 60;

        ApplyMinutesToSlots(totalMin);
        _cursorSegmentAfterArrow(segment);
        RefreshText();
        UpdateValidationChrome();
        return true;
    }

    private void _cursorSegmentAfterArrow(int segment)
    {
        _cursorSlot = segment switch
        {
            0 or 1 => 1,
            _ => 3
        };
    }

    private static bool SuppressPagingKeys(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        if (e.Key is not (Key.PageUp or Key.PageDown))
        {
            return false;
        }

        e.Handled = true;
        return true;
    }

    private bool TryGetCurrentMinutesFromSlots(out int totalMin)
    {
        totalMin = 0;
        if (BiLogMaskedTimeDigits.AllSlotsEmpty(_slots))
        {
            return false;
        }

        if (BiLogMaskedTimeDigits.TryParseCompleteHmFromSlots(_slots, out var hh, out var mm))
        {
            totalMin = hh * 60 + mm;
            return true;
        }

        if (BiLogMaskedTimeDigits.TryBlurCommit(_slots, out var hm) && hm is { Length: > 0 } s)
        {
            var parts = s.Split(':');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h2) &&
                int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m2) &&
                h2 is >= 0 and <= 23 &&
                m2 is >= 0 and <= 59)
            {
                totalMin = h2 * 60 + m2;
                return true;
            }
        }

        return false;
    }

    private void ApplyMinutesToSlots(int totalMin)
    {
        var hh = totalMin / 60;
        var mm = totalMin % 60;
        _slots[0] = (char)('0' + hh / 10);
        _slots[1] = (char)('0' + hh % 10);
        _slots[2] = (char)('0' + mm / 10);
        _slots[3] = (char)('0' + mm % 10);
    }

    /// <summary>BI log clears parallel hour/minute combo fields; other hosts override as no-op.</summary>
    protected virtual void ClearLegacyCompanionFields()
    {
        if (DataContext is not BiLogSheetRowDto row)
        {
            return;
        }

        if (IsTimeOut)
        {
            row.BiTimeOutHour = "";
            row.BiTimeOutMinute = "";
        }
        else
        {
            row.BiTimeInHour = "";
            row.BiTimeInMinute = "";
        }
    }

    private static bool IsNavigationSteal(Key key) =>
        key is Key.Space or Key.Decimal or Key.OemSemicolon or Key.Oem2 or Key.Oem1;

    private bool AppendDigit(KeyEventArgs e)
    {
        var c = DigitFromKey(e.Key);
        if (c is null || Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        if (_cursorSlot >= 4)
        {
            _cursorSlot = 3;
        }

        _mouseCaretSyncGeneration++;
        e.Handled = true;
        var slot = Math.Clamp(_cursorSlot, 0, 3);
        _slots[slot] = c.Value;
        _cursorSlot = Math.Min(slot + 1, 4);
        RefreshText();
        UpdateValidationChrome();
        return true;
    }

    private static char? DigitFromKey(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
        >= Key.NumPad0 and <= Key.NumPad9 => (char)('0' + (key - Key.NumPad0)),
        _ => null
    };

    private bool HandleBackspace(KeyEventArgs e)
    {
        if (e.Key != Key.Back || Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        if (_cursorSlot == 0 && BiLogMaskedTimeDigits.IsEmptySlot(_slots[0]))
        {
            e.Handled = true;
            return true;
        }

        _mouseCaretSyncGeneration++;
        e.Handled = true;
        if (_cursorSlot == 0)
        {
            _slots[0] = '\0';
            RefreshText();
            UpdateValidationChrome();
            return true;
        }

        var del = _cursorSlot - 1;
        _slots[del] = '\0';
        _cursorSlot = del;
        RefreshText();
        UpdateValidationChrome();
        return true;
    }

    private bool HandleCaretMove(KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        switch (e.Key)
        {
            case Key.Left:
                _mouseCaretSyncGeneration++;
                e.Handled = true;
                _cursorSlot = ClampCursor(_cursorSlot - 1);
                RefreshText();
                return true;
            case Key.Right:
                _mouseCaretSyncGeneration++;
                e.Handled = true;
                _cursorSlot = ClampCursor(_cursorSlot + 1);
                RefreshText();
                return true;
            case Key.Home:
                _mouseCaretSyncGeneration++;
                e.Handled = true;
                _cursorSlot = 0;
                RefreshText();
                return true;
            case Key.End:
                _mouseCaretSyncGeneration++;
                e.Handled = true;
                _cursorSlot = 4;
                RefreshText();
                return true;
            default:
                return false;
        }
    }

    private bool HandleEnter(KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        e.Handled = true;
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        return true;
    }

    private void LoadFromHm(string hm)
    {
        ClearSlots();
        hm = hm.Trim();
        if (BiLogMaskedTimeDigits.TryParseHmDigitRun(hm, out var digs))
        {
            for (var i = 0; i < 4; i++)
            {
                _slots[i] = digs[i];
            }
        }

        _cursorSlot = FirstEmptyOrEnd();
        RefreshText();
        UpdateValidationChrome();
    }

    private void ClearSlots()
    {
        for (var i = 0; i < 4; i++)
        {
            _slots[i] = '\0';
        }
    }

    private int FirstEmptyOrEnd()
    {
        for (var i = 0; i < 4; i++)
        {
            if (BiLogMaskedTimeDigits.IsEmptySlot(_slots[i]))
            {
                return i;
            }
        }

        return 4;
    }

    private static int ClampCursor(int candidate) => Math.Clamp(candidate, 0, 4);

    private void RefreshText()
    {
        var display = FormatSlotsForUi(_slots);
        Text = display;
        CaretIndex = CaretIndexFromSlotDisplay(display, _cursorSlot);
    }

    /// <summary>BI sheets use bracketed mask; derived types can switch to plain <c>__:__</c>.</summary>
    protected virtual string FormatSlotsForUi(ReadOnlySpan<char> slots) =>
        BiLogMaskedTimeDigits.FormatMaskDisplay(slots);

    protected virtual int CaretIndexFromSlotDisplay(string display, int slotIndex) =>
        BiLogMaskedTimeDigits.MaskCaretIndex(display, slotIndex);

    protected virtual int SlotIndexFromCaretPosition(string display, int caretIdx) =>
        BiLogMaskedTimeDigits.DigitInsertFromMaskCaret(display, caretIdx);

    private void FinalizeForBinding()
    {
        if (BiLogMaskedTimeDigits.AllSlotsEmpty(_slots))
        {
            if (!_suppressDpSync)
            {
                ValueHm = "";
            }

            return;
        }

        if (BiLogMaskedTimeDigits.TryBlurCommit(_slots, out var hm) && hm is not null)
        {
            if (!_suppressDpSync)
            {
                ValueHm = hm;
            }
        }
        else if (!_suppressDpSync)
        {
            ValueHm = "";
        }
    }

    private void UpdateValidationChrome()
    {
        if (BiLogMaskedTimeDigits.AllSlotsEmpty(_slots))
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xD2, 0xE5));
            return;
        }

        if (BiLogMaskedTimeDigits.HasInteriorGap(_slots))
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            return;
        }

        var allFour = !BiLogMaskedTimeDigits.IsEmptySlot(_slots[0]) &&
                      !BiLogMaskedTimeDigits.IsEmptySlot(_slots[1]) &&
                      !BiLogMaskedTimeDigits.IsEmptySlot(_slots[2]) &&
                      !BiLogMaskedTimeDigits.IsEmptySlot(_slots[3]);

        var ok = allFour
            ? BiLogMaskedTimeDigits.TryParseCompleteHmFromSlots(_slots, out _, out _)
            : BiLogMaskedTimeDigits.TryBlurCommit(_slots, out _);

        BorderBrush = ok
            ? new SolidColorBrush(Color.FromRgb(0xC5, 0xD2, 0xE5))
            : new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
    }
}

/// <summary>Parsing + fixed-slot mask helpers for BI log time (24 h).</summary>
public static class BiLogMaskedTimeDigits
{
    public static bool IsEmptySlot(char c) => c is < '0' or > '9';

    public static bool AllSlotsEmpty(ReadOnlySpan<char> slots)
    {
        for (var i = 0; i < 4; i++)
        {
            if (!IsEmptySlot(slots[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True if a filled slot appears after an empty slot (invalid “gap”).</summary>
    public static bool HasInteriorGap(ReadOnlySpan<char> slots)
    {
        var first = -1;
        var last = -1;
        for (var i = 0; i < 4; i++)
        {
            if (!IsEmptySlot(slots[i]))
            {
                first = first < 0 ? i : first;
                last = i;
            }
        }

        if (first < 0)
        {
            return false;
        }

        for (var i = first; i <= last; i++)
        {
            if (IsEmptySlot(slots[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Parses existing <c>HH:mm</c> into four digit chars.</summary>
    public static bool TryParseHmDigitRun(string hm, out char[] digits)
    {
        digits = [];
        hm = hm.Trim();
        var parts = hm.Split(':');
        if (parts.Length != 2 ||
            parts[0].Length != 2 ||
            parts[1].Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m))
        {
            return false;
        }

        if (h is < 0 or > 23 || m is < 0 or > 59)
        {
            return false;
        }

        digits =
        [
            parts[0][0], parts[0][1],
            parts[1][0], parts[1][1]
        ];
        return true;
    }

    /// <summary>All four slots are digits and form a valid time.</summary>
    public static bool TryParseCompleteHmFromSlots(ReadOnlySpan<char> slots, out int hh, out int mm)
    {
        hh = 0;
        mm = 0;
        if (IsEmptySlot(slots[0]) || IsEmptySlot(slots[1]) || IsEmptySlot(slots[2]) || IsEmptySlot(slots[3]))
        {
            return false;
        }

        hh = (slots[0] - '0') * 10 + (slots[1] - '0');
        mm = (slots[2] - '0') * 10 + (slots[3] - '0');
        return hh is >= 0 and <= 23 && mm is >= 0 and <= 59;
    }

    /// <summary>Commit rules on blur only: <c>3</c>→<c>03:00</c>, <c>12:3_</c>→<c>12:30</c>, full <c>HH:mm</c> when four digits.</summary>
    public static bool TryBlurCommit(ReadOnlySpan<char> slots, out string? hm)
    {
        hm = null;
        if (AllSlotsEmpty(slots))
        {
            hm = "";
            return true;
        }

        if (HasInteriorGap(slots))
        {
            return false;
        }

        var e0 = IsEmptySlot(slots[0]);
        var e1 = IsEmptySlot(slots[1]);
        var e2 = IsEmptySlot(slots[2]);
        var e3 = IsEmptySlot(slots[3]);
        var n = (!e0 ? 1 : 0) + (!e1 ? 1 : 0) + (!e2 ? 1 : 0) + (!e3 ? 1 : 0);

        if (n == 4)
        {
            var hh = (slots[0] - '0') * 10 + (slots[1] - '0');
            var mm = (slots[2] - '0') * 10 + (slots[3] - '0');
            if (hh is < 0 or > 23 || mm is < 0 or > 59)
            {
                return false;
            }

            hm = $"{hh:00}:{mm:00}";
            return true;
        }

        if (n == 1 && !e0 && e1 && e2 && e3)
        {
            var d = slots[0] - '0';
            if (d is < 0 or > 9)
            {
                return false;
            }

            hm = $"{d:00}:00";
            return true;
        }

        if (n == 2 && !e0 && !e1 && e2 && e3)
        {
            var hh = (slots[0] - '0') * 10 + (slots[1] - '0');
            if (hh is < 0 or > 23)
            {
                return false;
            }

            hm = $"{hh:00}:00";
            return true;
        }

        if (n == 3 && !e0 && !e1 && !e2 && e3)
        {
            var hh = (slots[0] - '0') * 10 + (slots[1] - '0');
            var mm = (slots[2] - '0') * 10;
            if (hh is < 0 or > 23 || mm is < 0 or > 59)
            {
                return false;
            }

            hm = $"{hh:00}:{mm:00}";
            return true;
        }

        return false;
    }

    /// <summary>No brackets — reads like a normal 24-hour time placeholder in grids.</summary>
    public static string FormatPlainHmMask(ReadOnlySpan<char> slots)
    {
        static char Disp(char x) => IsEmptySlot(x) ? '_' : x;
        return $"{Disp(slots[0])}{Disp(slots[1])}:{Disp(slots[2])}{Disp(slots[3])}";
    }

    public static string FormatMaskDisplay(ReadOnlySpan<char> slots) => $"[ {FormatPlainHmMask(slots)} ]";

    /// <summary>Caret positions for <see cref="FormatPlainHmMask"/> (length 5: <c>__.__</c>).</summary>
    public static int PlainMaskCaretIndex(string masked, int slotIndex)
    {
        if (string.IsNullOrEmpty(masked))
        {
            return 0;
        }

        var colon = masked.IndexOf(':');
        if (colon <= 0)
        {
            return 0;
        }

        ReadOnlySpan<int> off = stackalloc int[] { 0, 1, colon + 1, colon + 2 };

        var clamped = Math.Clamp(slotIndex, 0, 4);
        if (clamped >= 4)
        {
            return masked.Length;
        }

        var i = off[clamped];
        return Math.Clamp(i, 0, Math.Max(0, masked.Length - 1));
    }

    /// <summary>Maps caret position in plain mask to digit slot.</summary>
    public static int DigitInsertFromPlainMaskCaret(string masked, int caretIdx)
    {
        if (string.IsNullOrEmpty(masked))
        {
            return 0;
        }

        var colonIdx = masked.IndexOf(':');
        if (colonIdx < 1)
        {
            return 0;
        }

        var c = Math.Clamp(caretIdx, 0, masked.Length);

        ReadOnlySpan<int> digitPos =
        [
            colonIdx - 2,
            colonIdx - 1,
            colonIdx + 1,
            colonIdx + 2
        ];

        if (c <= digitPos[0])
        {
            return 0;
        }

        if (c <= digitPos[1])
        {
            return 1;
        }

        if (c <= digitPos[2])
        {
            return 2;
        }

        if (c <= digitPos[3])
        {
            return 3;
        }

        return 4;
    }

    /// <summary>Maps a character offset in the mask to slot index 0–4 from click / CaretIndex.</summary>
    public static int DigitInsertFromMaskCaret(string masked, int caretIdx)
    {
        if (string.IsNullOrEmpty(masked))
        {
            return 0;
        }

        var c = Math.Clamp(caretIdx, 0, masked.Length);
        var colonIdx = masked.IndexOf(':');
        if (colonIdx < 2)
        {
            return 0;
        }

        ReadOnlySpan<int> slot =
        [
            colonIdx - 2,
            colonIdx - 1,
            colonIdx + 1,
            colonIdx + 2
        ];

        if (c <= slot[0])
        {
            return 0;
        }

        if (c <= slot[1])
        {
            return 1;
        }

        if (c <= slot[2])
        {
            return 2;
        }

        if (c <= slot[3])
        {
            return 3;
        }

        return 4;
    }

    public static int MaskCaretIndex(string masked, int slotIndex)
    {
        if (string.IsNullOrEmpty(masked))
        {
            return 0;
        }

        var clamped = Math.Clamp(slotIndex, 0, 4);
        ReadOnlySpan<int> slotOffsets = stackalloc int[] { 2, 3, 5, 6 };
        if (clamped >= 4)
        {
            var close = masked.LastIndexOf(']');
            return close >= 1 ? close - 1 : Math.Max(0, masked.Length - 1);
        }

        var i = slotOffsets[clamped];
        return i < masked.Length ? i : masked.Length - 1;
    }

    /// <summary>Test helper: four digit chars → HH:mm after complete entry.</summary>
    public static string? NormalizeDigitInput(string rawDigits)
    {
        var digs = rawDigits.Where(char.IsDigit).Take(4).ToArray();
        if (digs.Length != 4)
        {
            return null;
        }

        Span<char> s = stackalloc char[4];
        for (var i = 0; i < 4; i++)
        {
            s[i] = digs[i];
        }

        return TryBlurCommit(s, out var hm) ? hm : null;
    }
}
