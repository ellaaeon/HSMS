namespace HSMS.Desktop.Ui;

public sealed class AnalyticsBarRow
{
    public string Key { get; init; } = string.Empty;
    public int Count { get; init; }

    /// <summary>Pixels for the bar fill width.</summary>
    public double BarWidth { get; init; }
}

