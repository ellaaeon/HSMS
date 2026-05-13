using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HSMS.Desktop.Ui;

/// <summary>
/// Quick-entry keyboard behavior: Enter advances focus; Left/Right move between fields only at text boundaries.
/// Editable <see cref="ComboBox"/> navigation is attached to <c>PART_EditableTextBox</c> so typing/backspace/caret
/// movement behave normally. <see cref="RoutedEventArgs.Handled"/> is set only when this behavior actually overrides
/// the key (never globally for unrelated keys).
/// </summary>
public static class QuickEntryNavigationBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(QuickEntryNavigationBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    private static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached(
            "State",
            typeof(State),
            typeof(QuickEntryNavigationBehavior),
            new PropertyMetadata(null));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe)
        {
            return;
        }

        var enabled = e.NewValue is true;
        if (enabled)
        {
            var state = new State();
            fe.SetValue(StateProperty, state);
            Attach(fe, state);
        }
        else
        {
            if (fe.GetValue(StateProperty) is State state)
            {
                Detach(fe, state);
            }

            fe.ClearValue(StateProperty);
        }
    }

    private static void Attach(FrameworkElement fe, State state)
    {
        fe.PreviewKeyDown += state.OnHostPreviewKeyDown;

        if (fe is ComboBox cb)
        {
            cb.Loaded += state.OnComboLoaded;
            cb.Unloaded += state.OnComboUnloaded;
        }
    }

    private static void Detach(FrameworkElement fe, State state)
    {
        fe.PreviewKeyDown -= state.OnHostPreviewKeyDown;

        if (fe is ComboBox cb)
        {
            cb.Loaded -= state.OnComboLoaded;
            cb.Unloaded -= state.OnComboUnloaded;
        }

        state.DetachComboInnerTextBox();
    }

    #region Navigation primitives (shared, no control-specific rules)

    /// <summary>Attempts focus traversal; returns whether focus actually moved.</summary>
    private static bool TryMoveFocus(UIElement? current, FocusNavigationDirection direction)
    {
        if (current is null)
        {
            return false;
        }

        return current.MoveFocus(new TraversalRequest(direction));
    }

    /// <summary>True when the caret is at the start and there is no active selection.</summary>
    private static bool IsCaretAtStartWithNoSelection(TextBox tb)
    {
        return tb.SelectionLength == 0 && tb.CaretIndex == 0;
    }

    /// <summary>True when the caret is at the end and there is no active selection.</summary>
    private static bool IsCaretAtEndWithNoSelection(TextBox tb)
    {
        return tb.SelectionLength == 0 && tb.CaretIndex == tb.Text.Length;
    }

    #endregion

    private sealed class State
    {
        private bool _suppressTextSync;
        private WeakReference<ComboBox>? _comboRef;
        private WeakReference<TextBox>? _innerTextRef;

        public void OnComboLoaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox cb)
            {
                return;
            }

            _comboRef = new WeakReference<ComboBox>(cb);

            // Template may not be applied yet; one deferred pass is enough.
            TryAttachComboInnerTextBox(cb, allowDeferOnce: true);
        }

        public void OnComboUnloaded(object? sender, RoutedEventArgs e)
        {
            DetachComboInnerTextBox();
            _comboRef = null;
        }

        /// <summary>
        /// Host-level handler: used for plain <see cref="TextBox"/> and non-editable <see cref="ComboBox"/>.
        /// For <c>IsEditable=true</c> combo boxes this must NOT run for navigation keys: <see cref="PreviewKeyDown"/>
        /// tunnels from root toward the focused inner text box, so this runs first and would incorrectly swallow
        /// Enter/arrows before the inner <see cref="TextBox"/> sees them.
        /// </summary>
        public void OnHostPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            // --- Editable ComboBox: navigation + Enter live on PART_EditableTextBox only ---
            if (sender is ComboBox cbHost && cbHost.IsEditable)
            {
                return;
            }

            // --- Non-editable ComboBox (if ever enabled): no caret; keep simple field-to-field navigation ---
            if (sender is ComboBox cbDrop && cbDrop.IsDropDownOpen &&
                e.Key is Key.Enter or Key.Return or Key.Left or Key.Right)
            {
                return;
            }

            // Note: Key.Return duplicates Key.Enter in WPF; test with a single branch.
            if (e.Key is Key.Enter or Key.Return)
            {
                if (TryMoveFocus(sender as UIElement, FocusNavigationDirection.Next))
                {
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Right)
            {
                if (sender is TextBox tbR && !IsCaretAtEndWithNoSelection(tbR))
                {
                    return;
                }

                if (TryMoveFocus(sender as UIElement, FocusNavigationDirection.Next))
                {
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Left)
            {
                if (sender is TextBox tbL && !IsCaretAtStartWithNoSelection(tbL))
                {
                    return;
                }

                if (TryMoveFocus(sender as UIElement, FocusNavigationDirection.Previous))
                {
                    e.Handled = true;
                }
            }
        }

        private void TryAttachComboInnerTextBox(ComboBox cb, bool allowDeferOnce)
        {
            if (!cb.IsEditable)
            {
                return;
            }

            cb.ApplyTemplate();
            var inner = cb.Template?.FindName("PART_EditableTextBox", cb) as TextBox;
            if (inner is null)
            {
                if (!allowDeferOnce)
                {
                    return;
                }

                cb.Dispatcher.BeginInvoke(
                    () => TryAttachComboInnerTextBox(cb, allowDeferOnce: false),
                    System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            if (_innerTextRef is not null && _innerTextRef.TryGetTarget(out var existing) && ReferenceEquals(existing, inner))
            {
                return;
            }

            DetachComboInnerTextBox();

            _innerTextRef = new WeakReference<TextBox>(inner);
            inner.PreviewKeyDown += InnerEditableTextBox_OnPreviewKeyDown;
            inner.TextChanged += InnerTextBox_OnTextChanged;
        }

        public void DetachComboInnerTextBox()
        {
            if (_innerTextRef is null)
            {
                return;
            }

            if (_innerTextRef.TryGetTarget(out var inner))
            {
                inner.PreviewKeyDown -= InnerEditableTextBox_OnPreviewKeyDown;
                inner.TextChanged -= InnerTextBox_OnTextChanged;
            }

            _innerTextRef = null;
        }

        /// <summary>
        /// Key handling for the combo's editable surface. Only handles Enter / Left / Right when we intentionally
        /// override default behavior; all other keys (Backspace, Delete, typing, etc.) are untouched.
        /// </summary>
        private void InnerEditableTextBox_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (_comboRef is null || !_comboRef.TryGetTarget(out var cb))
            {
                return;
            }

            if (sender is not TextBox inner)
            {
                return;
            }

            // While the dropdown is open, preserve WPF defaults (Enter selects highlighted row; arrows navigate list).
            if (cb.IsDropDownOpen)
            {
                return;
            }

            if (e.Key == Key.Right)
            {
                if (!IsCaretAtEndWithNoSelection(inner))
                {
                    return;
                }

                if (TryMoveFocus(inner, FocusNavigationDirection.Next))
                {
                    e.Handled = true;
                }

                return;
            }

            if (e.Key == Key.Left)
            {
                if (!IsCaretAtStartWithNoSelection(inner))
                {
                    return;
                }

                if (TryMoveFocus(inner, FocusNavigationDirection.Previous))
                {
                    e.Handled = true;
                }

                return;
            }

            if (e.Key is Key.Enter or Key.Return)
            {
                // Closed dropdown: commit best item match for the typed text, then advance if possible.
                    CommitBestMatch(cb);
                    SyncInnerTextFromCombo(inner, cb);

                if (TryMoveFocus(inner, FocusNavigationDirection.Next))
                {
                    // Prevent ding / inner single-line quirks only when navigation actually occurs.
                    e.Handled = true;
                }
            }
        }

        /// <summary>Keeps the inner text selection/caret coherent after programmatic Text/SelectedItem updates.</summary>
        private static void SyncInnerTextFromCombo(TextBox inner, ComboBox cb)
        {
            var text = cb.Text ?? string.Empty;
            if (!string.Equals(inner.Text, text, StringComparison.Ordinal))
            {
                inner.Text = text;
            }

            inner.CaretIndex = text.Length;
            inner.SelectionLength = 0;
        }

        #region Auto-match / type-ahead (input logic, separate from navigation)

        private void InnerTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressTextSync)
            {
                return;
            }

            if (_comboRef is null || !_comboRef.TryGetTarget(out var cb))
            {
                return;
            }

            if (cb.IsDropDownOpen)
            {
                return;
            }

            // Critical: never auto-expand on pure deletion (otherwise Backspace "doesn't work" — text keeps growing back).
            if (!TextChangedAddsCharacters(e))
            {
                return;
            }

            if (sender is not TextBox inner)
            {
                return;
            }

            // Only suggest when typing forward at end with no selection; don't fight editing in the middle.
            if (inner.SelectionLength > 0 || inner.CaretIndex != inner.Text.Length)
            {
                return;
            }

            var typed = (inner.Text ?? string.Empty).TrimStart();
            if (typed.Length == 0)
            {
                return;
            }

            if (!TryFindPrefixMatch(cb, typed, out var matchItem, out var matchText))
            {
                return;
            }

            _suppressTextSync = true;
            try
            {
                cb.SelectedItem = matchItem;
                cb.Text = matchText;
                inner.Text = matchText;
                inner.SelectionStart = typed.Length;
                inner.SelectionLength = Math.Max(0, matchText.Length - typed.Length);
            }
            finally
            {
                _suppressTextSync = false;
            }
        }

        private static bool TextChangedAddsCharacters(TextChangedEventArgs e)
        {
            foreach (var change in e.Changes)
            {
                if (change.AddedLength > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CommitBestMatch(ComboBox combo)
        {
            if (!combo.IsEditable)
            {
                return;
            }

            var raw = combo.Text ?? string.Empty;
            var typed = raw.Trim();
            if (typed.Length == 0)
            {
                return;
            }

            if (TryFindExactMatch(combo, typed, out var exactItem, out var exactText))
            {
                combo.SelectedItem = exactItem;
                combo.Text = exactText;
                return;
            }

            if (TryFindPrefixMatch(combo, typed, out var prefixItem, out var prefixText))
            {
                combo.SelectedItem = prefixItem;
                combo.Text = prefixText;
            }
        }

        private static bool TryFindExactMatch(ComboBox combo, string typed, out object? matchItem, out string matchText)
        {
            matchItem = null;
            matchText = typed;

            foreach (var (item, text) in EnumerateItemTexts(combo))
            {
                if (string.Equals(text, typed, StringComparison.OrdinalIgnoreCase))
                {
                    matchItem = item;
                    matchText = text;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindPrefixMatch(ComboBox combo, string typed, out object? matchItem, out string matchText)
        {
            matchItem = null;
            matchText = typed;

            foreach (var (item, text) in EnumerateItemTexts(combo))
            {
                if (text.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                {
                    matchItem = item;
                    matchText = text;
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<(object item, string text)> EnumerateItemTexts(ComboBox combo)
        {
            foreach (var item in combo.Items)
            {
                if (item is null)
                {
                    continue;
                }

                var t = item is DependencyObject dob ? TextSearch.GetText(dob) : null;
                if (string.IsNullOrWhiteSpace(t))
                {
                    t = item.ToString() ?? string.Empty;
                }

                t = t.Trim();
                if (t.Length == 0)
                {
                    continue;
                }

                yield return (item, t);
            }
        }

        #endregion
    }
}
