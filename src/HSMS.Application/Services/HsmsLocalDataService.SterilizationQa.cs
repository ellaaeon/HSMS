using System.Globalization;
using System.Security.Cryptography;
using System.IO;
using HSMS.Application.Audit;
using HSMS.Persistence.Data;
using HSMS.Persistence.Entities;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    private static readonly IReadOnlyDictionary<SterilizationQaCategory, string> QaCategoryToDb =
        new Dictionary<SterilizationQaCategory, string>
        {
            [SterilizationQaCategory.BowieDick] = "BowieDick",
            [SterilizationQaCategory.LeakTest] = "LeakTest",
            [SterilizationQaCategory.WarmUpTest] = "WarmUpTest",
            [SterilizationQaCategory.InstrumentTests] = "InstrumentTests",
            [SterilizationQaCategory.BiologicalIndicator] = "Bi",
            [SterilizationQaCategory.Ppm] = "PPM",
            [SterilizationQaCategory.MaintenanceCalibration] = "Maintenance",
            [SterilizationQaCategory.FailedIncident] = "Incident",
            [SterilizationQaCategory.Archived] = "Archived",
        };

    private static bool TryParseCategory(string raw, out SterilizationQaCategory cat)
    {
        cat = default;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        raw = raw.Trim();
        foreach (var kv in QaCategoryToDb)
        {
            if (string.Equals(kv.Value, raw, StringComparison.OrdinalIgnoreCase))
            {
                cat = kv.Key;
                return true;
            }
        }
        return false;
    }

    private static string CategoryToDbValue(SterilizationQaCategory cat) =>
        QaCategoryToDb.TryGetValue(cat, out var v) ? v : "Other";

    private static string StatusToDbValue(SterilizationQaWorkflowStatus s) => s.ToString();

    private static bool TryParseStatus(string raw, out SterilizationQaWorkflowStatus s) =>
        Enum.TryParse(raw, ignoreCase: true, out s);

    public async Task<(IReadOnlyList<SterilizationQaRecordListItemDto> items, string? error)> ListSterilizationQaRecordsAsync(
        SterilizationQaRecordQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var take = Math.Clamp(query.Take, 1, 1000);
        var skip = Math.Max(0, query.Skip);

        // New unified records
        var q = db.SterilizationQaRecords.AsNoTracking().AsQueryable();
        if (query.FromUtc.HasValue) q = q.Where(x => x.TestDateTimeUtc >= query.FromUtc.Value);
        if (query.ToUtc.HasValue) q = q.Where(x => x.TestDateTimeUtc <= query.ToUtc.Value);
        if (query.SterilizerId.HasValue) q = q.Where(x => x.SterilizerId == query.SterilizerId.Value);
        if (query.SterilizationId.HasValue) q = q.Where(x => x.SterilizationId == query.SterilizationId.Value);
        if (!string.IsNullOrWhiteSpace(query.Technician)) q = q.Where(x => x.Technician == query.Technician!.Trim());
        if (!string.IsNullOrWhiteSpace(query.Department)) q = q.Where(x => x.Department == query.Department!.Trim());
        if (query.ReviewerAccountId.HasValue) q = q.Where(x => x.ReviewedBy == query.ReviewerAccountId.Value || x.ApprovedBy == query.ReviewerAccountId.Value);
        if (query.Status.HasValue) q = q.Where(x => x.Status == StatusToDbValue(query.Status.Value));
        if (query.Category.HasValue && query.Category.Value != SterilizationQaCategory.Dashboard)
        {
            q = q.Where(x => x.Category == CategoryToDbValue(query.Category.Value));
        }
        if (query.FailedOnly) q = q.Where(x => x.Status == nameof(SterilizationQaWorkflowStatus.Failed));
        if (query.PendingOnly) q = q.Where(x => x.Status == nameof(SterilizationQaWorkflowStatus.PendingReview));
        if (query.ReviewQueue) q = q.Where(x => x.Status == nameof(SterilizationQaWorkflowStatus.PendingReview));

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            q = q.Where(x =>
                (x.Summary ?? "").Contains(s) ||
                (x.Notes ?? "").Contains(s) ||
                (x.Technician ?? "").Contains(s) ||
                (x.Department ?? "").Contains(s));
        }

        var rowQuery = (from r in q
                             join s in db.Sterilizations.AsNoTracking() on r.SterilizationId equals s.SterilizationId into rs
                             from s in rs.DefaultIfEmpty()
                             join u in db.SterilizerUnits.AsNoTracking() on r.SterilizerId equals u.SterilizerId into ru
                             from u in ru.DefaultIfEmpty()
                             select new
                             {
                                 r.RecordId,
                                 r.Category,
                                 r.SterilizationId,
                                 CycleNo = s != null ? s.CycleNo : null,
                                 r.SterilizerId,
                                 SterilizerNo = u != null ? u.SterilizerNumber : null,
                                 r.TestDateTimeUtc,
                                 r.Department,
                                 r.Technician,
                                 r.Status,
                                 r.ResultLabel,
                                 HasAttachments = db.SterilizationQaAttachments.Any(a => a.RecordId == r.RecordId),
                                 r.RowVersion
                             });

        // Review queue = oldest first (so supervisors clear oldest first); otherwise newest first.
        rowQuery = query.ReviewQueue
            ? rowQuery.OrderBy(x => x.TestDateTimeUtc)
            : rowQuery.OrderByDescending(x => x.TestDateTimeUtc);

        var newRows = await rowQuery
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var mapped = newRows.ConvertAll(x =>
        {
            var cat = TryParseCategory(x.Category, out var c) ? c : SterilizationQaCategory.InstrumentTests;
            var status = TryParseStatus(x.Status, out var st) ? st : SterilizationQaWorkflowStatus.Draft;
            return new SterilizationQaRecordListItemDto
            {
                RecordId = x.RecordId,
                Category = cat,
                SterilizationId = x.SterilizationId,
                CycleNo = x.CycleNo,
                SterilizerId = x.SterilizerId,
                SterilizerNo = x.SterilizerNo,
                TestDateTimeUtc = x.TestDateTimeUtc,
                Department = x.Department,
                Technician = x.Technician,
                Status = status,
                ResultLabel = x.ResultLabel,
                HasAttachments = x.HasAttachments,
                IsLegacyQaTest = false,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            };
        });

        // Legacy qa_tests (Leak/BowieDick) exposed into the same list for continuity.
        // Note: returned only when category is not set OR explicitly Leak/BowieDick.
        var includeLegacy = query.Category is null ||
                            query.Category == SterilizationQaCategory.LeakTest ||
                            query.Category == SterilizationQaCategory.BowieDick;
        if (!includeLegacy)
        {
            return (mapped, null);
        }

        var legacyQ = db.QaTests.AsNoTracking().AsQueryable();
        // Legacy qa_tests uses a non-UTC column (test_datetime). Treat it as local time for filtering.
        var legacyFromLocal = query.FromUtc?.ToLocalTime();
        var legacyToLocal = query.ToUtc?.ToLocalTime();
        if (legacyFromLocal.HasValue) legacyQ = legacyQ.Where(x => x.TestDateTime >= legacyFromLocal.Value);
        if (legacyToLocal.HasValue) legacyQ = legacyQ.Where(x => x.TestDateTime <= legacyToLocal.Value);
        if (query.SterilizationId.HasValue) legacyQ = legacyQ.Where(x => x.SterilizationId == query.SterilizationId.Value);
        if (query.Category == SterilizationQaCategory.LeakTest)
        {
            // Legacy data isn't consistent: "Leak", "Leak Test", etc.
            legacyQ = legacyQ.Where(x => x.TestType != null && EF.Functions.Like(x.TestType, "%Leak%"));
        }
        if (query.Category == SterilizationQaCategory.BowieDick)
        {
            // Legacy data isn't consistent: "BowieDick", "Bowie Dick", "Bowie-Dick", etc.
            legacyQ = legacyQ.Where(x => x.TestType != null && EF.Functions.Like(x.TestType, "%Bowie%"));
        }

        // Apply status filter for legacy rows (legacy only has pending vs approved semantics + result).
        if (query.Status.HasValue)
        {
            switch (query.Status.Value)
            {
                case SterilizationQaWorkflowStatus.PendingReview:
                    legacyQ = legacyQ.Where(x => x.ApprovedAt == null);
                    break;
                case SterilizationQaWorkflowStatus.Approved:
                    legacyQ = legacyQ.Where(x => x.ApprovedAt != null);
                    break;
                case SterilizationQaWorkflowStatus.Failed:
                    legacyQ = legacyQ.Where(x => x.Result == "Fail");
                    break;
                default:
                    // Draft/RetestRequired/Archived don't exist in legacy qa_tests; treat as no matches.
                    legacyQ = legacyQ.Where(_ => false);
                    break;
            }
        }
        if (query.PendingOnly || query.ReviewQueue) legacyQ = legacyQ.Where(x => x.ApprovedAt == null);
        if (query.ReviewerAccountId.HasValue) legacyQ = legacyQ.Where(x => x.ApprovedBy == query.ReviewerAccountId.Value);
        if (query.FailedOnly) legacyQ = legacyQ.Where(x => x.Result == "Fail");
        if (!string.IsNullOrWhiteSpace(query.Technician)) legacyQ = legacyQ.Where(x => x.PerformedBy == query.Technician!.Trim());
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim();
            legacyQ = legacyQ.Where(x =>
                (x.Notes ?? "").Contains(s) ||
                (x.PerformedBy ?? "").Contains(s) ||
                (x.TestType ?? "").Contains(s));
        }

        var legacyRowsQ = (from t in legacyQ
                                join s in db.Sterilizations.AsNoTracking() on t.SterilizationId equals s.SterilizationId
                                join u in db.SterilizerUnits.AsNoTracking() on s.SterilizerId equals u.SterilizerId into su
                                from u in su.DefaultIfEmpty()
                                select new
                                {
                                    t.QaTestId,
                                    t.SterilizationId,
                                    s.CycleNo,
                                    s.SterilizerId,
                                    SterilizerNo = u != null ? u.SterilizerNumber : null,
                                    t.TestType,
                                    t.TestDateTime,
                                    t.Result,
                                    t.PerformedBy,
                                    t.ApprovedAt,
                                    t.RowVersion
                                });

        legacyRowsQ = query.ReviewQueue
            ? legacyRowsQ.OrderBy(x => x.TestDateTime)
            : legacyRowsQ.OrderByDescending(x => x.TestDateTime);

        var legacyRows = await legacyRowsQ
            .Take(take)
            .ToListAsync(cancellationToken);

        foreach (var l in legacyRows)
        {
            mapped.Add(new SterilizationQaRecordListItemDto
            {
                // Make legacy IDs globally unique in UI by offsetting into negative space.
                RecordId = -l.QaTestId,
                Category = string.Equals(l.TestType, "BowieDick", StringComparison.OrdinalIgnoreCase)
                    ? SterilizationQaCategory.BowieDick
                    : SterilizationQaCategory.LeakTest,
                SterilizationId = l.SterilizationId,
                CycleNo = l.CycleNo,
                SterilizerId = l.SterilizerId,
                SterilizerNo = l.SterilizerNo,
                TestDateTimeUtc = l.TestDateTime,
                Department = null,
                Technician = l.PerformedBy,
                Status = l.ApprovedAt is null ? SterilizationQaWorkflowStatus.PendingReview : SterilizationQaWorkflowStatus.Approved,
                ResultLabel = l.Result,
                HasAttachments = false,
                IsLegacyQaTest = true,
                RowVersion = Convert.ToBase64String(l.RowVersion)
            });
        }

        // Stable ordering after merge.
        mapped.Sort(query.ReviewQueue
            ? (a, b) => a.TestDateTimeUtc.CompareTo(b.TestDateTimeUtc)
            : (a, b) => b.TestDateTimeUtc.CompareTo(a.TestDateTimeUtc));
        return (mapped, null);
    }

    public async Task<(long recordId, string? rowVersion, string? error)> CreateSterilizationQaRecordAsync(
        SterilizationQaRecordCreateDto payload,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var categoryDb = CategoryToDbValue(payload.Category);
        if (categoryDb == "Other")
        {
            return (0, null, "Invalid category.");
        }

        if (payload.SterilizationId.HasValue)
        {
            var ok = await db.Sterilizations.AnyAsync(x => x.SterilizationId == payload.SterilizationId.Value, cancellationToken);
            if (!ok) return (0, null, "Sterilization cycle not found.");
        }
        if (payload.SterilizerId.HasValue)
        {
            var ok = await db.SterilizerUnits.AnyAsync(x => x.SterilizerId == payload.SterilizerId.Value, cancellationToken);
            if (!ok) return (0, null, "Sterilizer not found.");
        }

        var entity = new SterilizationQaRecord
        {
            Category = categoryDb,
            SterilizationId = payload.SterilizationId,
            SterilizerId = payload.SterilizerId,
            TestDateTimeUtc = payload.TestDateTimeUtc,
            Department = string.IsNullOrWhiteSpace(payload.Department) ? null : payload.Department.Trim(),
            Technician = string.IsNullOrWhiteSpace(payload.Technician) ? null : payload.Technician.Trim(),
            ResultLabel = string.IsNullOrWhiteSpace(payload.ResultLabel) ? null : payload.ResultLabel.Trim(),
            Summary = string.IsNullOrWhiteSpace(payload.Summary) ? null : payload.Summary.Trim(),
            Notes = string.IsNullOrWhiteSpace(payload.Notes) ? null : payload.Notes.Trim(),
            Status = nameof(SterilizationQaWorkflowStatus.Draft),
            CreatedBy = Actor(),
            CreatedAtUtc = DateTime.UtcNow
        };

        db.SterilizationQaRecords.Add(entity);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);

            db.SterilizationQaStatusEvents.Add(new SterilizationQaStatusEvent
            {
                RecordId = entity.RecordId,
                EventAtUtc = DateTime.UtcNow,
                ActorAccountId = Actor(),
                FromStatus = "",
                ToStatus = nameof(SterilizationQaWorkflowStatus.Draft),
                Comment = null
            });

            await auditService.AppendAsync(
                db,
                module: "SterilizationQA",
                entityName: "qa_test_records",
                entityId: entity.RecordId.ToString(CultureInfo.InvariantCulture),
                action: AuditActions.SterilizationQaCreate,
                actorAccountId: Actor(),
                clientMachine: payload.ClientMachine,
                oldValues: null,
                newValues: new { entity.Category, entity.Status, entity.SterilizationId, entity.SterilizerId, entity.ResultLabel },
                correlationId: Guid.NewGuid(),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return (entity.RecordId, Convert.ToBase64String(entity.RowVersion), null);
    }

    public async Task<(string? rowVersion, string? error)> PatchSterilizationQaStatusAsync(
        long recordId,
        SterilizationQaRecordStatusPatchDto payload,
        CancellationToken cancellationToken = default)
    {
        if (recordId <= 0)
        {
            return (null, "This is a legacy QA test. Use the legacy Approve flow for it.");
        }
        if (string.IsNullOrWhiteSpace(payload.RowVersion))
        {
            return (null, "rowVersion is required.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.SterilizationQaRecords.SingleOrDefaultAsync(x => x.RecordId == recordId, cancellationToken);
        if (entity is null) return (null, "Record not found.");
        if (!entity.RowVersion.SequenceEqual(Convert.FromBase64String(payload.RowVersion)))
        {
            return (null, "Concurrency conflict. Refresh and try again.");
        }

        var actor = Actor();
        if (actor is null) return (null, "Sign in required.");

        var from = entity.Status;
        var to = StatusToDbValue(payload.NewStatus);

        // Authorization + transition rules:
        // - Staff can create and submit for review, and mark "RetestRequired" for their own follow-up.
        // - Only Admin can approve/archive/final-fail (hospital QA gatekeeper).
        if (Application.Security.RoleAuthorization.RequireAuthenticated(User()) is { } deniedAuth)
        {
            return (null, deniedAuth.Message);
        }

        if (!TryParseStatus(from, out var fromStatus))
        {
            fromStatus = SterilizationQaWorkflowStatus.Draft;
        }

        var needsAdmin =
            payload.NewStatus is SterilizationQaWorkflowStatus.Approved
                or SterilizationQaWorkflowStatus.Archived
                or SterilizationQaWorkflowStatus.Failed;
        if (needsAdmin && Application.Security.RoleAuthorization.RequireAdmin(User()) is { } deniedAdmin)
        {
            return (null, deniedAdmin.Message);
        }

        if (!IsAllowedTransition(fromStatus, payload.NewStatus))
        {
            return (null, $"Invalid status transition: {fromStatus} → {payload.NewStatus}.");
        }

        // Automatic transition hints (hospital workflow):
        // - Approve sets ApprovedBy/ApprovedAt
        // - PendingReview sets ReviewedBy/ReviewedAt (as "submitted to review")
        // - Archive sets ArchivedAt
        if (payload.NewStatus == SterilizationQaWorkflowStatus.PendingReview)
        {
            entity.ReviewedBy = actor;
            entity.ReviewedAtUtc = DateTime.UtcNow;
        }
        if (payload.NewStatus == SterilizationQaWorkflowStatus.Approved)
        {
            entity.ApprovedBy = actor;
            entity.ApprovedAtUtc = DateTime.UtcNow;
        }
        if (payload.NewStatus == SterilizationQaWorkflowStatus.Archived)
        {
            entity.ArchivedAtUtc = DateTime.UtcNow;
        }

        entity.Status = to;
        entity.UpdatedBy = actor;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);

            db.SterilizationQaStatusEvents.Add(new SterilizationQaStatusEvent
            {
                RecordId = entity.RecordId,
                EventAtUtc = DateTime.UtcNow,
                ActorAccountId = actor,
                FromStatus = from,
                ToStatus = to,
                Comment = string.IsNullOrWhiteSpace(payload.Comment) ? null : payload.Comment.Trim()
            });

            await auditService.AppendAsync(
                db,
                module: "SterilizationQA",
                entityName: "qa_test_records",
                entityId: entity.RecordId.ToString(CultureInfo.InvariantCulture),
                action: AuditActions.SterilizationQaStatusUpdate,
                actorAccountId: actor,
                clientMachine: payload.ClientMachine,
                oldValues: new { Status = from },
                newValues: new { Status = to, payload.Comment },
                correlationId: Guid.NewGuid(),
                cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return (Convert.ToBase64String(entity.RowVersion), null);
    }

    private static bool IsAllowedTransition(SterilizationQaWorkflowStatus from, SterilizationQaWorkflowStatus to)
    {
        if (from == to) return true;
        return from switch
        {
            SterilizationQaWorkflowStatus.Draft =>
                to is SterilizationQaWorkflowStatus.PendingReview
                    or SterilizationQaWorkflowStatus.Archived,

            SterilizationQaWorkflowStatus.PendingReview =>
                to is SterilizationQaWorkflowStatus.Approved
                    or SterilizationQaWorkflowStatus.Failed
                    or SterilizationQaWorkflowStatus.RetestRequired
                    or SterilizationQaWorkflowStatus.Draft,

            SterilizationQaWorkflowStatus.Approved =>
                to is SterilizationQaWorkflowStatus.Archived,

            SterilizationQaWorkflowStatus.Failed =>
                to is SterilizationQaWorkflowStatus.RetestRequired
                    or SterilizationQaWorkflowStatus.Archived,

            SterilizationQaWorkflowStatus.RetestRequired =>
                to is SterilizationQaWorkflowStatus.PendingReview
                    or SterilizationQaWorkflowStatus.Archived,

            SterilizationQaWorkflowStatus.Archived =>
                false,

            _ => false
        };
    }

    public async Task<(SterilizationQaDashboardDto? dashboard, string? error)> GetSterilizationQaDashboardAsync(
        SterilizationQaDashboardQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var fromUtc = query.FromUtc;
        var toUtc = query.ToUtc;

        var q = db.SterilizationQaRecords.AsNoTracking().AsQueryable();
        q = q.Where(x => x.TestDateTimeUtc >= fromUtc && x.TestDateTimeUtc <= toUtc);
        if (query.SterilizerId.HasValue) q = q.Where(x => x.SterilizerId == query.SterilizerId.Value);
        if (!string.IsNullOrWhiteSpace(query.Department)) q = q.Where(x => x.Department == query.Department!.Trim());

        var totals = await q.GroupBy(_ => 1).Select(g => new
            {
                Total = g.Count(),
                Approved = g.Count(x => x.Status == nameof(SterilizationQaWorkflowStatus.Approved)),
                Failed = g.Count(x => x.Status == nameof(SterilizationQaWorkflowStatus.Failed)),
                Pending = g.Count(x => x.Status == nameof(SterilizationQaWorkflowStatus.PendingReview)),
                Archived = g.Count(x => x.Status == nameof(SterilizationQaWorkflowStatus.Archived))
            })
            .FirstOrDefaultAsync(cancellationToken);

        var byDay = await q
            .GroupBy(x => x.TestDateTimeUtc.Date)
            .Select(g => new
            {
                DayUtc = g.Key,
                Total = g.Count(),
                Approved = g.Count(x => x.Status == nameof(SterilizationQaWorkflowStatus.Approved)),
                Failed = g.Count(x => x.Status == nameof(SterilizationQaWorkflowStatus.Failed)),
                Pending = g.Count(x => x.Status == nameof(SterilizationQaWorkflowStatus.PendingReview))
            })
            .OrderBy(x => x.DayUtc)
            .Take(400)
            .ToListAsync(cancellationToken);

        var byCategory = await q
            .GroupBy(x => x.Category)
            .Select(g => new { Key = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .Take(20)
            .ToListAsync(cancellationToken);

        var bySterilizer = await q
            .GroupBy(x => x.SterilizerId)
            .Select(g => new { SterilizerId = g.Key, Value = g.Count() })
            .OrderByDescending(x => x.Value)
            .Take(15)
            .ToListAsync(cancellationToken);

        var sterIds = bySterilizer.Where(x => x.SterilizerId != null).Select(x => x.SterilizerId!.Value).Distinct().ToList();
        var sterNames = sterIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.SterilizerUnits.AsNoTracking()
                .Where(u => sterIds.Contains(u.SterilizerId))
                .ToDictionaryAsync(u => u.SterilizerId, u => u.SterilizerNumber, cancellationToken);

        var lastFailure = await q
            .Where(x => x.Status == nameof(SterilizationQaWorkflowStatus.Failed))
            .OrderByDescending(x => x.TestDateTimeUtc)
            .Select(x => new { x.SterilizerId, x.TestDateTimeUtc })
            .FirstOrDefaultAsync(cancellationToken);

        // Legacy QA (Leak/BowieDick) summary in same date range
        var legacyQ = db.QaTests.AsNoTracking().AsQueryable();
        legacyQ = legacyQ.Where(x => x.TestDateTime >= fromUtc && x.TestDateTime <= toUtc);
        if (query.SterilizerId.HasValue)
        {
            var sid = query.SterilizerId.Value;
            legacyQ = from t in legacyQ
                      join s in db.Sterilizations.AsNoTracking() on t.SterilizationId equals s.SterilizationId
                      where s.SterilizerId == sid
                      select t;
        }

        var legacyTotals = await legacyQ.GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Pending = g.Count(x => x.ApprovedAt == null),
                BiFailures = g.Count(x => x.TestType == "BowieDick" && x.Result == "Fail") + g.Count(x => x.TestType == "Leak" && x.Result == "Fail")
            })
            .FirstOrDefaultAsync(cancellationToken);

        var dto = new SterilizationQaDashboardDto
        {
            Total = totals?.Total ?? 0,
            Approved = totals?.Approved ?? 0,
            Failed = totals?.Failed ?? 0,
            PendingReview = totals?.Pending ?? 0,
            Archived = totals?.Archived ?? 0,
            LegacyQaTotal = legacyTotals?.Total ?? 0,
            LegacyPendingApproval = legacyTotals?.Pending ?? 0,
            BiFailures = legacyTotals?.BiFailures ?? 0,
            LastFailedSterilizerId = lastFailure?.SterilizerId,
            LastFailedSterilizerNo = lastFailure?.SterilizerId is int id ? sterNames.GetValueOrDefault(id) : null,
            LastFailureAtUtc = lastFailure?.TestDateTimeUtc,
            ByDay = byDay.ConvertAll(x => new SterilizationQaTrendPointDto
            {
                DayUtc = DateTime.SpecifyKind(x.DayUtc, DateTimeKind.Utc),
                Total = x.Total,
                Approved = x.Approved,
                Failed = x.Failed,
                PendingReview = x.Pending
            }),
            ByCategory = byCategory.ConvertAll(x => new SterilizationQaBreakdownPointDto { Key = x.Key, Value = x.Value }),
            BySterilizer = bySterilizer.ConvertAll(x =>
            {
                var label = x.SterilizerId is null ? "(unassigned)" : sterNames.GetValueOrDefault(x.SterilizerId.Value) ?? $"Sterilizer #{x.SterilizerId.Value}";
                return new SterilizationQaBreakdownPointDto { Key = label, Value = x.Value };
            }),
            // Placeholders for next slices (PPM + maintenance due engine):
            OverduePpm = 0,
            UpcomingMaintenance = 0
        };

        // Compliance / smart alerts (phase 1 - no extra schema needed)
        dto.Alerts = await BuildQaAlertsAsync(db, fromUtc, toUtc, query.SterilizerId, cancellationToken);

        return (dto, null);
    }

    private async Task<List<SterilizationQaAlertDto>> BuildQaAlertsAsync(
        HsmsDbContext db,
        DateTime fromUtc,
        DateTime toUtc,
        int? sterilizerId,
        CancellationToken cancellationToken)
    {
        var alerts = new List<SterilizationQaAlertDto>();

        // 1) Pending review aging
        var pending = db.SterilizationQaRecords.AsNoTracking()
            .Where(x => x.TestDateTimeUtc >= fromUtc && x.TestDateTimeUtc <= toUtc)
            .Where(x => x.Status == nameof(SterilizationQaWorkflowStatus.PendingReview));
        if (sterilizerId.HasValue) pending = pending.Where(x => x.SterilizerId == sterilizerId.Value);

        var oldestPending = await pending.OrderBy(x => x.TestDateTimeUtc)
            .Select(x => new { x.RecordId, x.TestDateTimeUtc, x.SterilizerId })
            .FirstOrDefaultAsync(cancellationToken);
        var pendingCount = await pending.CountAsync(cancellationToken);
        if (pendingCount > 0 && oldestPending is not null)
        {
            var ageHours = (DateTime.UtcNow - oldestPending.TestDateTimeUtc).TotalHours;
            var sev = ageHours >= 48 ? SterilizationQaAlertSeverity.Critical : SterilizationQaAlertSeverity.Warning;
            alerts.Add(new SterilizationQaAlertDto
            {
                Severity = sev,
                Code = "PENDING_REVIEW_AGING",
                Title = $"{pendingCount} QA record(s) pending review",
                Detail = $"Oldest pending is {Math.Floor(ageHours)}h old.",
                SterilizerId = oldestPending.SterilizerId,
                EventAtUtc = oldestPending.TestDateTimeUtc
            });
        }

        // 2) Consecutive failures per sterilizer (last 7 days inside toUtc window)
        var failFrom = toUtc.AddDays(-7);
        var fails = db.SterilizationQaRecords.AsNoTracking()
            .Where(x => x.TestDateTimeUtc >= failFrom && x.TestDateTimeUtc <= toUtc)
            .Where(x => x.Status == nameof(SterilizationQaWorkflowStatus.Failed));
        if (sterilizerId.HasValue) fails = fails.Where(x => x.SterilizerId == sterilizerId.Value);

        var failBySter = await fails
            .GroupBy(x => x.SterilizerId)
            .Select(g => new { SterilizerId = g.Key, FailCount = g.Count(), LastFail = g.Max(x => x.TestDateTimeUtc) })
            .OrderByDescending(x => x.FailCount)
            .Take(5)
            .ToListAsync(cancellationToken);

        if (failBySter.Count > 0)
        {
            var sterIds = failBySter.Where(x => x.SterilizerId != null).Select(x => x.SterilizerId!.Value).Distinct().ToList();
            var sterNames = sterIds.Count == 0
                ? new Dictionary<int, string>()
                : await db.SterilizerUnits.AsNoTracking()
                    .Where(u => sterIds.Contains(u.SterilizerId))
                    .ToDictionaryAsync(u => u.SterilizerId, u => u.SterilizerNumber, cancellationToken);

            foreach (var row in failBySter)
            {
                if (row.SterilizerId is null) continue;
                if (row.FailCount < 2) continue;
                var sterNo = sterNames.GetValueOrDefault(row.SterilizerId.Value) ?? $"Sterilizer #{row.SterilizerId.Value}";
                alerts.Add(new SterilizationQaAlertDto
                {
                    Severity = row.FailCount >= 3 ? SterilizationQaAlertSeverity.Critical : SterilizationQaAlertSeverity.Warning,
                    Code = "CONSECUTIVE_FAILURES",
                    Title = $"{sterNo}: repeated failures",
                    Detail = $"{row.FailCount} failure(s) in the last 7 days.",
                    SterilizerId = row.SterilizerId.Value,
                    SterilizerNo = sterNo,
                    EventAtUtc = row.LastFail
                });
            }
        }

        // 3) Missed daily Bowie Dick (using legacy qa_tests for now)
        // Hospital rule of thumb: each active sterilizer should have a BowieDick daily (steam).
        // We compute missing days in the selected range per sterilizer.
        var sterQuery = db.SterilizerUnits.AsNoTracking().Where(x => x.IsActive);
        if (sterilizerId.HasValue) sterQuery = sterQuery.Where(x => x.SterilizerId == sterilizerId.Value);
        var activeSter = await sterQuery.Select(x => new { x.SterilizerId, x.SterilizerNumber }).ToListAsync(cancellationToken);

        var bowieDays = await (from t in db.QaTests.AsNoTracking()
                               join s in db.Sterilizations.AsNoTracking() on t.SterilizationId equals s.SterilizationId
                               where t.TestType == "BowieDick"
                                     && t.TestDateTime >= fromUtc && t.TestDateTime <= toUtc
                               select new { s.SterilizerId, Day = t.TestDateTime.Date })
            .Distinct()
            .ToListAsync(cancellationToken);

        var bowieSet = bowieDays
            .Select(x => (x.SterilizerId, x.Day))
            .ToHashSet();
        var daysInRange = (int)Math.Ceiling((toUtc.Date - fromUtc.Date).TotalDays);
        daysInRange = Math.Clamp(daysInRange, 1, 120);

        foreach (var st in activeSter)
        {
            var missing = 0;
            for (var i = 0; i < daysInRange; i++)
            {
                var day = fromUtc.Date.AddDays(i);
                if (!bowieSet.Contains((st.SterilizerId, day)))
                {
                    missing++;
                }
            }

            if (missing > 0)
            {
                alerts.Add(new SterilizationQaAlertDto
                {
                    Severity = missing >= 3 ? SterilizationQaAlertSeverity.Critical : SterilizationQaAlertSeverity.Warning,
                    Code = "MISSING_DAILY_BOWIE_DICK",
                    Title = $"{st.SterilizerNumber}: missing daily Bowie-Dick",
                    Detail = $"{missing} day(s) missing in selected range.",
                    SterilizerId = st.SterilizerId,
                    SterilizerNo = st.SterilizerNumber
                });
            }
        }

        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenByDescending(a => a.EventAtUtc)
            .Take(12)
            .ToList();
    }

    public async Task<(SterilizationQaTimelineDto? timeline, string? error)> GetSterilizationQaTimelineAsync(
        long recordId,
        CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        if (recordId <= 0)
        {
            var qaId = (int)Math.Abs(recordId);
            var row = await db.QaTests.AsNoTracking().SingleOrDefaultAsync(x => x.QaTestId == qaId, cancellationToken);
            if (row is null) return (null, "Legacy QA test not found.");

            var events = new List<SterilizationQaTimelineEventDto>
            {
                new()
                {
                    EventAtUtc = row.TestDateTime,
                    Title = "QA test recorded",
                    Detail = $"{row.TestType} • Result {row.Result}",
                    Status = SterilizationQaWorkflowStatus.PendingReview
                }
            };

            if (row.ApprovedAt is not null)
            {
                events.Add(new SterilizationQaTimelineEventDto
                {
                    EventAtUtc = row.ApprovedAt.Value,
                    Title = "Approved",
                    Detail = string.IsNullOrWhiteSpace(row.ApprovedRemarks) ? null : row.ApprovedRemarks,
                    Status = SterilizationQaWorkflowStatus.Approved
                });
            }

            events.Sort((a, b) => b.EventAtUtc.CompareTo(a.EventAtUtc));
            return (new SterilizationQaTimelineDto { RecordId = recordId, IsLegacy = true, Events = events }, null);
        }

        var exists = await db.SterilizationQaRecords.AsNoTracking().AnyAsync(x => x.RecordId == recordId, cancellationToken);
        if (!exists) return (null, "Record not found.");

        var raw = await (from e in db.SterilizationQaStatusEvents.AsNoTracking()
                         where e.RecordId == recordId
                         orderby e.EventAtUtc descending
                         select new { e.EventAtUtc, e.FromStatus, e.ToStatus, e.Comment })
            .Take(200)
            .ToListAsync(cancellationToken);

        var evts = raw.ConvertAll(e =>
        {
            SterilizationQaWorkflowStatus? parsed = null;
            if (Enum.TryParse<SterilizationQaWorkflowStatus>(e.ToStatus, ignoreCase: true, out var st))
            {
                parsed = st;
            }
            return new SterilizationQaTimelineEventDto
            {
                EventAtUtc = e.EventAtUtc,
                Title = string.IsNullOrWhiteSpace(e.FromStatus)
                    ? $"Created: {e.ToStatus}"
                    : $"Status: {e.FromStatus} → {e.ToStatus}",
                Detail = e.Comment,
                Status = parsed,
                ActorUsername = null
            };
        });

        return (new SterilizationQaTimelineDto { RecordId = recordId, IsLegacy = false, Events = evts }, null);
    }

    public async Task<(IReadOnlyList<SterilizationQaAttachmentListItemDto> items, string? error)> ListSterilizationQaAttachmentsAsync(
        long recordId,
        CancellationToken cancellationToken = default)
    {
        if (recordId <= 0)
        {
            return ([], null);
        }

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.SterilizationQaAttachments.AsNoTracking()
            .Where(x => x.RecordId == recordId)
            .OrderByDescending(x => x.CapturedAtUtc)
            .Take(200)
            .Select(x => new SterilizationQaAttachmentListItemDto
            {
                AttachmentId = x.AttachmentId,
                RecordId = x.RecordId,
                FileName = x.FileName,
                ContentType = x.ContentType,
                FileSizeBytes = x.FileSizeBytes,
                CapturedAtUtc = x.CapturedAtUtc,
                FilePath = x.FilePath,
                RowVersion = Convert.ToBase64String(x.RowVersion)
            })
            .ToListAsync(cancellationToken);

        return (rows, null);
    }

    public async Task<(long attachmentId, string? rowVersion, string? error)> AddSterilizationQaAttachmentAsync(
        long recordId,
        SterilizationQaAttachmentAddDto payload,
        CancellationToken cancellationToken = default)
    {
        if (recordId <= 0) return (0, null, "Attachments are not supported for legacy QA tests.");
        if (string.IsNullOrWhiteSpace(payload.SourceFilePath)) return (0, null, "SourceFilePath is required.");
        var src = payload.SourceFilePath.Trim();
        if (!File.Exists(src)) return (0, null, "Selected file does not exist.");

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var rec = await db.SterilizationQaRecords.SingleOrDefaultAsync(x => x.RecordId == recordId, cancellationToken);
        if (rec is null) return (0, null, "Record not found.");

        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HSMS", "qa-evidence", recordId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(baseDir);

        var ext = Path.GetExtension(src);
        var safeExt = string.IsNullOrWhiteSpace(ext) ? "" : ext;
        var fileName = Path.GetFileName(src);
        var storedName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{safeExt}";
        var dest = Path.Combine(baseDir, storedName);

        File.Copy(src, dest, overwrite: false);

        var fi = new FileInfo(dest);
        var sha256 = ComputeSha256Hex(dest);

        var contentType = GuessContentType(fileName);

        var entity = new SterilizationQaAttachment
        {
            RecordId = recordId,
            FilePath = dest,
            FileName = fileName,
            ContentType = contentType,
            FileSizeBytes = fi.Length,
            Sha256 = sha256,
            CapturedAtUtc = DateTime.UtcNow,
            CapturedBy = Actor()
        };
        db.SterilizationQaAttachments.Add(entity);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await auditService.AppendAsync(
                db,
                module: "SterilizationQA",
                entityName: "qa_test_attachments",
                entityId: entity.AttachmentId.ToString(CultureInfo.InvariantCulture),
                action: AuditActions.SterilizationQaAttachmentAdd,
                actorAccountId: Actor(),
                clientMachine: payload.ClientMachine,
                oldValues: null,
                newValues: new { recordId, entity.FileName, entity.ContentType, entity.FileSizeBytes, entity.Sha256 },
                correlationId: Guid.NewGuid(),
                cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return (entity.AttachmentId, Convert.ToBase64String(entity.RowVersion), null);
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
    }

    private static string GuessContentType(string fileName)
    {
        var ext = (Path.GetExtension(fileName) ?? "").ToLowerInvariant();
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".csv" => "text/csv",
            _ => "application/octet-stream"
        };
    }
}

