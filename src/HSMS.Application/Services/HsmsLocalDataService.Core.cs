using System.Globalization;
using HSMS.Application.Audit;
using HSMS.Application.Security;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Persistence.Services;
using HSMS.Shared.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService(
    IDbContextFactory<HsmsDbContext> dbFactory,
    IAuditService auditService,
    ICurrentUserAccessor currentUser) : IHsmsDataService
{
    private int? Actor() => currentUser.GetCurrentUser()?.AccountId;
    private CurrentUser? User() => currentUser.GetCurrentUser();
    private bool IsAdmin() => string.Equals(User()?.Role, RoleAuthorization.AdminRoleName, StringComparison.Ordinal);

    private string? DenyIfNotOwnerOrAdmin(int? createdByAccountId)
    {
        var user = User();
        if (RoleAuthorization.RequireAuthenticated(user) is { } deniedAuth)
        {
            return deniedAuth.Message;
        }

        if (IsAdmin())
        {
            return null;
        }

        if (createdByAccountId is null)
        {
            return "This record was created by an older version of HSMS and has no owner. Only an administrator can edit it.";
        }

        if (user is null || user.AccountId != createdByAccountId.Value)
        {
            return "You can only edit records that you created. You can still view and print/export this record.";
        }

        return null;
    }

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
        return await SearchCyclesFilteredAsync(cycleNo, fromUtc: null, toUtc: null, cancellationToken, matchCycleNoOnly: true);
    }

    public async Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> SearchCyclesFilteredAsync(
        string searchQuery,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default,
        bool matchCycleNoOnly = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var take = string.IsNullOrWhiteSpace(searchQuery)
            ? 500
            : (matchCycleNoOnly ? 200 : 350);
        var q = db.Sterilizations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var raw = searchQuery.Trim();
            if (matchCycleNoOnly)
            {
                q = q.Where(x => x.CycleNo.StartsWith(raw));
            }
            else
            {
                int? byId = int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId) && parsedId > 0
                    ? parsedId
                    : null;

                q = q.Where(x =>
                    (byId.HasValue && x.SterilizationId == byId.Value) ||
                    x.CycleNo.Contains(raw) ||
                    (x.OperatorName ?? "").Contains(raw) ||
                    (x.CycleStatus ?? "").Contains(raw) ||
                    (x.BiLotNo ?? "").Contains(raw) ||
                    (x.BiResult ?? "").Contains(raw) ||
                    (x.SterilizationType ?? "").Contains(raw) ||
                    (x.Notes ?? "").Contains(raw) ||
                    (x.BiIncubatorTemp ?? "").Contains(raw) ||
                    db.SterilizerUnits.Any(u =>
                        u.SterilizerId == x.SterilizerId &&
                        u.SterilizerNumber.Contains(raw)) ||
                    x.Items.Any(i =>
                        (i.ItemName ?? "").Contains(raw) ||
                        (i.DepartmentName ?? "").Contains(raw) ||
                        (i.DoctorOrRoom ?? "").Contains(raw)));
            }
        }

        if (fromUtc.HasValue) q = q.Where(x => x.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) q = q.Where(x => x.CreatedAt <= toUtc.Value);

        var batch = await q.OrderByDescending(x => x.CycleDateTime)
            .Take(take)
            .Select(x => new
            {
                x.SterilizationId,
                x.CycleNo,
                x.CycleProgram,
                x.CycleDateTime,
                x.CycleTimeIn,
                x.CycleTimeOut,
                x.CycleStatus,
                x.SterilizerId,
                x.CreatedAt,
                x.CreatedBy,
                x.OperatorName,
                TotalPcs = x.Items.Sum(i => (int?)i.Pcs) ?? 0,
                TotalQty = x.Items.Sum(i => (int?)i.Qty) ?? 0,
                x.RowVersion
            })
            .ToListAsync(cancellationToken);

        var sterIds = batch.Select(x => x.SterilizerId).Distinct().ToList();
        var sterNames = sterIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SterilizerUnits.AsNoTracking()
                .Where(u => sterIds.Contains(u.SterilizerId))
                .ToDictionaryAsync(u => u.SterilizerId, u => u.SterilizerNumber, cancellationToken);

        var data = batch.ConvertAll(x => new SterilizationSearchItemDto
        {
            SterilizationId = x.SterilizationId,
            CycleNo = x.CycleNo,
            CycleProgram = x.CycleProgram,
            CycleDateTimeUtc = x.CycleDateTime,
            CycleTimeInUtc = x.CycleTimeIn,
            RegisteredAtUtc = x.CreatedAt,
            CycleTimeOutUtc = x.CycleTimeOut,
            CycleStatus = x.CycleStatus,
            CreatedByAccountId = x.CreatedBy,
            OperatorName = x.OperatorName,
            TotalPcs = x.TotalPcs,
            TotalQty = x.TotalQty,
            SterilizerNo = sterNames.GetValueOrDefault(x.SterilizerId) ?? x.SterilizerId.ToString(CultureInfo.InvariantCulture),
            RowVersion = Convert.ToBase64String(x.RowVersion)
        });

        return (data, null);
    }

    public async Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> ListLoadsByCycleProgramAsync(
        string cycleProgramContains,
        string? searchQuery,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default)
    {
        // These tabs are driven by load metadata (tbl_sterilization + tbl_str_items), not by test records.
        // We treat CycleProgram as the authoritative "cycle name" and filter by substring match.
        // Users can narrow via date range + search box.
        var q = (searchQuery ?? "").Trim();
        var take = string.IsNullOrWhiteSpace(q) ? 500 : 350;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var set = db.Sterilizations.AsNoTracking().AsQueryable();

        var contains = (cycleProgramContains ?? "").Trim();
        if (contains.Length == 0)
        {
            return ([], "Cycle program filter is required.");
        }

        set = set.Where(x => x.CycleProgram != null && x.CycleProgram.Contains(contains));

        if (fromUtc.HasValue) set = set.Where(x => x.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) set = set.Where(x => x.CreatedAt <= toUtc.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Reuse the same broad match semantics as SearchCyclesFilteredAsync (non-cycleNo-only path).
            set = set.Where(x =>
                x.CycleNo.Contains(q) ||
                (x.OperatorName ?? "").Contains(q) ||
                (x.CycleStatus ?? "").Contains(q) ||
                (x.Notes ?? "").Contains(q) ||
                db.SterilizerUnits.Any(u =>
                    u.SterilizerId == x.SterilizerId &&
                    u.SterilizerNumber.Contains(q)) ||
                x.Items.Any(i =>
                    (i.ItemName ?? "").Contains(q) ||
                    (i.DepartmentName ?? "").Contains(q) ||
                    (i.DoctorOrRoom ?? "").Contains(q)));
        }

        var batch = await set
            .OrderByDescending(x => x.CycleDateTime)
            .Take(take)
            .Select(x => new
            {
                x.SterilizationId,
                x.CycleNo,
                x.CycleProgram,
                x.CycleDateTime,
                x.CycleTimeIn,
                x.CycleTimeOut,
                x.CycleStatus,
                x.SterilizerId,
                x.CreatedAt,
                x.CreatedBy,
                x.OperatorName,
                TotalPcs = x.Items.Sum(i => (int?)i.Pcs) ?? 0,
                TotalQty = x.Items.Sum(i => (int?)i.Qty) ?? 0,
                x.RowVersion
            })
            .ToListAsync(cancellationToken);

        var sterIds = batch.Select(x => x.SterilizerId).Distinct().ToList();
        var sterNames = sterIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SterilizerUnits.AsNoTracking()
                .Where(u => sterIds.Contains(u.SterilizerId))
                .ToDictionaryAsync(u => u.SterilizerId, u => u.SterilizerNumber, cancellationToken);

        var data = batch.ConvertAll(x => new SterilizationSearchItemDto
        {
            SterilizationId = x.SterilizationId,
            CycleNo = x.CycleNo,
            CycleProgram = x.CycleProgram,
            CycleDateTimeUtc = x.CycleDateTime,
            CycleTimeInUtc = x.CycleTimeIn,
            RegisteredAtUtc = x.CreatedAt,
            CycleTimeOutUtc = x.CycleTimeOut,
            CycleStatus = x.CycleStatus,
            CreatedByAccountId = x.CreatedBy,
            OperatorName = x.OperatorName,
            TotalPcs = x.TotalPcs,
            TotalQty = x.TotalQty,
            SterilizerNo = sterNames.GetValueOrDefault(x.SterilizerId) ?? x.SterilizerId.ToString(CultureInfo.InvariantCulture),
            RowVersion = Convert.ToBase64String(x.RowVersion)
        });

        return (data, null);
    }

    public async Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> ListLoadsWithBiAsync(
        string? searchQuery,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken cancellationToken = default)
    {
        // "BI = YES" in the desktop UI means the BI section is enabled/filled.
        // The existing register-load screen considers this true when BiLotNo or BiResult is present.
        var q = (searchQuery ?? "").Trim();
        var take = string.IsNullOrWhiteSpace(q) ? 500 : 350;

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var set = db.Sterilizations.AsNoTracking().AsQueryable();

        set = set.Where(x => !string.IsNullOrWhiteSpace(x.BiLotNo) || !string.IsNullOrWhiteSpace(x.BiResult));

        if (fromUtc.HasValue) set = set.Where(x => x.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) set = set.Where(x => x.CreatedAt <= toUtc.Value);

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Broad match: same semantics as other load listing/search paths.
            set = set.Where(x =>
                x.CycleNo.Contains(q) ||
                (x.OperatorName ?? "").Contains(q) ||
                (x.CycleStatus ?? "").Contains(q) ||
                (x.CycleProgram ?? "").Contains(q) ||
                (x.BiLotNo ?? "").Contains(q) ||
                (x.BiResult ?? "").Contains(q) ||
                (x.Notes ?? "").Contains(q) ||
                db.SterilizerUnits.Any(u =>
                    u.SterilizerId == x.SterilizerId &&
                    u.SterilizerNumber.Contains(q)) ||
                x.Items.Any(i =>
                    (i.ItemName ?? "").Contains(q) ||
                    (i.DepartmentName ?? "").Contains(q) ||
                    (i.DoctorOrRoom ?? "").Contains(q)));
        }

        var batch = await set
            .OrderByDescending(x => x.CycleDateTime)
            .Take(take)
            .Select(x => new
            {
                x.SterilizationId,
                x.CycleNo,
                x.CycleProgram,
                x.CycleDateTime,
                x.CycleTimeIn,
                x.CycleTimeOut,
                x.CycleStatus,
                x.SterilizerId,
                x.CreatedAt,
                x.CreatedBy,
                x.OperatorName,
                TotalPcs = x.Items.Sum(i => (int?)i.Pcs) ?? 0,
                TotalQty = x.Items.Sum(i => (int?)i.Qty) ?? 0,
                x.RowVersion
            })
            .ToListAsync(cancellationToken);

        var sterIds = batch.Select(x => x.SterilizerId).Distinct().ToList();
        var sterNames = sterIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SterilizerUnits.AsNoTracking()
                .Where(u => sterIds.Contains(u.SterilizerId))
                .ToDictionaryAsync(u => u.SterilizerId, u => u.SterilizerNumber, cancellationToken);

        var data = batch.ConvertAll(x => new SterilizationSearchItemDto
        {
            SterilizationId = x.SterilizationId,
            CycleNo = x.CycleNo,
            CycleProgram = x.CycleProgram,
            CycleDateTimeUtc = x.CycleDateTime,
            CycleTimeInUtc = x.CycleTimeIn,
            RegisteredAtUtc = x.CreatedAt,
            CycleTimeOutUtc = x.CycleTimeOut,
            CycleStatus = x.CycleStatus,
            CreatedByAccountId = x.CreatedBy,
            OperatorName = x.OperatorName,
            TotalPcs = x.TotalPcs,
            TotalQty = x.TotalQty,
            SterilizerNo = sterNames.GetValueOrDefault(x.SterilizerId) ?? x.SterilizerId.ToString(CultureInfo.InvariantCulture),
            RowVersion = Convert.ToBase64String(x.RowVersion)
        });

        return (data, null);
    }

    public async Task<(IReadOnlyList<SterilizationSearchItemDto> items, string? error)> AnalyticsDrilldownAsync(
        AnalyticsFilterDto filter,
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var s = Math.Max(0, skip);
        var t = Math.Clamp(take, 1, 500);

        var q = db.Sterilizations.AsNoTracking().AsQueryable();
        q = ApplySterilizationAnalyticsFilterV2(q, filter ?? new AnalyticsFilterDto());

        var batch = await q.OrderByDescending(x => x.CycleDateTime)
            .Skip(s)
            .Take(t)
            .Select(x => new
            {
                x.SterilizationId,
                x.CycleNo,
                x.CycleProgram,
                x.CycleDateTime,
                x.CycleTimeIn,
                x.CycleTimeOut,
                x.CycleStatus,
                x.SterilizerId,
                x.CreatedAt,
                x.CreatedBy,
                x.OperatorName,
                TotalPcs = x.Items.Sum(i => (int?)i.Pcs) ?? 0,
                TotalQty = x.Items.Sum(i => (int?)i.Qty) ?? 0,
                x.RowVersion
            })
            .ToListAsync(cancellationToken);

        var sterIds = batch.Select(x => x.SterilizerId).Distinct().ToList();
        var sterNames = sterIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SterilizerUnits.AsNoTracking()
                .Where(u => sterIds.Contains(u.SterilizerId))
                .ToDictionaryAsync(u => u.SterilizerId, u => u.SterilizerNumber, cancellationToken);

        var data = batch.ConvertAll(x => new SterilizationSearchItemDto
        {
            SterilizationId = x.SterilizationId,
            CycleNo = x.CycleNo,
            CycleProgram = x.CycleProgram,
            CycleDateTimeUtc = x.CycleDateTime,
            CycleTimeInUtc = x.CycleTimeIn,
            RegisteredAtUtc = x.CreatedAt,
            CycleTimeOutUtc = x.CycleTimeOut,
            CycleStatus = x.CycleStatus,
            CreatedByAccountId = x.CreatedBy,
            OperatorName = x.OperatorName,
            TotalPcs = x.TotalPcs,
            TotalQty = x.TotalQty,
            SterilizerNo = sterNames.GetValueOrDefault(x.SterilizerId) ?? x.SterilizerId.ToString(CultureInfo.InvariantCulture),
            RowVersion = Convert.ToBase64String(x.RowVersion)
        });

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
            CreatedByAccountId = row.CreatedBy,
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

    private static IQueryable<Sterilization> ApplySterilizationAnalyticsFilter(
        IQueryable<Sterilization> source,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? operatorName)
    {
        var q = source.AsQueryable();
        if (fromUtc.HasValue) q = q.Where(x => x.CreatedAt >= fromUtc.Value);
        if (toUtc.HasValue) q = q.Where(x => x.CreatedAt <= toUtc.Value);
        if (!string.IsNullOrWhiteSpace(operatorName))
        {
            var op = operatorName.Trim();
            q = q.Where(x => x.OperatorName == op);
        }

        return q;
    }

    private static IQueryable<Sterilization> ApplySterilizationAnalyticsFilterV2(
        IQueryable<Sterilization> source,
        AnalyticsFilterDto filter) =>
        Application.Analytics.SterilizationAnalyticsQueryBuilder.Apply(source, filter);

    public async Task<(SterilizationAnalyticsDto? analytics, string? error)> GetSterilizationAnalyticsV2Async(
        AnalyticsDashboardQueryDto query,
        CancellationToken cancellationToken = default)
    {
        if (Application.Security.AnalyticsAuthorization.RequireView(User()) is { } denied)
        {
            return (null, denied.Message);
        }

        if (query is null)
        {
            return (null, "Analytics query is missing.");
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var baseSet = db.Sterilizations.AsNoTracking().AsQueryable();
            var q = ApplySterilizationAnalyticsFilterV2(baseSet, query.Filter ?? new AnalyticsFilterDto());

            // Reuse the existing aggregation engine by feeding derived primitives:
            // - from/to bounds come from Filter
            // - operatorName constraint is already in the query via Filter.OperatorName (if set)
            // - compare window comes from query
            return await GetSterilizationAnalyticsFromQueryableAsync(
                db,
                q,
                compareFromUtc: query.CompareFromUtc,
                compareToUtc: query.CompareToUtc,
                operatorNameForCompare: query.Filter?.OperatorName,
                cancellationToken);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    private async Task<(SterilizationAnalyticsDto? analytics, string? error)> GetSterilizationAnalyticsFromQueryableAsync(
        HsmsDbContext db,
        IQueryable<Sterilization> q,
        DateTime? compareFromUtc,
        DateTime? compareToUtc,
        string? operatorNameForCompare,
        CancellationToken cancellationToken)
    {
        var totals = await q.GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Draft = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Draft),
                Completed = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Completed),
                Voided = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Voided),
                Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
            })
            .FirstOrDefaultAsync(cancellationToken);

        var topOperatorsRaw = await q
            .GroupBy(x => x.OperatorName)
            .Select(g => new
            {
                Operator = g.Key,
                Loads = g.Count(),
                Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
            })
            .OrderByDescending(x => x.Loads)
            .ThenBy(x => x.Operator)
            .Take(15)
            .ToListAsync(cancellationToken);

        var topSterilizerRaw = await q
            .GroupBy(x => x.SterilizerId)
            .Select(g => new
            {
                SterilizerId = g.Key,
                Loads = g.Count(),
                Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
            })
            .OrderByDescending(x => x.Loads)
            .ThenBy(x => x.SterilizerId)
            .Take(15)
            .ToListAsync(cancellationToken);

        var sterIds = topSterilizerRaw.Select(x => x.SterilizerId).Distinct().ToList();
        var sterNames = sterIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SterilizerUnits.AsNoTracking()
                .Where(u => sterIds.Contains(u.SterilizerId))
                .ToDictionaryAsync(u => u.SterilizerId, u => u.SterilizerNumber, cancellationToken);

        var byDay = await q
            .GroupBy(x => x.CreatedAt.Date)
            .Select(g => new
            {
                DayUtc = g.Key,
                Loads = g.Count(),
                Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
            })
            .OrderBy(x => x.DayUtc)
            .Take(400)
            .ToListAsync(cancellationToken);

        var byTypeRaw = await q
            .GroupBy(x => string.IsNullOrWhiteSpace(x.SterilizationType) ? "(blank)" : x.SterilizationType.Trim())
            .Select(g => new
            {
                Key = g.Key,
                Loads = g.Count(),
                Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
            })
            .OrderByDescending(x => x.Loads)
            .Take(20)
            .ToListAsync(cancellationToken);

        var byBiRaw = await q
            .GroupBy(x => string.IsNullOrWhiteSpace(x.BiResult) ? "(blank)" : x.BiResult!.Trim())
            .Select(g => new
            {
                Key = g.Key,
                Loads = g.Count(),
                Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
            })
            .OrderByDescending(x => x.Loads)
            .Take(20)
            .ToListAsync(cancellationToken);

        var byDeptRaw = await q
            .SelectMany(s => s.Items)
            .GroupBy(i => string.IsNullOrWhiteSpace(i.DepartmentName) ? "(blank)" : i.DepartmentName!.Trim())
            .Select(g => new
            {
                Key = g.Key,
                Lines = g.Count(),
                Pcs = g.Sum(i => i.Pcs),
                Qty = g.Sum(i => i.Qty)
            })
            .OrderByDescending(x => x.Qty)
            .Take(20)
            .ToListAsync(cancellationToken);

        var topItemsRaw = await q
            .SelectMany(s => s.Items)
            .GroupBy(i => string.IsNullOrWhiteSpace(i.ItemName) ? "(blank)" : i.ItemName.Trim())
            .Select(g => new
            {
                Key = g.Key,
                Lines = g.Count(),
                Pcs = g.Sum(i => i.Pcs),
                Qty = g.Sum(i => i.Qty)
            })
            .OrderByDescending(x => x.Qty)
            .Take(15)
            .ToListAsync(cancellationToken);

        // NOTE: QA summary here is filter-independent; callers should add QA constraints via V2 filter if needed.
        var qaQuery =
            from t in db.QaTests.AsNoTracking()
            join s in db.Sterilizations.AsNoTracking() on t.SterilizationId equals s.SterilizationId
            where q.Select(x => x.SterilizationId).Contains(s.SterilizationId)
            select t;

        var qaGrouped = await qaQuery.GroupBy(t => new { t.TestType, t.Result })
            .Select(g => new { g.Key.TestType, g.Key.Result, Cnt = g.Count() })
            .ToListAsync(cancellationToken);
        var pendingQa = await qaQuery.CountAsync(t => t.ApprovedAt == null, cancellationToken);

        var doctorRoomRaw = await q
            .GroupBy(x => x.DoctorRoomId)
            .Select(g => new
            {
                DoctorRoomId = g.Key,
                Loads = g.Count(),
                Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
            })
            .OrderByDescending(x => x.Loads)
            .Take(25)
            .ToListAsync(cancellationToken);

        var doctorIds = doctorRoomRaw
            .Where(x => x.DoctorRoomId != null)
            .Select(x => x.DoctorRoomId!.Value)
            .Distinct()
            .ToList();
        var doctorLabelById = doctorIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.DoctorRooms.AsNoTracking()
                .Where(dr => doctorIds.Contains(dr.DoctorRoomId))
                .ToDictionaryAsync(
                    dr => dr.DoctorRoomId,
                    dr => AnalyticsDoctorRoomLabel(dr.DoctorName, dr.Room),
                    cancellationToken);

        AnalyticsBiLogPaperSummaryDto? biPaperDto = null;
        try
        {
            biPaperDto = await BuildBiLogPaperAnalyticsAsync(q, cancellationToken);
        }
        catch (SqlException sqlPaper) when (IsMissingAnalyticsBiPaperColumns(sqlPaper))
        {
            biPaperDto = null;
        }

        AnalyticsInstrumentSummaryDto? instDto = null;
        try
        {
            var instQ = db.InstrumentChecks.AsNoTracking().AsQueryable();
            // Instrument checks are independent; keep existing behavior (range filters will be handled in higher-level BI analytics later).
            instDto = new AnalyticsInstrumentSummaryDto
            {
                TotalChecks = await instQ.CountAsync(cancellationToken),
                WitnessPending = await instQ.CountAsync(x => x.WitnessApprovedAt == null, cancellationToken),
                WitnessApproved = await instQ.CountAsync(x => x.WitnessApprovedAt != null, cancellationToken)
            };
        }
        catch (SqlException sqlEx) when (sqlEx.Number == 208)
        {
            instDto = null;
        }

        AnalyticsPeriodCompareDto? compareDto = null;
        if (compareFromUtc.HasValue && compareToUtc.HasValue && compareToUtc.Value >= compareFromUtc.Value)
        {
            var baseSet = db.Sterilizations.AsNoTracking().AsQueryable();
            var cq = ApplySterilizationAnalyticsFilter(baseSet, compareFromUtc, compareToUtc, operatorNameForCompare);
            var cmp = await cq.GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Draft = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Draft),
                    Completed = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Completed),
                    Voided = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Voided),
                    Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                    Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                })
                .FirstOrDefaultAsync(cancellationToken);
            compareDto = new AnalyticsPeriodCompareDto
            {
                PeriodStartUtc = compareFromUtc.Value,
                PeriodEndUtc = compareToUtc.Value,
                TotalLoads = cmp?.Total ?? 0,
                DraftLoads = cmp?.Draft ?? 0,
                CompletedLoads = cmp?.Completed ?? 0,
                VoidedLoads = cmp?.Voided ?? 0,
                TotalPcs = cmp?.Pcs ?? 0,
                TotalQty = cmp?.Qty ?? 0
            };
        }

        var dto = new SterilizationAnalyticsDto
        {
            TotalLoads = totals?.Total ?? 0,
            DraftLoads = totals?.Draft ?? 0,
            CompletedLoads = totals?.Completed ?? 0,
            VoidedLoads = totals?.Voided ?? 0,
            TotalPcs = totals?.Pcs ?? 0,
            TotalQty = totals?.Qty ?? 0,
            ComparePriorPeriod = compareDto,
            ByOperator = topOperatorsRaw.ConvertAll(x => new AnalyticsOperatorSummaryRowDto
            {
                OperatorName = string.IsNullOrWhiteSpace(x.Operator) ? "(blank)" : x.Operator.Trim(),
                Loads = x.Loads,
                Pcs = x.Pcs,
                Qty = x.Qty
            }),
            BySterilizer = topSterilizerRaw.ConvertAll(x => new AnalyticsSterilizerSummaryRowDto
            {
                SterilizerId = x.SterilizerId,
                SterilizerNo = sterNames.GetValueOrDefault(x.SterilizerId) ?? x.SterilizerId.ToString(CultureInfo.InvariantCulture),
                Loads = x.Loads,
                Pcs = x.Pcs,
                Qty = x.Qty
            }),
            ByDay = byDay.ConvertAll(x => new AnalyticsDaySummaryRowDto
            {
                DayUtc = DateTime.SpecifyKind(x.DayUtc, DateTimeKind.Utc),
                Loads = x.Loads,
                Pcs = x.Pcs,
                Qty = x.Qty
            }),
            BySterilizationType = TopNWithOthers(byTypeRaw.ConvertAll(x => new AnalyticsBreakdownRowDto
            {
                Key = x.Key,
                Loads = x.Loads,
                Pcs = x.Pcs,
                Qty = x.Qty
            }), 10, "(others)"),
            ByBiResult = TopNWithOthers(byBiRaw.ConvertAll(x => new AnalyticsBreakdownRowDto
            {
                Key = x.Key,
                Loads = x.Loads,
                Pcs = x.Pcs,
                Qty = x.Qty
            }), 10, "(others)"),
            ByDepartment = TopNWithOthers(byDeptRaw.ConvertAll(x => new AnalyticsBreakdownRowDto
            {
                Key = x.Key,
                Loads = x.Lines,
                Pcs = x.Pcs,
                Qty = x.Qty
            }), 10, "(others)"),
            TopItemsByQty = TopNWithOthers(topItemsRaw.ConvertAll(x => new AnalyticsBreakdownRowDto
            {
                Key = x.Key,
                Loads = x.Lines,
                Pcs = x.Pcs,
                Qty = x.Qty
            }), 10, "(others)"),
            ByDoctorRoom = TopNWithOthers(doctorRoomRaw.ConvertAll(x =>
            {
                var key = x.DoctorRoomId is null
                    ? "(unassigned)"
                    : doctorLabelById.GetValueOrDefault(x.DoctorRoomId.Value)
                      ?? $"Doctor room #{x.DoctorRoomId.Value.ToString(CultureInfo.InvariantCulture)}";
                return new AnalyticsBreakdownRowDto { Id = x.DoctorRoomId, Key = key, Loads = x.Loads, Pcs = x.Pcs, Qty = x.Qty };
            }), 10, "(others)"),
            BiLogPaper = biPaperDto,
            QaTests = new AnalyticsQaSummaryDto
            {
                LeakPass = qaGrouped.Where(x => string.Equals(x.TestType, "Leak", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Result, "Pass", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Cnt),
                LeakFail = qaGrouped.Where(x => string.Equals(x.TestType, "Leak", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Result, "Fail", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Cnt),
                BowiePass = qaGrouped.Where(x => string.Equals(x.TestType, "BowieDick", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Result, "Pass", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Cnt),
                BowieFail = qaGrouped.Where(x => string.Equals(x.TestType, "BowieDick", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Result, "Fail", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Cnt),
                PendingApproval = pendingQa
            },
            InstrumentChecks = instDto
        };

        return (dto, null);
    }

    public async Task<(SterilizationAnalyticsDto? analytics, string? error)> GetSterilizationAnalyticsAsync(
        DateTime? fromUtc,
        DateTime? toUtc,
        string? operatorName,
        CancellationToken cancellationToken = default,
        DateTime? compareFromUtc = null,
        DateTime? compareToUtc = null)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var baseSet = db.Sterilizations.AsNoTracking().AsQueryable();
            var q = ApplySterilizationAnalyticsFilter(baseSet, fromUtc, toUtc, operatorName);

            var totals = await q.GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Draft = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Draft),
                    Completed = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Completed),
                    Voided = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Voided),
                    Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                    Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                })
                .FirstOrDefaultAsync(cancellationToken);

            var topOperatorsRaw = await q
                .GroupBy(x => x.OperatorName)
                .Select(g => new
                {
                    Operator = g.Key,
                    Loads = g.Count(),
                    Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                    Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                })
                .OrderByDescending(x => x.Loads)
                .ThenBy(x => x.Operator)
                .Take(15)
                .ToListAsync(cancellationToken);

            var topSterilizerRaw = await q
                .GroupBy(x => x.SterilizerId)
                .Select(g => new
                {
                    SterilizerId = g.Key,
                    Loads = g.Count(),
                    Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                    Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                })
                .OrderByDescending(x => x.Loads)
                .ThenBy(x => x.SterilizerId)
                .Take(15)
                .ToListAsync(cancellationToken);

            var sterIds = topSterilizerRaw.Select(x => x.SterilizerId).Distinct().ToList();
            var sterNames = sterIds.Count == 0
                ? new Dictionary<int, string>()
                : await db.SterilizerUnits.AsNoTracking()
                    .Where(u => sterIds.Contains(u.SterilizerId))
                    .ToDictionaryAsync(u => u.SterilizerId, u => u.SterilizerNumber, cancellationToken);

            var byDay = await q
                .GroupBy(x => x.CreatedAt.Date)
                .Select(g => new
                {
                    DayUtc = g.Key,
                    Loads = g.Count(),
                    Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                    Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                })
                .OrderBy(x => x.DayUtc)
                .Take(400)
                .ToListAsync(cancellationToken);

            var byTypeRaw = await q
                .GroupBy(x => string.IsNullOrWhiteSpace(x.SterilizationType) ? "(blank)" : x.SterilizationType.Trim())
                .Select(g => new
                {
                    Key = g.Key,
                    Loads = g.Count(),
                    Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                    Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                })
                .OrderByDescending(x => x.Loads)
                .Take(20)
                .ToListAsync(cancellationToken);

            var byBiRaw = await q
                .GroupBy(x => string.IsNullOrWhiteSpace(x.BiResult) ? "(blank)" : x.BiResult!.Trim())
                .Select(g => new
                {
                    Key = g.Key,
                    Loads = g.Count(),
                    Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                    Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                })
                .OrderByDescending(x => x.Loads)
                .Take(20)
                .ToListAsync(cancellationToken);

            var byDeptRaw = await q
                .SelectMany(s => s.Items)
                .GroupBy(i => string.IsNullOrWhiteSpace(i.DepartmentName) ? "(blank)" : i.DepartmentName!.Trim())
                .Select(g => new
                {
                    Key = g.Key,
                    Lines = g.Count(),
                    Pcs = g.Sum(i => i.Pcs),
                    Qty = g.Sum(i => i.Qty)
                })
                .OrderByDescending(x => x.Qty)
                .Take(20)
                .ToListAsync(cancellationToken);

            var topItemsRaw = await q
                .SelectMany(s => s.Items)
                .GroupBy(i => string.IsNullOrWhiteSpace(i.ItemName) ? "(blank)" : i.ItemName.Trim())
                .Select(g => new
                {
                    Key = g.Key,
                    Lines = g.Count(),
                    Pcs = g.Sum(i => i.Pcs),
                    Qty = g.Sum(i => i.Qty)
                })
                .OrderByDescending(x => x.Qty)
                .Take(15)
                .ToListAsync(cancellationToken);

            var qaQuery =
                from t in db.QaTests.AsNoTracking()
                join s in db.Sterilizations.AsNoTracking() on t.SterilizationId equals s.SterilizationId
                where (!fromUtc.HasValue || t.TestDateTime >= fromUtc.Value)
                      && (!toUtc.HasValue || t.TestDateTime <= toUtc.Value)
                      && (string.IsNullOrWhiteSpace(operatorName) ||
                          s.OperatorName == operatorName.Trim())
                select t;

            var qaGrouped = await qaQuery.GroupBy(t => new { t.TestType, t.Result })
                .Select(g => new { g.Key.TestType, g.Key.Result, Cnt = g.Count() })
                .ToListAsync(cancellationToken);

            var pendingQa = await qaQuery.CountAsync(t => t.ApprovedAt == null, cancellationToken);

            var doctorRoomRaw = await q
                .GroupBy(x => x.DoctorRoomId)
                .Select(g => new
                {
                    DoctorRoomId = g.Key,
                    Loads = g.Count(),
                    Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                    Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                })
                .OrderByDescending(x => x.Loads)
                .Take(25)
                .ToListAsync(cancellationToken);

            var doctorIds = doctorRoomRaw
                .Where(x => x.DoctorRoomId != null)
                .Select(x => x.DoctorRoomId!.Value)
                .Distinct()
                .ToList();
            var doctorLabelById = doctorIds.Count == 0
                ? new Dictionary<int, string>()
                : await db.DoctorRooms.AsNoTracking()
                    .Where(dr => doctorIds.Contains(dr.DoctorRoomId))
                    .ToDictionaryAsync(
                        dr => dr.DoctorRoomId,
                        dr => AnalyticsDoctorRoomLabel(dr.DoctorName, dr.Room),
                        cancellationToken);

            AnalyticsBiLogPaperSummaryDto? biPaperDto = null;
            try
            {
                biPaperDto = await BuildBiLogPaperAnalyticsAsync(q, cancellationToken);
            }
            catch (SqlException sqlPaper) when (IsMissingAnalyticsBiPaperColumns(sqlPaper))
            {
                biPaperDto = null;
            }

            AnalyticsInstrumentSummaryDto? instDto = null;
            try
            {
                var instQ = db.InstrumentChecks.AsNoTracking().AsQueryable();
                if (fromUtc.HasValue) instQ = instQ.Where(x => x.CheckedAtUtc >= fromUtc.Value);
                if (toUtc.HasValue) instQ = instQ.Where(x => x.CheckedAtUtc <= toUtc.Value);
                instDto = new AnalyticsInstrumentSummaryDto
                {
                    TotalChecks = await instQ.CountAsync(cancellationToken),
                    WitnessPending = await instQ.CountAsync(x => x.WitnessApprovedAt == null, cancellationToken),
                    WitnessApproved = await instQ.CountAsync(x => x.WitnessApprovedAt != null, cancellationToken)
                };
            }
            catch (SqlException sqlEx) when (sqlEx.Number == 208)
            {
                instDto = null;
            }

            AnalyticsPeriodCompareDto? compareDto = null;
            if (compareFromUtc.HasValue && compareToUtc.HasValue &&
                compareToUtc.Value >= compareFromUtc.Value)
            {
                var cq = ApplySterilizationAnalyticsFilter(baseSet, compareFromUtc, compareToUtc, operatorName);
                var cmp = await cq.GroupBy(_ => 1)
                    .Select(g => new
                    {
                        Total = g.Count(),
                        Draft = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Draft),
                        Completed = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Completed),
                        Voided = g.Count(x => x.CycleStatus == LoadRecordCycleStatuses.Voided),
                        Pcs = g.SelectMany(x => x.Items).Sum(i => (int?)i.Pcs) ?? 0,
                        Qty = g.SelectMany(x => x.Items).Sum(i => (int?)i.Qty) ?? 0
                    })
                    .FirstOrDefaultAsync(cancellationToken);
                compareDto = new AnalyticsPeriodCompareDto
                {
                    PeriodStartUtc = compareFromUtc.Value,
                    PeriodEndUtc = compareToUtc.Value,
                    TotalLoads = cmp?.Total ?? 0,
                    DraftLoads = cmp?.Draft ?? 0,
                    CompletedLoads = cmp?.Completed ?? 0,
                    VoidedLoads = cmp?.Voided ?? 0,
                    TotalPcs = cmp?.Pcs ?? 0,
                    TotalQty = cmp?.Qty ?? 0
                };
            }

            var dto = new SterilizationAnalyticsDto
            {
                TotalLoads = totals?.Total ?? 0,
                DraftLoads = totals?.Draft ?? 0,
                CompletedLoads = totals?.Completed ?? 0,
                VoidedLoads = totals?.Voided ?? 0,
                TotalPcs = totals?.Pcs ?? 0,
                TotalQty = totals?.Qty ?? 0,
                ComparePriorPeriod = compareDto,
                ByOperator = topOperatorsRaw.ConvertAll(x => new AnalyticsOperatorSummaryRowDto
                {
                    OperatorName = string.IsNullOrWhiteSpace(x.Operator) ? "(blank)" : x.Operator.Trim(),
                    Loads = x.Loads,
                    Pcs = x.Pcs,
                    Qty = x.Qty
                }),
                BySterilizer = topSterilizerRaw.ConvertAll(x => new AnalyticsSterilizerSummaryRowDto
                {
                    SterilizerId = x.SterilizerId,
                    SterilizerNo = sterNames.GetValueOrDefault(x.SterilizerId) ?? x.SterilizerId.ToString(CultureInfo.InvariantCulture),
                    Loads = x.Loads,
                    Pcs = x.Pcs,
                    Qty = x.Qty
                }),
                ByDay = byDay.ConvertAll(x => new AnalyticsDaySummaryRowDto
                {
                    DayUtc = DateTime.SpecifyKind(x.DayUtc, DateTimeKind.Utc),
                    Loads = x.Loads,
                    Pcs = x.Pcs,
                    Qty = x.Qty
                }),
                BySterilizationType = byTypeRaw.ConvertAll(x => new AnalyticsBreakdownRowDto
                {
                    Key = x.Key,
                    Loads = x.Loads,
                    Pcs = x.Pcs,
                    Qty = x.Qty
                }),
                ByBiResult = byBiRaw.ConvertAll(x => new AnalyticsBreakdownRowDto
                {
                    Key = x.Key,
                    Loads = x.Loads,
                    Pcs = x.Pcs,
                    Qty = x.Qty
                }),
                ByDepartment = byDeptRaw.ConvertAll(x => new AnalyticsBreakdownRowDto
                {
                    Key = x.Key,
                    Loads = x.Lines,
                    Pcs = x.Pcs,
                    Qty = x.Qty
                }),
                TopItemsByQty = topItemsRaw.ConvertAll(x => new AnalyticsBreakdownRowDto
                {
                    Key = x.Key,
                    Loads = x.Lines,
                    Pcs = x.Pcs,
                    Qty = x.Qty
                }),
                ByDoctorRoom = doctorRoomRaw.ConvertAll(x =>
                {
                    var key = x.DoctorRoomId is null
                        ? "(unassigned)"
                        : doctorLabelById.GetValueOrDefault(x.DoctorRoomId.Value)
                          ?? $"Doctor room #{x.DoctorRoomId.Value.ToString(CultureInfo.InvariantCulture)}";
                    return new AnalyticsBreakdownRowDto { Key = key, Loads = x.Loads, Pcs = x.Pcs, Qty = x.Qty };
                }),
                BiLogPaper = biPaperDto,
                QaTests = new AnalyticsQaSummaryDto
                {
                    LeakPass = qaGrouped.Where(x => string.Equals(x.TestType, "Leak", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Result, "Pass", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Cnt),
                    LeakFail = qaGrouped.Where(x => string.Equals(x.TestType, "Leak", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Result, "Fail", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Cnt),
                    BowiePass = qaGrouped.Where(x => string.Equals(x.TestType, "BowieDick", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Result, "Pass", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Cnt),
                    BowieFail = qaGrouped.Where(x => string.Equals(x.TestType, "BowieDick", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Result, "Fail", StringComparison.OrdinalIgnoreCase)).Sum(x => x.Cnt),
                    PendingApproval = pendingQa
                },
                InstrumentChecks = instDto
            };

            return (dto, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
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
            CreatedBy = Actor(),
            UpdatedBy = Actor(),
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

        if (DenyIfNotOwnerOrAdmin(entity.CreatedBy) is { } denied)
        {
            return (false, denied);
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
        entity.UpdatedBy = Actor();
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
                    SerialNumber = x.SerialNumber,
                    MaintenanceSchedule = x.MaintenanceSchedule,
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

            var q = SterilizationBiLogSheetFilter.WhereUsesBiologicalIndicator(db.Sterilizations.AsNoTracking());
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
                    CreatedByAccountId = x.CreatedBy,
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

    private static string AnalyticsDoctorRoomLabel(string doctorName, string? room)
    {
        var d = (doctorName ?? "").Trim();
        var r = (room ?? "").Trim();
        if (d.Length > 0 && r.Length > 0)
        {
            return $"{d} ({r})";
        }

        if (d.Length > 0)
        {
            return d;
        }

        if (r.Length > 0)
        {
            return r;
        }

        return "(blank)";
    }

    private static bool IsMissingAnalyticsBiPaperColumns(SqlException ex)
    {
        if (ex.Number != 207)
        {
            return false;
        }

        var m = ex.Message ?? "";
        return m.Contains("bi_", StringComparison.OrdinalIgnoreCase);
    }

    private static List<AnalyticsBreakdownRowDto> ToBiSignBuckets(
        IEnumerable<(string Key, int Loads)> rows) =>
        rows.OrderByDescending(x => x.Loads)
            .Select(x => new AnalyticsBreakdownRowDto { Key = x.Key, Loads = x.Loads, Pcs = 0, Qty = 0 })
            .ToList();

    private static List<AnalyticsBreakdownRowDto> TopNWithOthers(
        IReadOnlyList<AnalyticsBreakdownRowDto> rows,
        int topN,
        string othersLabel)
    {
        if (rows.Count <= topN || topN < 1)
        {
            return rows.ToList();
        }

        var top = rows.Take(topN).ToList();
        var rest = rows.Skip(topN).ToList();
        if (rest.Count == 0)
        {
            return top;
        }

        top.Add(new AnalyticsBreakdownRowDto
        {
            Key = othersLabel,
            Loads = rest.Sum(x => x.Loads),
            Pcs = rest.Sum(x => x.Pcs),
            Qty = rest.Sum(x => x.Qty)
        });
        return top;
    }

    private static async Task<AnalyticsBiLogPaperSummaryDto> BuildBiLogPaperAnalyticsAsync(
        IQueryable<Sterilization> q,
        CancellationToken cancellationToken)
    {
        q = SterilizationBiLogSheetFilter.WhereUsesBiologicalIndicator(q);
        var loadsInScope = await q.CountAsync(cancellationToken);
        var lotNoCaptured = await q.CountAsync(x => x.BiLotNo != null && x.BiLotNo != "", cancellationToken);
        var routineDaily = await q.CountAsync(x => x.BiDaily == true, cancellationToken);
        var tin = await q.CountAsync(x => x.BiTimeIn != null, cancellationToken);
        var tout = await q.CountAsync(x => x.BiTimeOut != null, cancellationToken);
        var both = await q.CountAsync(x => x.BiTimeIn != null && x.BiTimeOut != null, cancellationToken);
        var inc = await q.CountAsync(x => x.BiIncubatorChecked == true, cancellationToken);

        var p24 = await q
            .GroupBy(x =>
                x.BiProcessedResult24m == null || x.BiProcessedResult24m.Trim() == ""
                    ? "(blank)"
                    : x.BiProcessedResult24m.Trim() == "+"
                        ? "+"
                        : x.BiProcessedResult24m.Trim() == "-"
                            ? "-"
                            : "(other)")
            .Select(g => new { g.Key, Loads = g.Count() })
            .OrderByDescending(x => x.Loads)
            .ToListAsync(cancellationToken);

        var p24h = await q
            .GroupBy(x =>
                x.BiProcessedResult24h == null || x.BiProcessedResult24h.Trim() == ""
                    ? "(blank)"
                    : x.BiProcessedResult24h.Trim() == "+"
                        ? "+"
                        : x.BiProcessedResult24h.Trim() == "-"
                            ? "-"
                            : "(other)")
            .Select(g => new { g.Key, Loads = g.Count() })
            .OrderByDescending(x => x.Loads)
            .ToListAsync(cancellationToken);

        var c24 = await q
            .GroupBy(x =>
                x.BiControlResult24m == null || x.BiControlResult24m.Trim() == ""
                    ? "(blank)"
                    : x.BiControlResult24m.Trim() == "+"
                        ? "+"
                        : x.BiControlResult24m.Trim() == "-"
                            ? "-"
                            : "(other)")
            .Select(g => new { g.Key, Loads = g.Count() })
            .OrderByDescending(x => x.Loads)
            .ToListAsync(cancellationToken);

        var c24h = await q
            .GroupBy(x =>
                x.BiControlResult24h == null || x.BiControlResult24h.Trim() == ""
                    ? "(blank)"
                    : x.BiControlResult24h.Trim() == "+"
                        ? "+"
                        : x.BiControlResult24h.Trim() == "-"
                            ? "-"
                            : "(other)")
            .Select(g => new { g.Key, Loads = g.Count() })
            .OrderByDescending(x => x.Loads)
            .ToListAsync(cancellationToken);

        return new AnalyticsBiLogPaperSummaryDto
        {
            LoadsInScope = loadsInScope,
            LotNoCaptured = lotNoCaptured,
            RoutineDailyMarked = routineDaily,
            BiTimeInCaptured = tin,
            BiTimeOutCaptured = tout,
            BiTimesBothCaptured = both,
            IncubatorReadingChecked = inc,
            ProcessedSample24mSign = ToBiSignBuckets(p24.ConvertAll(x => (x.Key, x.Loads))),
            ProcessedSample24hSign = ToBiSignBuckets(p24h.ConvertAll(x => (x.Key, x.Loads))),
            ControlSample24mSign = ToBiSignBuckets(c24.ConvertAll(x => (x.Key, x.Loads))),
            ControlSample24hSign = ToBiSignBuckets(c24h.ConvertAll(x => (x.Key, x.Loads))),
        };
    }
}
