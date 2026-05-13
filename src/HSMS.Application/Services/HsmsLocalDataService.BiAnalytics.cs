using System.Globalization;
using HSMS.Application.Analytics;
using HSMS.Persistence.Data;
using HSMS.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace HSMS.Application.Services;

public sealed partial class HsmsLocalDataService
{
    public async Task<(BiAnalyticsDto? analytics, string? error)> GetBiAnalyticsAsync(
        AnalyticsFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        if (Application.Security.AnalyticsAuthorization.RequireViewBi(User()) is { } denied)
        {
            return (null, denied.Message);
        }

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var q = db.Sterilizations.AsNoTracking().AsQueryable();

            // Reuse the same structured filter surface as analytics.
            q = SterilizationAnalyticsQueryBuilder.Apply(q, filter ?? new AnalyticsFilterDto());

            // BI-relevant projection (single scan).
            var rows = await q
                .Select(x => new
                {
                    DayUtc = x.CreatedAt.Date,
                    x.SterilizerId,
                    x.BiLotNo,
                    x.BiResult,
                    x.BiTimeIn,
                    x.BiTimeOut,
                    x.Implants
                })
                .ToListAsync(cancellationToken);

            static bool HasText(string? s) => !string.IsNullOrWhiteSpace(s);
            static string Norm(string? s) => string.IsNullOrWhiteSpace(s) ? "" : s.Trim();

            // Missing BI detection (first-pass rule):
            // - consider cycles in scope as BI cycles (we'll later refine by sterilization type/cycle program rules)
            // - treat as "missing" if implants==true AND (lot/result missing)
            var total = rows.Count;
            var passed = 0;
            var failed = 0;
            var pending = 0;
            var missing = 0;

            foreach (var r in rows)
            {
                var bi = Norm(r.BiResult);
                if (string.Equals(bi, "Pass", StringComparison.OrdinalIgnoreCase)) passed++;
                else if (string.Equals(bi, "Fail", StringComparison.OrdinalIgnoreCase)) failed++;
                else if (string.Equals(bi, "Pending", StringComparison.OrdinalIgnoreCase)) pending++;

                if (r.Implants && (!HasText(r.BiLotNo) || !HasText(r.BiResult)))
                {
                    missing++;
                }
            }

            var byDay = rows
                .GroupBy(x => x.DayUtc)
                .OrderBy(g => g.Key)
                .Select(g =>
                {
                    var t = 0;
                    var p = 0;
                    var f = 0;
                    var pe = 0;
                    var m = 0;
                    foreach (var r in g)
                    {
                        t++;
                        var bi = Norm(r.BiResult);
                        if (string.Equals(bi, "Pass", StringComparison.OrdinalIgnoreCase)) p++;
                        else if (string.Equals(bi, "Fail", StringComparison.OrdinalIgnoreCase)) f++;
                        else if (string.Equals(bi, "Pending", StringComparison.OrdinalIgnoreCase)) pe++;
                        if (r.Implants && (!HasText(r.BiLotNo) || !HasText(r.BiResult))) m++;
                    }
                    return new BiAnalyticsDayTrendRowDto
                    {
                        DayUtc = DateTime.SpecifyKind(g.Key, DateTimeKind.Utc),
                        Total = t,
                        Pass = p,
                        Fail = f,
                        Pending = pe,
                        Missing = m
                    };
                })
                .ToList();

            var sterIds = rows.Select(x => x.SterilizerId).Distinct().ToList();
            var sterLabels = sterIds.Count == 0
                ? new Dictionary<int, string>()
                : await db.SterilizerUnits.AsNoTracking()
                    .Where(s => sterIds.Contains(s.SterilizerId))
                    .ToDictionaryAsync(s => s.SterilizerId, s => s.SterilizerNumber, cancellationToken);

            static List<BiAnalyticsBreakdownRowDto> Breakdown<TKey, TRow>(
                IEnumerable<IGrouping<TKey, TRow>> groups,
                Func<TKey, (int? id, string key)> label,
                Func<TRow, string?> biResult,
                Func<TRow, string?> biLotNo,
                Func<TRow, bool> implants)
            {
                return groups.Select(g =>
                {
                    var (id, key) = label(g.Key);
                    var t = 0;
                    var p = 0;
                    var f = 0;
                    var pe = 0;
                    var m = 0;
                    foreach (var r in g)
                    {
                        t++;
                        var bi = Norm(biResult(r));
                        if (string.Equals(bi, "Pass", StringComparison.OrdinalIgnoreCase)) p++;
                        else if (string.Equals(bi, "Fail", StringComparison.OrdinalIgnoreCase)) f++;
                        else if (string.Equals(bi, "Pending", StringComparison.OrdinalIgnoreCase)) pe++;
                        if (implants(r) && (!HasText(biLotNo(r)) || !HasText(biResult(r)))) m++;
                    }
                    return new BiAnalyticsBreakdownRowDto { Id = id, Key = key, Total = t, Pass = p, Fail = f, Pending = pe, Missing = m };
                }).OrderByDescending(x => x.Total).ThenBy(x => x.Key).Take(20).ToList();
            }

            var bySterilizer = Breakdown(
                rows.GroupBy(x => x.SterilizerId),
                sid => (sid, sterLabels.GetValueOrDefault(sid) ?? sid.ToString(CultureInfo.InvariantCulture)),
                r => r.BiResult,
                r => r.BiLotNo,
                r => r.Implants);

            var byLot = Breakdown(
                rows.GroupBy(x => HasText(x.BiLotNo) ? Norm(x.BiLotNo) : "(blank)"),
                lot => (null, lot),
                r => r.BiResult,
                r => r.BiLotNo,
                r => r.Implants);

            var byResult = Breakdown(
                rows.GroupBy(x => HasText(x.BiResult) ? Norm(x.BiResult) : "(blank)"),
                res => (null, res),
                r => r.BiResult,
                r => r.BiLotNo,
                r => r.Implants);

            var completeness = new BiAnalyticsCompletenessDto
            {
                CyclesInScope = total,
                LotNoCaptured = rows.Count(x => HasText(x.BiLotNo)),
                ResultCaptured = rows.Count(x => HasText(x.BiResult)),
                TimeInCaptured = rows.Count(x => x.BiTimeIn.HasValue),
                TimeOutCaptured = rows.Count(x => x.BiTimeOut.HasValue)
            };

            return (new BiAnalyticsDto
            {
                TotalBiCycles = total,
                Passed = passed,
                Failed = failed,
                Pending = pending,
                MissingEntries = missing,
                ByDay = byDay,
                BySterilizer = bySterilizer,
                ByLotNumber = byLot,
                ByResult = byResult,
                Completeness = completeness
            }, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}

