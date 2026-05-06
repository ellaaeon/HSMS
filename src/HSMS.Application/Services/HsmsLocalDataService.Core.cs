using HSMS.Application.Audit;
using HSMS.Application.Security;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService(
    IDbContextFactory<HsmsDbContext> dbFactory,
    IAuditService auditService,
    ICurrentUserAccessor currentUser) : IHsmsDataService
{
    private int? Actor() => currentUser.GetCurrentUser()?.AccountId;

    public async Task<(string? cycleNo, string? error)> GetNextCycleNoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            // CycleNo is now a simple sequence: 00001, 00002, ...
            // Use the primary key sequence (SterilizationId) as the source of truth (old CycleNo values may be non-numeric).
            var maxId = await db.Sterilizations.AsNoTracking()
                .Select(x => (int?)x.SterilizationId)
                .MaxAsync(cancellationToken) ?? 0;

            var next = maxId + 1;
            if (next < 1) next = 1;
            return (next.ToString("D5"), null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    public async Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> SearchCyclesAsync(
        string cycleNo,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var take = string.IsNullOrWhiteSpace(cycleNo) ? 500 : 200;
        var q = db.Sterilizations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(cycleNo))
        {
            q = q.Where(x => x.CycleNo.StartsWith(cycleNo));
        }

        var data = await q.OrderByDescending(x => x.CycleDateTime)
            .Take(take)
            .Select(x => new SterilizationSearchItemDto
            {
                SterilizationId = x.SterilizationId,
                CycleNo = x.CycleNo,
                CycleDateTimeUtc = x.CycleDateTime,
                SterilizerNo = db.SterilizerUnits
                    .Where(u => u.SterilizerId == x.SterilizerId)
                    .Select(u => u.SterilizerNumber)
                    .FirstOrDefault() ?? x.SterilizerId.ToString(),
                CycleStatus = x.CycleStatus
            })
            .ToListAsync(cancellationToken);

        return (data, null);
    }

    public async Task<(SterilizationDetailsDto? detail, string? error)> GetCycleAsync(
        int sterilizationId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Sterilizations.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Receipts)
            .SingleOrDefaultAsync(x => x.SterilizationId == sterilizationId, cancellationToken);

        if (row is null)
        {
            return (null, "Sterilization cycle not found.");
        }

        var dto = new SterilizationDetailsDto
        {
            SterilizationId = row.SterilizationId,
            CycleNo = row.CycleNo,
            SterilizerId = row.SterilizerId,
            SterilizationType = row.SterilizationType,
            CycleProgram = row.CycleProgram,
            CycleDateTimeUtc = row.CycleDateTime,
            OperatorName = row.OperatorName,
            TemperatureC = row.TemperatureC,
            Pressure = row.Pressure,
            ExposureTimeMinutes = row.ExposureTimeMinutes,
            BiLotNo = row.BiLotNo,
            BiResult = row.BiResult,
            BiResultUpdatedAtUtc = row.BiResultUpdatedAt,
            CycleStatus = row.CycleStatus,
            DoctorRoomId = row.DoctorRoomId,
            Implants = row.Implants,
            Notes = row.Notes,
            RowVersion = Convert.ToBase64String(row.RowVersion),
            Items = row.Items.Select(i => new SterilizationItemDto
            {
                SterilizationItemId = i.SterilizationItemId,
                DeptItemId = i.DeptItemId,
                DepartmentName = i.DepartmentName,
                DoctorOrRoom = i.DoctorOrRoom,
                ItemName = i.ItemName,
                Pcs = i.Pcs,
                Qty = i.Qty,
                RowVersion = Convert.ToBase64String(i.RowVersion)
            }).ToList(),
            Receipts = row.Receipts.Select(r => new ReceiptMetadataDto
            {
                ReceiptId = r.ReceiptId,
                FileName = r.FileName,
                ContentType = r.ContentType,
                FileSizeBytes = r.FileSizeBytes,
                CapturedAtUtc = r.CapturedAt
            }).ToList()
        };

        return (dto, null);
    }

    public async Task<(bool ok, string? error)> CreateCycleAsync(SterilizationUpsertDto request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        if (await db.Sterilizations.AnyAsync(x => x.CycleNo == request.CycleNo, cancellationToken))
        {
            return (false, "Cycle number already exists. Please open the existing record.");
        }

        if (SterilizationUpsertValidator.Validate(request) is { } createValidationError)
        {
            return (false, createValidationError);
        }

        var entity = new Sterilization
        {
            CycleNo = request.CycleNo.Trim(),
            SterilizerId = request.SterilizerId,
            SterilizationType = request.SterilizationType.Trim(),
            CycleProgram = string.IsNullOrWhiteSpace(request.CycleProgram) ? null : request.CycleProgram.Trim(),
            CycleDateTime = request.CycleDateTimeUtc,
            OperatorName = request.OperatorName.Trim(),
            TemperatureC = request.TemperatureC,
            Pressure = request.Pressure,
            ExposureTimeMinutes = request.ExposureTimeMinutes,
            BiLotNo = string.IsNullOrWhiteSpace(request.BiLotNo) ? null : request.BiLotNo.Trim(),
            BiResult = string.IsNullOrWhiteSpace(request.BiResult) ? null : request.BiResult.Trim(),
            BiTimeIn = RegisterLoadBiTimeInRules.BiTimeInUtcForCreate(request),
            LoadQty = SterilizationLoadQty.FromItems(request.Items),
            CycleStatus = request.CycleStatus,
            DoctorRoomId = request.DoctorRoomId,
            Implants = request.Implants,
            BiDaily = request.Implants ? true : null,
            Notes = request.Notes
        };

        foreach (var item in request.Items)
        {
            entity.Items.Add(new SterilizationItem
            {
                DeptItemId = item.DeptItemId,
                DepartmentName = string.IsNullOrWhiteSpace(item.DepartmentName) ? null : item.DepartmentName.Trim(),
                DoctorOrRoom = string.IsNullOrWhiteSpace(item.DoctorOrRoom) ? null : item.DoctorOrRoom.Trim(),
                ItemName = item.ItemName.Trim(),
                Pcs = Math.Max(1, item.Pcs),
                Qty = Math.Max(1, item.Qty)
            });
        }

        if (!string.IsNullOrWhiteSpace(entity.BiResult))
        {
            entity.BiResultUpdatedAt = DateTime.UtcNow;
        }

        db.Sterilizations.Add(entity);
        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await auditService.AppendAsync(
                    db,
                    module: AuditModules.Sterilization,
                    entityName: "tbl_sterilization",
                    entityId: entity.SterilizationId.ToString(),
                    action: AuditActions.SterilizationCreate,
                    actorAccountId: Actor(),
                    clientMachine: request.ClientMachine,
                    oldValues: null,
                    newValues: SterilizationAuditSnapshot(entity, request.Items.Count),
                    correlationId: Guid.NewGuid(),
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync(cancellationToken);
                return (false, DescribeSterilizationSaveError(ex));
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return (true, null);
    }

    public async Task<(bool ok, string? error)> UpdateCycleAsync(int id, SterilizationUpsertDto request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Sterilizations.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.SterilizationId == id, cancellationToken);

        if (entity is null)
        {
            return (false, "Sterilization cycle not found.");
        }

        if (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            return (false, "rowVersion is required.");
        }

        var incomingVersion = Convert.FromBase64String(request.RowVersion);
        if (!entity.RowVersion.SequenceEqual(incomingVersion))
        {
            return (false, "Someone updated this record. Press F5 to reload.");
        }

        if (SterilizationUpsertValidator.Validate(request) is { } updateValidationError)
        {
            return (false, updateValidationError);
        }

        var oldSnapshot = SterilizationAuditSnapshot(entity, entity.Items.Count);

        entity.SterilizerId = request.SterilizerId;
        entity.SterilizationType = request.SterilizationType.Trim();
        entity.CycleProgram = string.IsNullOrWhiteSpace(request.CycleProgram) ? null : request.CycleProgram.Trim();
        entity.CycleDateTime = request.CycleDateTimeUtc;
        entity.OperatorName = request.OperatorName.Trim();
        entity.TemperatureC = request.TemperatureC;
        entity.Pressure = request.Pressure;
        entity.ExposureTimeMinutes = request.ExposureTimeMinutes;
        entity.BiLotNo = string.IsNullOrWhiteSpace(request.BiLotNo) ? null : request.BiLotNo.Trim();
        var newBiResult = string.IsNullOrWhiteSpace(request.BiResult) ? null : request.BiResult.Trim();
        if (!string.Equals(entity.BiResult, newBiResult, StringComparison.Ordinal))
        {
            entity.BiResultUpdatedAt = DateTime.UtcNow;
        }

        entity.BiResult = newBiResult;
        entity.CycleStatus = request.CycleStatus;
        entity.DoctorRoomId = request.DoctorRoomId;
        entity.Implants = request.Implants;
        if (request.Implants)
        {
            entity.BiDaily = true;
        }
        entity.Notes = request.Notes;
        entity.LoadQty = SterilizationLoadQty.FromItems(request.Items);
        if (RegisterLoadBiTimeInRules.BiTimeInUtcForUpdate(entity.BiTimeIn, request) is { } biTimeInUtc)
        {
            entity.BiTimeIn = biTimeInUtc;
        }

        db.SterilizationItems.RemoveRange(entity.Items);
        entity.Items = request.Items.Select(x => new SterilizationItem
        {
            SterilizationId = id,
            DeptItemId = x.DeptItemId,
            DepartmentName = string.IsNullOrWhiteSpace(x.DepartmentName) ? null : x.DepartmentName.Trim(),
            DoctorOrRoom = string.IsNullOrWhiteSpace(x.DoctorOrRoom) ? null : x.DoctorOrRoom.Trim(),
            ItemName = x.ItemName.Trim(),
            Pcs = Math.Max(1, x.Pcs),
            Qty = Math.Max(1, x.Qty)
        }).ToList();

        await using (var tx = await db.Database.BeginTransactionAsync(cancellationToken))
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                await auditService.AppendAsync(
                    db,
                    module: AuditModules.Sterilization,
                    entityName: "tbl_sterilization",
                    entityId: id.ToString(),
                    action: AuditActions.SterilizationUpdate,
                    actorAccountId: Actor(),
                    clientMachine: request.ClientMachine,
                    oldValues: oldSnapshot,
                    newValues: SterilizationAuditSnapshot(entity, entity.Items.Count),
                    correlationId: Guid.NewGuid(),
                    cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync(cancellationToken);
                return (false, DescribeSterilizationSaveError(ex));
            }
            catch
            {
                await tx.RollbackAsync(cancellationToken);
                throw;
            }
        }

        return (true, null);
    }

    public async Task<(IReadOnlyList<SterilizerListItemDto> items, string? error)> GetSterilizersAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var list = await db.SterilizerUnits.AsNoTracking()
                .OrderBy(x => x.SterilizerId)
                .Select(x => new SterilizerListItemDto
                {
                    SterilizerId = x.SterilizerId,
                    SterilizerNo = x.SterilizerNumber,
                    Model = x.Model,
                    Manufacturer = x.Manufacturer,
                    PurchaseDate = x.PurchaseDate,
                    IsActive = x.IsActive
                })
                .ToListAsync(cancellationToken);
            return (list, null);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (IsMissingSterilizerColumns(ex))
        {
            return ([], "Database is missing sterilizer columns. Run ddl/003_hsms_sterilizer_manufacturer_purchase_date.sql.");
        }
    }

    public async Task<(IReadOnlyList<DepartmentListItemDto> items, string? error)> GetDepartmentsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var list = await db.Departments.AsNoTracking()
            .OrderBy(x => x.DepartmentId)
            .Select(x => new DepartmentListItemDto
            {
                DepartmentId = x.DepartmentId,
                DepartmentName = x.DepartmentName,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
        return (list, null);
    }

    public async Task<(IReadOnlyList<DoctorRoomListItemDto> items, string? error)> GetDoctorRoomsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var list = await db.DoctorRooms.AsNoTracking()
            .OrderBy(x => x.DoctorRoomId)
            .Select(x => new DoctorRoomListItemDto
            {
                DoctorRoomId = x.DoctorRoomId,
                DoctorName = x.DoctorName,
                Room = x.Room,
                DisplayName = string.IsNullOrWhiteSpace(x.Room) ? x.DoctorName : $"{x.DoctorName} / {x.Room}",
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
        return (list, null);
    }

    public async Task<(IReadOnlyList<DepartmentItemListItemDto> items, string? error)> GetDepartmentItemsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var list = await (from di in db.DepartmentItems.AsNoTracking()
                join d in db.Departments.AsNoTracking() on di.DepartmentId equals d.DepartmentId
                where di.IsActive && d.IsActive
                orderby di.DeptItemId
                select new DepartmentItemListItemDto
                {
                    DeptItemId = di.DeptItemId,
                    DepartmentId = di.DepartmentId,
                    DepartmentName = d.DepartmentName,
                    ItemName = di.ItemName,
                    DefaultPcs = di.DefaultPcs,
                    DefaultQty = di.DefaultQty
                })
            .ToListAsync(cancellationToken);
        return (list, null);
    }

    public async Task<(IReadOnlyList<AccountListItemDto> items, string? error)> GetAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var accounts = await db.Accounts.AsNoTracking()
                .OrderBy(x => x.AccountId)
                .ToListAsync(cancellationToken);
            if (accounts.Count == 0)
            {
                return ([], null);
            }

            var idStrings = accounts.Select(a => a.AccountId.ToString()).ToList();
            var auditProjected = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityName == AuditEntities.Account && idStrings.Contains(a.EntityId))
                .Select(a => new { a.EntityId, a.EventAt, a.Action, a.AuditId })
                .ToListAsync(cancellationToken);
            var auditRows = auditProjected.ConvertAll(x => (x.EntityId, x.EventAt, x.Action, x.AuditId));

            var list = AccountListMapper.Map(accounts, auditRows);
            return (list, null);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 207 && ex.Message.Contains("first_name", StringComparison.OrdinalIgnoreCase))
        {
            return ([], "Database is missing staff profile columns. Run hsms-db/ddl/007_hsms_account_staff_profile.sql.");
        }
    }

    public async Task<(SchemaHealthDto? health, string? error)> GetSchemaHealthAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var checks = new (string Table, string Column, string Label)[]
        {
            ("tbl_sterilizer_no", "manufacturer", "tbl_sterilizer_no.manufacturer"),
            ("tbl_sterilizer_no", "purchase_date", "tbl_sterilizer_no.purchase_date"),
            ("tbl_departments", "department_code", "tbl_departments.department_code"),
            ("tbl_sterilization", "cycle_program", "tbl_sterilization.cycle_program"),
            ("tbl_sterilization", "bi_lot_no", "tbl_sterilization.bi_lot_no"),
            ("tbl_sterilization", "cycle_time_in", "tbl_sterilization.cycle_time_in"),
            ("tbl_sterilization", "bi_processed_result_24m", "tbl_sterilization.bi_processed_result_24m (ddl 013)"),
            ("tbl_account_login", "first_name", "tbl_account_login.first_name")
        };

        var missing = new List<string>();
        foreach (var (table, column, label) in checks)
        {
            if (!await ColumnExistsAsync(db, table, column, cancellationToken))
            {
                missing.Add(label);
            }
        }

        if (missing.Count == 0)
        {
            return (new SchemaHealthDto { IsOk = true, Message = "Schema is up to date." }, null);
        }

        return (new SchemaHealthDto
        {
            IsOk = false,
            MissingItems = missing,
            Message = "Schema migration required. Run latest ddl scripts."
        }, null);
    }

    public async Task<(IReadOnlyList<BiLogSheetRowDto> rows, string? error)> GetBiLogSheetAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string? sterilizationType,
        string? cycleNo,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

            var q = db.Sterilizations.AsNoTracking().AsQueryable();
            if (fromUtc.HasValue) q = q.Where(x => x.CycleDateTime >= fromUtc.Value);
            if (toUtc.HasValue) q = q.Where(x => x.CycleDateTime <= toUtc.Value);
            if (!string.IsNullOrWhiteSpace(sterilizationType))
            {
                q = ApplySterilizationTypeFilter(q, sterilizationType.Trim());
            }
            if (!string.IsNullOrWhiteSpace(cycleNo)) q = q.Where(x => x.CycleNo.StartsWith(cycleNo));

            const int take = 500;
            var rows = await q.OrderByDescending(x => x.CycleDateTime)
                .Take(take)
                .Select(x => new BiLogSheetRowDto
                {
                    SterilizationId = x.SterilizationId,
                    RowVersion = Convert.ToBase64String(x.RowVersion),
                    CycleDateTimeUtc = x.CycleDateTime,
                    SterilizerNo = db.SterilizerUnits
                        .Where(u => u.SterilizerId == x.SterilizerId)
                        .Select(u => u.SterilizerNumber)
                        .FirstOrDefault() ?? x.SterilizerId.ToString(),
                    CycleNo = x.CycleNo,
                    BiLotNo = x.BiLotNo,
                    BiDaily = x.BiDaily,
                    Implants = x.Implants,
                    LoadQty = x.LoadQty ?? x.Items.Sum(i => i.Qty),
                    ExposureTimeMinutes = x.ExposureTimeMinutes,
                    TemperatureC = x.TemperatureC,
                    BiIncubatorTemp = x.BiIncubatorTemp,
                    BiIncubatorChecked = x.BiIncubatorChecked,
                    BiTimeInUtc = x.BiTimeIn,
                    BiTimeInInitials = x.BiTimeInInitials,
                    BiTimeOutUtc = x.BiTimeOut,
                    BiTimeOutInitials = x.BiTimeOutInitials,
                    BiProcessedResult24m = x.BiProcessedResult24m ?? "",
                    BiProcessedValue24m = x.BiProcessedValue24m,
                    BiProcessedResult24h = x.BiProcessedResult24h ?? "",
                    BiProcessedValue24h = x.BiProcessedValue24h,
                    BiControlResult24m = x.BiControlResult24m ?? "",
                    BiControlValue24m = x.BiControlValue24m,
                    BiControlResult24h = x.BiControlResult24h ?? "",
                    BiControlValue24h = x.BiControlValue24h,
                    OperatorName = x.OperatorName,
                    Notes = x.Notes,
                    BiResultUpdatedAtUtc = x.BiResultUpdatedAt
                })
                .ToListAsync(cancellationToken);

            foreach (var row in rows)
            {
                BiLogSheetTimeEditor.SyncEditorsFromUtc(row);
            }

            return (rows, null);
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (IsMissingBiLogColumns(ex))
        {
            return ([], "Database is missing BI log sheet columns. Run hsms-db/ddl/006_hsms_bi_log_sheet_columns.sql and 013_hsms_bi_log_sheet_qa_form.sql.");
        }
    }

    private static async Task<bool> ColumnExistsAsync(HsmsDbContext db, string tableName, string columnName, CancellationToken cancellationToken)
    {
        var sql = $"""
                   SELECT CASE WHEN EXISTS (
                       SELECT 1
                       FROM INFORMATION_SCHEMA.COLUMNS
                       WHERE TABLE_SCHEMA = 'dbo'
                         AND TABLE_NAME = '{tableName}'
                         AND COLUMN_NAME = '{columnName}'
                   ) THEN CAST(1 AS bit) ELSE CAST(0 AS bit) END AS [Value]
                   """;

        var result = await db.Database.SqlQueryRaw<bool>(sql).SingleAsync(cancellationToken);
        return result;
    }

    private static bool IsMissingSterilizerColumns(Microsoft.Data.SqlClient.SqlException ex)
    {
        return ex.Number == 207 &&
               (ex.Message.Contains("manufacturer", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("purchase_date", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMissingBiLogColumns(Microsoft.Data.SqlClient.SqlException ex)
    {
        if (ex.Number != 207) return false;
        return ex.Message.Contains("cycle_time_in", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("cycle_time_out", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_lot_no", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("load_qty", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("exposure_time_minutes", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("temperature_in_c", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("temperature_out_c", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_strip_no", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_time_in", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_time_out", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_time_cut", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_result_updated_at", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_daily", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_incubator_temp", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_incubator_checked", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_processed_result_24m", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("bi_control_result_24m", StringComparison.OrdinalIgnoreCase);
    }

    private string? ForbidUnlessAdmin()
    {
        return RoleAuthorization.RequireAdmin(currentUser.GetCurrentUser()) is { } d ? d.Message : null;
    }

    private static object SterilizationAuditSnapshot(Sterilization e, int itemCount) =>
        new
        {
            e.CycleNo,
            e.SterilizationType,
            e.CycleProgram,
            e.CycleStatus,
            e.OperatorName,
            notesPreview = AuditTextPreview(e.Notes, 120),
            itemCount
        };

    private static IQueryable<Sterilization> ApplySterilizationTypeFilter(IQueryable<Sterilization> q, string filter)
    {
        if (string.Equals(filter, "High temperature", StringComparison.OrdinalIgnoreCase))
        {
            return q.Where(x =>
                x.SterilizationType == "High temperature" ||
                x.SterilizationType == "Steam (high temperature)");
        }

        if (string.Equals(filter, "Low temperature", StringComparison.OrdinalIgnoreCase))
        {
            return q.Where(x =>
                x.SterilizationType == "Low temperature" ||
                x.SterilizationType == "Steam (low temperature)");
        }

        return q.Where(x => x.SterilizationType == filter);
    }

    private static string? AuditTextPreview(string? value, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= maxChars ? t : t[..maxChars] + "…";
    }

    private static string DescribeSterilizationSaveError(DbUpdateException ex)
    {
        var inner = DeepestSqlMessage(ex);
        if (inner.Contains("overflow", StringComparison.OrdinalIgnoreCase) ||
            inner.Contains("Arithmetic", StringComparison.OrdinalIgnoreCase))
        {
            return "Cannot save this record: invalid input — a number is too large or too small for the database (often Temperature °C or Pressure). " +
                   $"Temperature must be between -{SterilizationUpsertValidator.MaxTemperatureMagnitude} and +{SterilizationUpsertValidator.MaxTemperatureMagnitude} °C; " +
                   $"pressure between -{SterilizationUpsertValidator.MaxPressureMagnitude} and +{SterilizationUpsertValidator.MaxPressureMagnitude}. " +
                   "Fix those fields and save again.";
        }

        if (inner.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase) ||
            inner.Contains("REFERENCE", StringComparison.OrdinalIgnoreCase))
        {
            return "Cannot save this record: invalid input — a choice on the form does not match the master data (for example sterilizer, doctor/room, or item). " +
                   "Press F5 to refresh the lists, pick a valid option, and try again.";
        }

        if (inner.Contains("affected 0 row", StringComparison.OrdinalIgnoreCase) ||
            inner.Contains("expected to affect", StringComparison.OrdinalIgnoreCase) ||
            ex is DbUpdateConcurrencyException)
        {
            return "Cannot save this record: it was changed in the database since the screen was loaded (row version mismatch). Press Go on the BI log sheet to refresh, then try again.";
        }

        if (inner.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase) &&
            inner.Contains("bi_result_updated_at", StringComparison.OrdinalIgnoreCase))
        {
            return "Cannot save BI result: the database is missing column bi_result_updated_at. Run hsms-db/ddl/012_hsms_bi_result_updated_at.sql on this server, then try again.";
        }

        if (inner.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase) &&
            (inner.Contains("bi_daily", StringComparison.OrdinalIgnoreCase) ||
             inner.Contains("bi_incubator_temp", StringComparison.OrdinalIgnoreCase) ||
             inner.Contains("bi_incubator_checked", StringComparison.OrdinalIgnoreCase) ||
             inner.Contains("bi_processed_result_24m", StringComparison.OrdinalIgnoreCase)))
        {
            return "Cannot save BI log sheet: the database is missing QA form columns. Run hsms-db/ddl/013_hsms_bi_log_sheet_qa_form.sql on this server, then try again.";
        }

        if (inner.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase))
        {
            return "Cannot save: the database schema is out of date for this app version. Run the latest hsms-db/ddl scripts, then try again. Detail: " + TruncateDetail(inner, 220);
        }

        return "Cannot save this record. " + TruncateDetail(inner, 400);
    }

    private static string DeepestSqlMessage(Exception ex)
    {
        var cur = ex;
        while (cur.InnerException is not null)
        {
            cur = cur.InnerException;
        }

        return cur.Message ?? ex.Message;
    }

    private static string TruncateDetail(string message, int maxLen)
    {
        if (string.IsNullOrEmpty(message))
        {
            return "";
        }

        var t = message.Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= maxLen ? t : t[..maxLen] + "…";
    }
}
