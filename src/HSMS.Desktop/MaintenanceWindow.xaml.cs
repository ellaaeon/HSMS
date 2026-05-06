using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using HSMS.Application.Services;
using HSMS.Shared.Contracts;
using HSMS.Shared.Time;
using Microsoft.Win32;

namespace HSMS.Desktop;

public partial class MaintenanceWindow : Window
{
    private readonly IHsmsDataService _data;
    private readonly ObservableCollection<SterilizerListItemDto> _sterilizers = [];
    private readonly ObservableCollection<DepartmentListItemDto> _departments = [];
    private readonly ObservableCollection<DepartmentItemListItemDto> _departmentItems = [];
    private readonly ObservableCollection<DoctorRoomListItemDto> _doctorRooms = [];
    private readonly ObservableCollection<AccountListItemDto> _accounts = [];
    private readonly ObservableCollection<AuditLogRowDto> _auditRows = [];
    private readonly ObservableCollection<AuditSecurityAlertDto> _auditAlerts = [];
    private readonly ObservableCollection<AccountRecentActivityDto> _auditActive = [];
    private readonly ObservableCollection<AuditVolumeRowDto> _auditVolume = [];

    public ObservableCollection<DepartmentListItemDto> DepartmentLookup { get; } = [];

    public MaintenanceWindow(IHsmsDataService dataService)
    {
        _data = dataService;
        InitializeComponent();
        DataContext = this;

        SterilizersGrid.ItemsSource = _sterilizers;
        DepartmentsGrid.ItemsSource = _departments;
        DepartmentItemsGrid.ItemsSource = _departmentItems;
        DoctorsRoomsGrid.ItemsSource = _doctorRooms;
        AccountsGrid.ItemsSource = _accounts;
        AuditLogGrid.ItemsSource = _auditRows;
        AuditAlertsGrid.ItemsSource = _auditAlerts;
        AuditActiveGrid.ItemsSource = _auditActive;
        AuditVolumeGrid.ItemsSource = _auditVolume;

        SterilizersGrid.PreviewKeyDown += SterilizersGrid_OnPreviewKeyDown;
        SterilizersGrid.CurrentCellChanged += SterilizersGrid_OnCurrentCellChanged;
        SterilizersGrid.PreparingCellForEdit += SterilizersGrid_OnPreparingCellForEdit;
        DepartmentsGrid.PreviewKeyDown += DepartmentsGrid_OnPreviewKeyDown;
        DepartmentItemsGrid.PreviewKeyDown += DepartmentItemsGrid_OnPreviewKeyDown;
        DoctorsRoomsGrid.PreviewKeyDown += DoctorsRoomsGrid_OnPreviewKeyDown;

        EnsureDepartmentItemsDepartmentColumnItemsSource();
        EnsureDoctorsRoomsDepartmentColumnItemsSource();

        Loaded += MaintenanceWindow_OnLoaded;
    }

