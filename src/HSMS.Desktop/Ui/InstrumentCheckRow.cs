using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HSMS.Desktop.Ui;

public sealed class InstrumentCheckRow : INotifyPropertyChanged
{
    private DateTime _checkedAtLocal = DateTime.Now;
    private string _itemName = string.Empty;
    private string _serialReference = string.Empty;
    private string _checkedBy = string.Empty;
    private string _witnessBy = string.Empty;
    private string _remarks = string.Empty;

    public DateTime CheckedAtLocal
    {
        get => _checkedAtLocal;
        set { _checkedAtLocal = value; OnPropertyChanged(); }
    }

    public string ItemName
    {
        get => _itemName;
        set { _itemName = value; OnPropertyChanged(); }
    }

    public string SerialReference
    {
        get => _serialReference;
        set { _serialReference = value; OnPropertyChanged(); }
    }

    public string CheckedBy
    {
        get => _checkedBy;
        set { _checkedBy = value; OnPropertyChanged(); }
    }

    public string WitnessBy
    {
        get => _witnessBy;
        set { _witnessBy = value; OnPropertyChanged(); }
    }

    public string Remarks
    {
        get => _remarks;
        set { _remarks = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

