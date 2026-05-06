using HSMS.Application.Audit;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Shared.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<(SterilizerListItemDto? item, string? error)> CreateSterilizerAsync(SterilizerUpsertDto request, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } adminErr)
        {
            return (null, adminErr);
        }

        var sterilizerNo = request.SterilizerNo.Trim();
        if (string.IsNullOrWhiteSpace(sterilizerNo))
        {
            return (null, "Sterilizer number is required.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = new SterilizerUnit
        {
            SterilizerNumber = sterilizerNo,
            Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim(),
            Manufacturer = string.IsNullOrWhiteSpace(request.Manufacturer) ? null : request.Manufacturer.Trim(),
            PurchaseDate = request.PurchaseDate,
            IsActive = true
        };
        try
        {
            db.SterilizerUnits.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            return (null, $"Sterilizer number '{sterilizerNo}' already exists.");
        }
        catch (SqlException ex) when (IsMissingSterilizerColumns(ex))
        {
            return (null, "Database is missing sterilizer columns. Run ddl/003_hsms_sterilizer_manufacturer_purchase_date.sql.");
        }

        return (new SterilizerListItemDto
        {
            SterilizerId = entity.SterilizerId,
            SterilizerNo = entity.SterilizerNumber,
            Model = entity.Model,
            Manufacturer = entity.Manufacturer,
            PurchaseDate = entity.PurchaseDate,
            IsActive = entity.IsActive
        }, null);
    }

    public async Task<string?> UpdateSterilizerAsync(int id, SterilizerUpsertDto request, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } e)
        {
            return e;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SterilizerUnits.SingleOrDefaultAsync(x => x.SterilizerId == id, cancellationToken);
        if (entity is null)
        {
            return "Sterilizer not found.";
        }

        var oldSnap = new { entity.SterilizerNumber, entity.Model, entity.Manufacturer, entity.PurchaseDate };
        entity.SterilizerNumber = request.SterilizerNo.Trim();
        entity.Model = string.IsNullOrWhiteSpace(request.Model) ? null : request.Model.Trim();
        entity.Manufacturer = string.IsNullOrWhiteSpace(request.Manufacturer) ? null : request.Manufacturer.Trim();
        entity.PurchaseDate = request.PurchaseDate;
        entity.IsActive = true;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (SqlException ex) when (IsMissingSterilizerColumns(ex))
        {
            return "Database is missing sterilizer columns. Run ddl/003_hsms_sterilizer_manufacturer_purchase_date.sql.";
        }

        await AppendMasterAuditAsync(
            db,
            AuditActions.MastersSterilizerUpdate,
            "tbl_sterilizer_no",
            id.ToString(),
            oldSnap,
            new { entity.SterilizerNumber, entity.Model, entity.Manufacturer, entity.PurchaseDate },
            cancellationToken);
        return null;
    }

    public async Task<string?> DeleteSterilizerAsync(int id, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } e)
        {
            return e;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SterilizerUnits.SingleOrDefaultAsync(x => x.SterilizerId == id, cancellationToken);
        if (entity is null)
        {
            return "Sterilizer not found.";
        }

        var snap = new { entity.SterilizerNumber, entity.SterilizerId };
        entity.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        await AppendMasterAuditAsync(
            db,
            AuditActions.MastersSterilizerDeactivate,
            "tbl_sterilizer_no",
            id.ToString(),
            snap,
            new { deactivated = true },
            cancellationToken);
        return null;
    }

    public async Task<(DepartmentListItemDto? item, string? error)> CreateDepartmentAsync(DepartmentUpsertDto request, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } adminErr)
        {
            return (null, adminErr);
        }

        var name = request.DepartmentName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "Department name is required.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = new Department
        {
            DepartmentCode = await GenerateDepartmentCodeAsync(db, name, cancellationToken),
            DepartmentName = name,
            IsActive = true
        };
        db.Departments.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (new DepartmentListItemDto { DepartmentId = entity.DepartmentId, DepartmentName = entity.DepartmentName, IsActive = entity.IsActive }, null);
    }

    public async Task<string?> UpdateDepartmentAsync(int id, DepartmentUpsertDto request, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } e)
        {
            return e;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Departments.SingleOrDefaultAsync(x => x.DepartmentId == id, cancellationToken);
        if (entity is null)
        {
            return "Department not found.";
        }

        var oldSnap = new { entity.DepartmentName, entity.DepartmentCode };
        entity.DepartmentName = request.DepartmentName.Trim();
        entity.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);
        await AppendMasterAuditAsync(
            db,
            AuditActions.MastersDepartmentUpdate,
            "tbl_departments",
            id.ToString(),
            oldSnap,
            new { entity.DepartmentName, entity.DepartmentCode },
            cancellationToken);
        return null;
    }

    public async Task<string?> DeleteDepartmentAsync(int id, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } e)
        {
            return e;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Departments.SingleOrDefaultAsync(x => x.DepartmentId == id, cancellationToken);
        if (entity is null)
        {
            return "Department not found.";
        }

        var snap = new { entity.DepartmentName, entity.DepartmentId };
        entity.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        await AppendMasterAuditAsync(
            db,
            AuditActions.MastersDepartmentDeactivate,
            "tbl_departments",
            id.ToString(),
            snap,
            new { deactivated = true },
            cancellationToken);
        return null;
    }

    public async Task<(DoctorRoomListItemDto? item, string? error)> CreateDoctorRoomAsync(DoctorRoomUpsertDto request, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } adminErr)
        {
            return (null, adminErr);
        }

        var doctorName = request.DoctorName.Trim();
        if (string.IsNullOrWhiteSpace(doctorName))
        {
            return (null, "Doctor name is required.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = new DoctorRoom
        {
            DoctorName = doctorName,
            Room = string.IsNullOrWhiteSpace(request.Room) ? null : request.Room.Trim(),
            IsActive = true
        };
        db.DoctorRooms.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (new DoctorRoomListItemDto
        {
            DoctorRoomId = entity.DoctorRoomId,
            DoctorName = entity.DoctorName,
            Room = entity.Room,
            DisplayName = string.IsNullOrWhiteSpace(entity.Room) ? entity.DoctorName : $"{entity.DoctorName} / {entity.Room}",
            IsActive = entity.IsActive
        }, null);
    }

    public async Task<string?> UpdateDoctorRoomAsync(int id, DoctorRoomUpsertDto request, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } e)
        {
            return e;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DoctorRooms.SingleOrDefaultAsync(x => x.DoctorRoomId == id, cancellationToken);
        if (entity is null)
        {
            return "Doctor/room not found.";
        }

        var oldSnap = new { entity.DoctorName, entity.Room };
        entity.DoctorName = request.DoctorName.Trim();
        entity.Room = string.IsNullOrWhiteSpace(request.Room) ? null : request.Room.Trim();
        entity.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);
        await AppendMasterAuditAsync(
            db,
            AuditActions.MastersDoctorRoomUpdate,
            "tbl_doctors_rooms",
            id.ToString(),
            oldSnap,
            new { entity.DoctorName, entity.Room },
            cancellationToken);
        return null;
    }

    public async Task<string?> DeleteDoctorRoomAsync(int id, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } e)
        {
            return e;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DoctorRooms.SingleOrDefaultAsync(x => x.DoctorRoomId == id, cancellationToken);
        if (entity is null)
        {
            return "Doctor/room not found.";
        }

        var snap = new { entity.DoctorName, entity.Room, entity.DoctorRoomId };
        entity.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        await AppendMasterAuditAsync(
            db,
            AuditActions.MastersDoctorRoomDeactivate,
            "tbl_doctors_rooms",
            id.ToString(),
            snap,
            new { deactivated = true },
            cancellationToken);
        return null;
    }

    public async Task<(DepartmentItemListItemDto? item, string? error)> CreateDepartmentItemAsync(DepartmentItemUpsertDto request, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } adminErr)
        {
            return (null, adminErr);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var dept = await db.Departments.SingleOrDefaultAsync(x => x.DepartmentId == request.DepartmentId && x.IsActive, cancellationToken);
        if (dept is null)
        {
            return (null, "Department is required.");
        }

        var itemName = request.ItemName.Trim();
        if (string.IsNullOrWhiteSpace(itemName))
        {
            return (null, "Item name is required.");
        }

        var entity = new DepartmentItem
        {
            DepartmentId = request.DepartmentId,
            ItemCode = await GenerateDepartmentItemCodeAsync(db, request.DepartmentId, itemName, cancellationToken),
            ItemName = itemName,
            DefaultPcs = request.DefaultPcs,
            DefaultQty = request.DefaultQty,
            IsActive = true
        };
        try
        {
            db.DepartmentItems.Add(entity);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2627 || sqlEx.Number == 2601))
        {
            return (null, "This item already exists for the selected department.");
        }

        return (new DepartmentItemListItemDto
        {
            DeptItemId = entity.DeptItemId,
            DepartmentId = entity.DepartmentId,
            DepartmentName = dept.DepartmentName,
            ItemName = entity.ItemName,
            DefaultPcs = entity.DefaultPcs,
            DefaultQty = entity.DefaultQty
        }, null);
    }

    public async Task<string?> UpdateDepartmentItemAsync(int id, DepartmentItemUpsertDto request, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } e)
        {
            return e;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DepartmentItems.SingleOrDefaultAsync(x => x.DeptItemId == id, cancellationToken);
        if (entity is null)
        {
            return "Department item not found.";
        }

        var dept = await db.Departments.SingleOrDefaultAsync(x => x.DepartmentId == request.DepartmentId && x.IsActive, cancellationToken);
        if (dept is null)
        {
            return "Department is required.";
        }

        var oldSnap = new { entity.DepartmentId, entity.ItemName, entity.DefaultPcs, entity.DefaultQty };
        entity.DepartmentId = request.DepartmentId;
        entity.ItemName = request.ItemName.Trim();
        entity.DefaultPcs = request.DefaultPcs;
        entity.DefaultQty = request.DefaultQty;
        entity.IsActive = true;
        await db.SaveChangesAsync(cancellationToken);
        await AppendMasterAuditAsync(
            db,
            AuditActions.MastersDeptItemUpdate,
            "tbl_dept_items",
            id.ToString(),
            oldSnap,
            new { entity.DepartmentId, entity.ItemName, entity.DefaultPcs, entity.DefaultQty },
            cancellationToken);
        return null;
    }

    public async Task<string?> DeleteDepartmentItemAsync(int id, CancellationToken cancellationToken = default)
    {
        if (ForbidUnlessAdmin() is { } e)
        {
            return e;
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.DepartmentItems.SingleOrDefaultAsync(x => x.DeptItemId == id, cancellationToken);
        if (entity is null)
        {
            return "Department item not found.";
        }

        var snap = new { entity.ItemName, entity.DepartmentId, entity.DeptItemId };
        entity.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);
        await AppendMasterAuditAsync(
            db,
            AuditActions.MastersDeptItemDeactivate,
            "tbl_dept_items",
            id.ToString(),
            snap,
            new { deactivated = true },
            cancellationToken);
        return null;
    }

    private async Task AppendMasterAuditAsync(
        HsmsDbContext db,
        string action,
        string entityName,
        string entityId,
        object? oldValues,
        object? newValues,
        CancellationToken cancellationToken)
    {
        await auditService.AppendAsync(
            db,
            AuditModules.Masters,
            entityName,
            entityId,
            action,
            Actor(),
            Environment.MachineName,
            oldValues,
            newValues,
            Guid.NewGuid(),
            cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<string> GenerateDepartmentCodeAsync(HsmsDbContext db, string name, CancellationToken cancellationToken)
    {
        var compact = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(compact))
        {
            compact = "DEPT";
        }

        var baseCode = compact.Length > 8 ? compact[..8] : compact;
        var code = baseCode;
        var i = 1;
        while (await db.Departments.AnyAsync(x => x.DepartmentCode == code, cancellationToken))
        {
            i++;
            var prefix = baseCode[..Math.Min(6, baseCode.Length)];
            code = $"{prefix}{i:00}";
        }

        return code;
    }

    private static async Task<string> GenerateDepartmentItemCodeAsync(HsmsDbContext db, int departmentId, string itemName, CancellationToken cancellationToken)
    {
        var compact = new string(itemName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(compact))
        {
            compact = "ITEM";
        }

        var baseCode = compact.Length > 24 ? compact[..24] : compact;
        var code = baseCode;
        var i = 1;
        while (await db.DepartmentItems.AnyAsync(x => x.DepartmentId == departmentId && x.ItemCode == code, cancellationToken))
        {
            i++;
            var prefix = baseCode[..Math.Min(22, baseCode.Length)];
            code = $"{prefix}{i:00}";
        }

        return code.Length > 32 ? code[..32] : code;
    }
}