    private async void MaintenanceWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await RefreshAllAsync();
    }

    private async Task RefreshAllAsync()
    {
        try
        {
            var (sterilizers, sterErr) = await _data.GetSterilizersAsync();
            if (sterErr is not null && !sterErr.StartsWith("404", StringComparison.Ordinal))
                throw new InvalidOperationException(sterErr);
            var (departments, depErr) = await _data.GetDepartmentsAsync();
            if (depErr is not null && !depErr.StartsWith("404", StringComparison.Ordinal))
                throw new InvalidOperationException(depErr);
            var (departmentItems, itemErr) = await _data.GetDepartmentItemsAsync();
            if (itemErr is not null && !itemErr.StartsWith("404", StringComparison.Ordinal))
                throw new InvalidOperationException(itemErr);
            var (doctorRooms, docErr) = await _data.GetDoctorRoomsAsync();
            if (docErr is not null && !docErr.StartsWith("404", StringComparison.Ordinal))
                throw new InvalidOperationException(docErr);
            var (accounts, accErr) = await _data.GetAccountsAsync();
            if (accErr is not null && !accErr.StartsWith("404", StringComparison.Ordinal))
                throw new InvalidOperationException(accErr);

            // Maintenance should show ALL master rows (active + inactive) so admins can manage them.
            ResetCollection(_sterilizers, sterilizers.OrderBy(x => x.SterilizerId));
            ResetCollection(_departments, departments.OrderBy(x => x.DepartmentName));
            ResetCollection(_departmentItems, departmentItems.OrderBy(x => x.DepartmentName).ThenBy(x => x.ItemName));
            ResetCollection(_doctorRooms, doctorRooms.OrderBy(x => x.DoctorRoomId));
            ResetCollection(_accounts, accounts.OrderBy(x => x.AccountId));

            // Dropdown lookup for Department Items tab.
            ResetDepartmentLookup();
            EnsureDepartmentItemsDepartmentColumnItemsSource();
            EnsureDoctorsRoomsDepartmentColumnItemsSource();

            if (sterErr is not null || depErr is not null || itemErr is not null || docErr is not null || accErr is not null)
            {
                MessageBox.Show(this,
                    "Some maintenance endpoints returned 404. Restart HSMS.Api so the latest endpoints are active.",
                    "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void EnsureDepartmentItemsDepartmentColumnItemsSource()
    {
        try
        {
            // DataGridComboBoxColumn bindings can fail to refresh; set ItemsSource directly.
            var col = DepartmentItemsGrid.Columns
                .OfType<DataGridComboBoxColumn>()
                .FirstOrDefault(c => string.Equals(c.Header?.ToString(), "Department", StringComparison.OrdinalIgnoreCase));
            if (col is null) return;

            col.ItemsSource = DepartmentLookup;
            col.SelectedValuePath = "DepartmentId";
            col.DisplayMemberPath = "DepartmentName";
        }
        catch
        {
            // ignore
        }
    }

    private void EnsureDoctorsRoomsDepartmentColumnItemsSource()
    {
        try
        {
            // Same WPF quirk: DataGridComboBoxColumn isn't in the visual tree; set ItemsSource directly.
            var col = DoctorsRoomsGrid.Columns
                .OfType<DataGridComboBoxColumn>()
                .FirstOrDefault(c => string.Equals(c.Header?.ToString(), "Department", StringComparison.OrdinalIgnoreCase));
            if (col is null) return;

            col.ItemsSource = DepartmentLookup;
            col.SelectedValuePath = "DepartmentName";
            col.DisplayMemberPath = "DepartmentName";
        }
        catch
        {
            // ignore
        }
    }

    private void ResetDepartmentLookup()
    {
        DepartmentLookup.Clear();
        DepartmentLookup.Add(new DepartmentListItemDto
        {
            DepartmentId = 0,
            DepartmentName = "Select a department...",
            IsActive = true
        });

        foreach (var d in _departments.OrderBy(x => x.DepartmentName))
        {
            DepartmentLookup.Add(d);
        }
    }

    private static void ResetCollection<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }

    private async void RefreshAll_OnClick(object sender, RoutedEventArgs e) => await RefreshAllAsync();

    private void AddSterilizer_OnClick(object sender, RoutedEventArgs e)
    {
        var newRow = new SterilizerListItemDto { IsActive = true };
        _sterilizers.Add(newRow);
        FocusCellAfterAdd(SterilizersGrid, newRow, "Sterilizer No");
    }

    private async void SaveSterilizers_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveSterilizersAsync();
    }

    private async void DeleteSterilizer_OnClick(object sender, RoutedEventArgs e)
    {
        await RunSafeAsync(async () =>
        {
            if (SterilizersGrid.SelectedItem is not SterilizerListItemDto selected) return;
            if (selected.SterilizerId <= 0)
            {
                _sterilizers.Remove(selected);
                return;
            }
            var err = await _data.DeleteSterilizerAsync(selected.SterilizerId);
            if (err is not null) throw new InvalidOperationException(err);
            await RefreshAllAsync();
        });
    }

    private void AddDepartment_OnClick(object sender, RoutedEventArgs e)
    {
        var newRow = new DepartmentListItemDto { IsActive = true };
        _departments.Add(newRow);
        FocusCellAfterAdd(DepartmentsGrid, newRow, "Department");
    }

    private async void SaveDepartments_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveDepartmentsAsync();
    }

    private async void DeleteDepartment_OnClick(object sender, RoutedEventArgs e)
    {
        await RunSafeAsync(async () =>
        {
            if (DepartmentsGrid.SelectedItem is not DepartmentListItemDto selected) return;
            if (selected.DepartmentId <= 0)
            {
                _departments.Remove(selected);
                return;
            }
            var err = await _data.DeleteDepartmentAsync(selected.DepartmentId);
            if (err is not null) throw new InvalidOperationException(err);
            await RefreshAllAsync();
        });
    }

    private void AddDepartmentItem_OnClick(object sender, RoutedEventArgs e)
    {
        var defaultDepartmentId = 0; // placeholder: "Select a department..."
        var newRow = new DepartmentItemListItemDto
        {
            DepartmentId = defaultDepartmentId,
            DefaultPcs = 1,
            DefaultQty = 1
        };
        _departmentItems.Add(newRow);
        FocusCellAfterAdd(DepartmentItemsGrid, newRow, "Item description");
    }

    private async void SaveDepartmentItems_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveDepartmentItemsAsync();
    }

    private async void DeleteDepartmentItem_OnClick(object sender, RoutedEventArgs e)
    {
        await RunSafeAsync(async () =>
        {
            if (DepartmentItemsGrid.SelectedItem is not DepartmentItemListItemDto selected) return;
            if (selected.DeptItemId <= 0)
            {
                _departmentItems.Remove(selected);
                return;
            }
            var err = await _data.DeleteDepartmentItemAsync(selected.DeptItemId);
            if (err is not null) throw new InvalidOperationException(err);
            await RefreshAllAsync();
        });
    }

    private void AddDoctorRoom_OnClick(object sender, RoutedEventArgs e)
    {
        var newRow = new DoctorRoomListItemDto { IsActive = true };
        _doctorRooms.Add(newRow);
        FocusCellAfterAdd(DoctorsRoomsGrid, newRow, "Doctor");
    }

    private static void FocusCellAfterAdd(System.Windows.Controls.DataGrid grid, object row, string headerText)
    {
        grid.Dispatcher.BeginInvoke(new System.Action(() =>
        {
            grid.Focus();
            grid.SelectedItem = row;

            var column = grid.Columns.FirstOrDefault(c =>
                string.Equals(c.Header?.ToString(), headerText, StringComparison.OrdinalIgnoreCase));

            if (column is null) return;

            grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(row, column);
            grid.ScrollIntoView(row, column);

            try
            {
                grid.BeginEdit();
            }
            catch
            {
                // Some columns may not support editing immediately; ignore.
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private async void SaveDoctorsRooms_OnClick(object sender, RoutedEventArgs e)
    {
        await SaveDoctorsRoomsAsync();
    }

    private static void CommitGridEdits(System.Windows.Controls.DataGrid grid)
    {
        try
        {
            grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Cell, true);
            grid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        }
        catch
        {
            // ignore commit errors, still try to save.
        }
    }

    private static bool ShouldSkipArrowHandling(System.Windows.Controls.DataGrid grid)
    {
        // If the DatePicker's calendar popup is open, we should not hijack arrows.
        var fe = System.Windows.Input.Keyboard.FocusedElement;
        if (fe is System.Windows.Controls.DatePicker dp && dp.IsDropDownOpen) return true;
        if (fe is System.Windows.Controls.Calendar) return true;
        return false;
    }

    private static bool IsPurchaseDateColumn(DataGridColumn column)
        => column.Header?.ToString()?.Equals("Purchase Date", StringComparison.OrdinalIgnoreCase) == true;

    private static void TryBeginEditAndOpenPurchaseDatePicker(DataGrid grid)
    {
        if (!grid.CurrentCell.IsValid) return;
        if (!IsPurchaseDateColumn(grid.CurrentCell.Column)) return;
        if (grid.IsReadOnly) return;

        var item = grid.CurrentCell.Item;
        var column = grid.CurrentCell.Column;
        if (item is null || column is null) return;

        // Ensure cell is realized so popup placement is correct.
        grid.ScrollIntoView(item, column);

        grid.Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!grid.CurrentCell.IsValid) return;
                grid.BeginEdit();
            }
            catch
            {
                // ignore
            }
        }), DispatcherPriority.Background);
    }

    private void SterilizersGrid_OnCurrentCellChanged(object? sender, EventArgs e)
    {
        if (sender is not System.Windows.Controls.DataGrid grid) return;
        if (grid.CurrentCell.IsValid && IsPurchaseDateColumn(grid.CurrentCell.Column))
        {
            // When keyboard focus lands on Purchase Date, enter edit mode immediately.
            TryBeginEditAndOpenPurchaseDatePicker(grid);
        }
    }

    private void SterilizersGrid_OnPreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is null) return;

        if (IsPurchaseDateColumn(e.Column))
        {
            // Find DatePicker in the editing element and open popup AFTER focus is inside the cell.
            var dp = e.EditingElement as DatePicker;
            if (dp is null && e.EditingElement is FrameworkElement fe)
                dp = FindVisualChild<DatePicker>(fe);
            if (dp is null) return;

            dp.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    dp.ApplyTemplate();

                    // Force popup placement to this DatePicker (prevents "detached far right" popups)
                    var popup = dp.Template?.FindName("PART_Popup", dp) as Popup;
                    if (popup is not null)
                    {
                        popup.PlacementTarget = dp;
                        popup.Placement = PlacementMode.Bottom;
                        popup.HorizontalOffset = 0;
                        popup.VerticalOffset = 0;
                    }

                    dp.Focus();
                    dp.IsDropDownOpen = true;
                }
                catch
                {
                    // ignore
                }
            }), DispatcherPriority.Background);

            return;
        }
    }

    private static DatePicker? TryGetDatePickerInCurrentCell(DataGrid grid)
    {
        try
        {
            if (!grid.CurrentCell.IsValid) return null;

            var rowItem = grid.CurrentCell.Item;
            var col = grid.CurrentCell.Column;

            var rowContainer = grid.ItemContainerGenerator.ContainerFromItem(rowItem) as DataGridRow;
            if (rowContainer is null) return null;

            int columnIndex = grid.Columns.IndexOf(col);
            if (columnIndex < 0) return null;

            var cell = GetDataGridCell(rowContainer, columnIndex);
            if (cell is null) return null;

            return FindVisualChild<DatePicker>(cell);
        }
        catch
        {
            return null;
        }
    }

    private static DataGridCell? GetDataGridCell(DataGridRow rowContainer, int columnIndex)
    {
        try
        {
            rowContainer.ApplyTemplate();
            var presenter = FindVisualChild<DataGridCellsPresenter>(rowContainer);
            if (presenter is null) return null;

            var cell = presenter.ItemContainerGenerator.ContainerFromIndex(columnIndex) as DataGridCell;
            return cell;
        }
        catch
        {
            return null;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        try
        {
            if (root is null) return null;
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result is not null) return result;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void MoveFocusOnArrow(System.Windows.Controls.DataGrid grid, System.Windows.Input.Key key)
    {
        if (key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right)
        {
            MoveFocusHorizontally(grid, key);
            return;
        }

        if (key is System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)
        {
            MoveFocusVertically(grid, key);
            return;
        }

        var dir = key switch
        {
            _ => System.Windows.Input.FocusNavigationDirection.Next
        };

        grid.MoveFocus(new System.Windows.Input.TraversalRequest(dir));
    }

    private static void MoveFocusVertically(System.Windows.Controls.DataGrid grid, System.Windows.Input.Key key)
    {
        var currentCell = grid.CurrentCell;
        if (!currentCell.IsValid)
        {
            foreach (var c in grid.SelectedCells)
            {
                currentCell = c;
                break;
            }
        }

        if (!currentCell.IsValid) return;

        var currentItem = currentCell.Item;
        int currentRowIndex = grid.Items.IndexOf(currentItem);
        if (currentRowIndex < 0) return;

        int currentColumnIndex = grid.Columns.IndexOf(currentCell.Column);
        if (currentColumnIndex < 0) return;

        int delta = key == System.Windows.Input.Key.Down ? 1 : -1;
        int targetRowIndex = currentRowIndex + delta;
        if (targetRowIndex < 0 || targetRowIndex >= grid.Items.Count) return;

        var targetItem = grid.Items[targetRowIndex];
        var targetColumn = grid.Columns[currentColumnIndex];

        grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(targetItem, targetColumn);
        grid.ScrollIntoView(targetItem, targetColumn);

        try
        {
            if (!targetColumn.IsReadOnly && !grid.IsReadOnly)
                grid.BeginEdit();
        }
        catch
        {
            // ignore
        }

        if (IsPurchaseDateColumn(targetColumn))
        {
            TryBeginEditAndOpenPurchaseDatePicker(grid);
        }
    }

    private static void MoveFocusHorizontally(System.Windows.Controls.DataGrid grid, System.Windows.Input.Key key)
    {
        var currentCell = grid.CurrentCell;
        if (!currentCell.IsValid)
        {
            // Fallback: use selected cell when CurrentCell isn't set yet.
            foreach (var c in grid.SelectedCells)
            {
                currentCell = c;
                break;
            }
        }

        if (!currentCell.IsValid) return;

        var currentItem = currentCell.Item;
        int currentRowIndex = grid.Items.IndexOf(currentItem);
        if (currentRowIndex < 0)
        {
            var container = grid.ItemContainerGenerator.ContainerFromItem(currentItem);
            if (container is not null)
                currentRowIndex = grid.ItemContainerGenerator.IndexFromContainer(container);
        }

        int currentColumnIndex = grid.Columns.IndexOf(currentCell.Column);
        if (currentColumnIndex < 0) return;

        int delta = key == System.Windows.Input.Key.Right ? 1 : -1;
        int targetColumnIndex = currentColumnIndex + delta;
        int targetRowIndex = currentRowIndex;

        // Wrap across rows to keep navigation continuous.
        if (targetColumnIndex < 0)
        {
            targetColumnIndex = grid.Columns.Count - 1;
            targetRowIndex = currentRowIndex - 1;
        }
        else if (targetColumnIndex >= grid.Columns.Count)
        {
            targetColumnIndex = 0;
            targetRowIndex = currentRowIndex + 1;
        }

        if (targetRowIndex < 0 || targetRowIndex >= grid.Items.Count) return;
        if (targetColumnIndex < 0 || targetColumnIndex >= grid.Columns.Count) return;

        var targetItem = grid.Items[targetRowIndex];
        var targetColumn = grid.Columns[targetColumnIndex];

        grid.CurrentCell = new System.Windows.Controls.DataGridCellInfo(targetItem, targetColumn);
        grid.ScrollIntoView(targetItem, targetColumn);

        // If the grid is in edit mode for the previous cell, try to immediately edit the new cell.
        try
        {
            if (!targetColumn.IsReadOnly && !grid.IsReadOnly)
                grid.BeginEdit();
        }
        catch
        {
            // Not all columns support editing; ignore.
        }

        if (IsPurchaseDateColumn(targetColumn))
        {
            TryBeginEditAndOpenPurchaseDatePicker(grid);
        }
    }

    private async void SterilizersGrid_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitGridEdits(SterilizersGrid);
            await SaveSterilizersAsync();
            return;
        }

        if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)
        {
            // If DatePicker/Calendar popup is open, let it consume arrows.
            if (ShouldSkipArrowHandling(SterilizersGrid)) return;
            CommitGridEdits(SterilizersGrid);
            MoveFocusOnArrow(SterilizersGrid, e.Key);
            TryBeginEditAndOpenPurchaseDatePicker(SterilizersGrid);
            e.Handled = true;
        }
    }

    private async void DepartmentsGrid_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitGridEdits(DepartmentsGrid);
            await SaveDepartmentsAsync();
            return;
        }

        if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)
        {
            if (ShouldSkipArrowHandling(DepartmentsGrid)) return;
            CommitGridEdits(DepartmentsGrid);
            MoveFocusOnArrow(DepartmentsGrid, e.Key);
            e.Handled = true;
        }
    }

    private async void DepartmentItemsGrid_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitGridEdits(DepartmentItemsGrid);
            await SaveDepartmentItemsAsync();
            return;
        }

        if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)
        {
            if (ShouldSkipArrowHandling(DepartmentItemsGrid)) return;
            CommitGridEdits(DepartmentItemsGrid);
            MoveFocusOnArrow(DepartmentItemsGrid, e.Key);
            e.Handled = true;
        }
    }

    private async void DoctorsRoomsGrid_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            CommitGridEdits(DoctorsRoomsGrid);
            await SaveDoctorsRoomsAsync();
            return;
        }

        if (e.Key is System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Up or System.Windows.Input.Key.Down)
        {
            if (ShouldSkipArrowHandling(DoctorsRoomsGrid)) return;
            CommitGridEdits(DoctorsRoomsGrid);
            MoveFocusOnArrow(DoctorsRoomsGrid, e.Key);
            e.Handled = true;
        }
    }

    private async Task SaveSterilizersAsync()
    {
        await RunSafeAsync(async () =>
        {
            foreach (var row in _sterilizers)
            {
                if (string.IsNullOrWhiteSpace(row.SterilizerNo)) continue;
                var payload = new SterilizerUpsertDto
                {
                    SterilizerNo = row.SterilizerNo.Trim(),
                    Model = row.Model,
                    Manufacturer = row.Manufacturer,
                    PurchaseDate = row.PurchaseDate
                };
                if (row.SterilizerId <= 0)
                {
                    var (created, err) = await _data.CreateSterilizerAsync(payload);
                    if (err is not null) throw new InvalidOperationException(err);
                    if (created is not null) row.SterilizerId = created.SterilizerId;
                }
                else
                {
                    var err = await _data.UpdateSterilizerAsync(row.SterilizerId, payload);
                    if (err is not null) throw new InvalidOperationException(err);
                }
            }
            await RefreshAllAsync();
        });
    }

    private async Task SaveDepartmentsAsync()
    {
        await RunSafeAsync(async () =>
        {
            foreach (var row in _departments)
            {
                if (string.IsNullOrWhiteSpace(row.DepartmentName)) continue;
                var payload = new DepartmentUpsertDto { DepartmentName = row.DepartmentName.Trim() };
                if (row.DepartmentId <= 0)
                {
                    var (created, err) = await _data.CreateDepartmentAsync(payload);
                    if (err is not null) throw new InvalidOperationException(err);
                    if (created is not null) row.DepartmentId = created.DepartmentId;
                }
                else
                {
                    var err = await _data.UpdateDepartmentAsync(row.DepartmentId, payload);
                    if (err is not null) throw new InvalidOperationException(err);
                }
            }
            await RefreshAllAsync();
        });
    }

    private async Task SaveDepartmentItemsAsync()
    {
        await RunSafeAsync(async () =>
        {
            foreach (var row in _departmentItems)
            {
                if (row.DepartmentId <= 0 || string.IsNullOrWhiteSpace(row.ItemName)) continue;
                var payload = new DepartmentItemUpsertDto
                {
                    DepartmentId = row.DepartmentId,
                    ItemName = row.ItemName.Trim(),
                    DefaultPcs = row.DefaultPcs,
                    DefaultQty = row.DefaultQty
                };

                if (row.DeptItemId <= 0)
                {
                    var (created, err) = await _data.CreateDepartmentItemAsync(payload);
                    if (err is not null) throw new InvalidOperationException(err);
                    if (created is not null) row.DeptItemId = created.DeptItemId;
                }
                else
                {
                    var err = await _data.UpdateDepartmentItemAsync(row.DeptItemId, payload);
                    if (err is not null) throw new InvalidOperationException(err);
                }
            }
            await RefreshAllAsync();
        });
    }

    private async Task SaveDoctorsRoomsAsync()
    {
        await RunSafeAsync(async () =>
        {
            foreach (var row in _doctorRooms)
            {
                if (string.IsNullOrWhiteSpace(row.DoctorName)) continue;
                var payload = new DoctorRoomUpsertDto { DoctorName = row.DoctorName.Trim(), Room = row.Room };
                if (row.DoctorRoomId <= 0)
                {
                    var (created, err) = await _data.CreateDoctorRoomAsync(payload);
                    if (err is not null) throw new InvalidOperationException(err);
                    if (created is not null) row.DoctorRoomId = created.DoctorRoomId;
                }
                else
                {
                    var err = await _data.UpdateDoctorRoomAsync(row.DoctorRoomId, payload);
                    if (err is not null) throw new InvalidOperationException(err);
                }
            }
            await RefreshAllAsync();
        });
    }

    private async void DeleteDoctorRoom_OnClick(object sender, RoutedEventArgs e)
    {
        await RunSafeAsync(async () =>
        {
            if (DoctorsRoomsGrid.SelectedItem is not DoctorRoomListItemDto selected) return;
            if (selected.DoctorRoomId <= 0)
            {
                _doctorRooms.Remove(selected);
                return;
            }
            var err = await _data.DeleteDoctorRoomAsync(selected.DoctorRoomId);
            if (err is not null) throw new InvalidOperationException(err);
            await RefreshAllAsync();
        });
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private async void AuditRefreshButton_OnClick(object sender, RoutedEventArgs e)
    {
        await RunSafeAsync(LoadAuditViewsAsync);
    }

    private async Task LoadAuditViewsAsync()
    {
        int? actorId = null;
        if (!string.IsNullOrWhiteSpace(AuditActorIdBox.Text)
            && int.TryParse(AuditActorIdBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var aid))
        {
            actorId = aid;
        }

        var q = new AuditLogQueryDto
        {
            FromUtc = HsmsDeploymentTimeZone.DeploymentCalendarDayStartUtc(AuditFromDate.SelectedDate),
            ToUtc = HsmsDeploymentTimeZone.DeploymentCalendarDayEndUtc(AuditToDate.SelectedDate),
            UserSearch = string.IsNullOrWhiteSpace(AuditUserSearchBox.Text) ? null : AuditUserSearchBox.Text.Trim(),
            ActorAccountId = actorId,
            ModuleFilter = string.IsNullOrWhiteSpace(AuditModuleFilterBox.Text) ? null : AuditModuleFilterBox.Text.Trim(),
            ActionFilter = string.IsNullOrWhiteSpace(AuditActionFilterBox.Text) ? null : AuditActionFilterBox.Text.Trim(),
            Take = 300
        };

        var (rows, err) = await _data.GetAuditLogsAsync(q);
        if (err is not null)
        {
            throw new InvalidOperationException(err);
        }

        ResetCollection(_auditRows, rows);

        var (alerts, aErr) = await _data.GetAuditSecurityAlertsAsync(80);
        if (aErr is not null)
        {
            throw new InvalidOperationException(aErr);
        }

        ResetCollection(_auditAlerts, alerts);

        var (active, actErr) = await _data.GetRecentlyActiveAccountsAsync(24);
        if (actErr is not null)
        {
            throw new InvalidOperationException(actErr);
        }

        ResetCollection(_auditActive, active);

        var (vol, vErr) = await _data.GetSterilizationUpdateVolumeAsync(1, 25);
        if (vErr is not null)
        {
            throw new InvalidOperationException(vErr);
        }

        ResetCollection(_auditVolume, vol);
    }

    private void AuditExportCsvButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_auditRows.Count == 0)
        {
            MessageBox.Show(this, "Load the audit log first, then export.", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"hsms-audit-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dlg.ShowDialog(this) != true)
        {
            return;
        }

        static string Csv(string? s)
        {
            if (s is null)
            {
                return "\"\"";
            }

            var t = s.Replace("\"", "\"\"", StringComparison.Ordinal);
            return $"\"{t}\"";
        }

        var sb = new StringBuilder();
        sb.AppendLine("event_at_utc,actor_id,actor_username,module,action,entity_name,entity_id,client_machine,old_values,new_values");
        foreach (var r in _auditRows)
        {
            sb.Append(Csv(r.EventAtUtc.ToString("u", CultureInfo.InvariantCulture))).Append(',');
            sb.Append(r.ActorAccountId?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',');
            sb.Append(Csv(r.ActorUsername)).Append(',');
            sb.Append(Csv(r.Module)).Append(',');
            sb.Append(Csv(r.Action)).Append(',');
            sb.Append(Csv(r.EntityName)).Append(',');
            sb.Append(Csv(r.EntityId)).Append(',');
            sb.Append(Csv(r.ClientMachine)).Append(',');
            sb.Append(Csv(r.OldValuesJson)).Append(',');
            sb.AppendLine(Csv(r.NewValuesJson));
        }

        File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        MessageBox.Show(this, $"Saved {dlg.FileName}", "HSMS", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async Task RunSafeAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "HSMS", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}

public sealed class ZeroIdToEmptyStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null) return "";

        return value switch
        {
            int i => i <= 0 ? "" : i.ToString(culture),
            long l => l <= 0 ? "" : l.ToString(culture),
            short s => s <= 0 ? "" : s.ToString(culture),
            byte b => b == 0 ? "" : b.ToString(culture),
            _ => value.ToString() ?? ""
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
