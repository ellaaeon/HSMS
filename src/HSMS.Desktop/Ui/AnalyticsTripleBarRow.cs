namespace HSMS.Desktop.Ui;

public sealed class AnalyticsTripleBarRow
{
    public string Key { get; init; } = string.Empty;

    public int Loads { get; init; }
    public int Pcs { get; init; }
    public int Qty { get; init; }

    public double LoadsBarWidth { get; init; }
    public double PcsBarWidth { get; init; }
    public double QtyBarWidth { get; init; }
}

