using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace HSMS.Desktop.Models;

public sealed class CycleItemRow : INotifyPropertyChanged
{
    private string _department = string.Empty;
    private string _doctorOrRoom = string.Empty;
    private string _itemName = string.Empty;
    private int _pcs = 1;
    private int _qty = 1;
    public ObservableCollection<string> ItemOptions { get; } = [];

    public string Department
    {
        get => _department;
        set => SetField(ref _department, value);
    }

    public string DoctorOrRoom
    {
        get => _doctorOrRoom;
        set => SetField(ref _doctorOrRoom, value);
    }

    public string ItemName
    {
        get => _itemName;
        set => SetField(ref _itemName, value);
    }

    public int Pcs
    {
        get => _pcs;
        set => SetField(ref _pcs, value);
    }

    public int Qty
    {
        get => _qty;
        set => SetField(ref _qty, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
