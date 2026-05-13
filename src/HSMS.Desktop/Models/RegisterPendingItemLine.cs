using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HSMS.Desktop.Models;

/// <summary>One instrument line on Register Load before save (shown in the pending items grid).</summary>
public sealed class RegisterPendingItemLine : INotifyPropertyChanged
{
    private string _itemName = "";
    private int _pcs = 1;
    private int _qty = 1;
    private string? _departmentName;
    private string? _doctorOrRoom;

    public string ItemName
    {
        get => _itemName;
        set
        {
            var v = value ?? "";
            if (_itemName == v)
            {
                return;
            }

            _itemName = v;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ItemName)));
        }
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

    public string? DepartmentName
    {
        get => _departmentName;
        set => SetField(ref _departmentName, value);
    }

    public string? DoctorOrRoom
    {
        get => _doctorOrRoom;
        set => SetField(ref _doctorOrRoom, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField(ref string? field, string? value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private void SetField(ref int field, int value, [CallerMemberName] string? name = null)
    {
        if (field == value)
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
