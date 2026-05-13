using System;
using System.Windows;

namespace HSMS.Desktop.Controls;

/// <summary>Looks like normal <c>__.__</c> 24-hour entry (no BI brackets); arrows adjust time.</summary>
public sealed class LoadRecordCycleEndTimeTextBox : BiMasked24hTimeTextBox
{
    public LoadRecordCycleEndTimeTextBox()
    {
        CycleEndArrowAdjustmentEnabled = true;
        MinHeight = 26;
        MaxHeight = 28;
        MinWidth = 56;
        FontSize = 12;
        Margin = new Thickness(6, 2, 6, 2);
        Padding = new Thickness(6, 4, 6, 4);
        HorizontalContentAlignment = HorizontalAlignment.Left;
        ToolTip = null;
    }

    protected override string FormatSlotsForUi(ReadOnlySpan<char> slots) =>
        BiLogMaskedTimeDigits.FormatPlainHmMask(slots);

    protected override int CaretIndexFromSlotDisplay(string display, int slotIndex) =>
        BiLogMaskedTimeDigits.PlainMaskCaretIndex(display, slotIndex);

    protected override int SlotIndexFromCaretPosition(string display, int caretIdx) =>
        BiLogMaskedTimeDigits.DigitInsertFromPlainMaskCaret(display, caretIdx);

    protected override void ClearLegacyCompanionFields()
    {
    }
}
