using HSMS.Persistence.Entities;
using HSMS.Shared.Contracts;

namespace HSMS.Application.Analytics;

public static class SterilizationAnalyticsQueryBuilder
{
    public static IQueryable<Sterilization> Apply(IQueryable<Sterilization> source, AnalyticsFilterDto filter)
    {
        var q = source.AsQueryable();

        if (filter.FromUtc.HasValue) q = q.Where(x => x.CreatedAt >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue) q = q.Where(x => x.CreatedAt <= filter.ToUtc.Value);

        if (filter.SterilizerId is int sid) q = q.Where(x => x.SterilizerId == sid);

        if (!string.IsNullOrWhiteSpace(filter.SterilizationType))
        {
            var st = filter.SterilizationType.Trim();
            q = q.Where(x => x.SterilizationType == st);
        }

        if (!string.IsNullOrWhiteSpace(filter.LoadStatus))
        {
            var status = LoadRecordCycleStatuses.Normalize(filter.LoadStatus);
            if (!string.IsNullOrWhiteSpace(status))
            {
                q = q.Where(x => x.CycleStatus == status);
            }
        }

        if (filter.Implants is bool implants)
        {
            q = q.Where(x => x.Implants == implants);
        }

        if (!string.IsNullOrWhiteSpace(filter.BiLotNo))
        {
            var lot = filter.BiLotNo.Trim();
            q = q.Where(x => x.BiLotNo != null && x.BiLotNo == lot);
        }

        if (!string.IsNullOrWhiteSpace(filter.BiResult))
        {
            var r = filter.BiResult.Trim();
            q = q.Where(x => x.BiResult != null && x.BiResult == r);
        }

        if (!string.IsNullOrWhiteSpace(filter.Department))
        {
            var dept = filter.Department.Trim();
            q = q.Where(x => x.Items.Any(i => i.DepartmentName != null && i.DepartmentName == dept));
        }

        if (filter.DoctorRoomId is int drId)
        {
            q = q.Where(x => x.DoctorRoomId == drId);
        }

        if (!string.IsNullOrWhiteSpace(filter.CycleProgram))
        {
            var cp = filter.CycleProgram.Trim();
            q = q.Where(x => x.CycleProgram != null && x.CycleProgram == cp);
        }

        if (!string.IsNullOrWhiteSpace(filter.OperatorName))
        {
            var op = filter.OperatorName.Trim();
            q = q.Where(x => x.OperatorName == op);
        }

        if (filter.QaStatus is { } qa)
        {
            if (qa.RequireAnyQaTest)
            {
                q = q.Where(x => x.QaTests.Any());
            }

            if (qa.PendingApprovalOnly)
            {
                q = q.Where(x => x.QaTests.Any(t => t.ApprovedAt == null));
            }

            if (!string.IsNullOrWhiteSpace(qa.TestType))
            {
                var tt = qa.TestType.Trim();
                q = q.Where(x => x.QaTests.Any(t => t.TestType == tt));
            }

            if (!string.IsNullOrWhiteSpace(qa.Result))
            {
                var rr = qa.Result.Trim();
                q = q.Where(x => x.QaTests.Any(t => t.Result == rr));
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.GlobalSearch))
        {
            var s = filter.GlobalSearch.Trim();
            var lower = s.ToLowerInvariant();
            q = q.Where(x =>
                x.CycleNo.ToLower().Contains(lower)
                || x.OperatorName.ToLower().Contains(lower)
                || (x.Notes != null && x.Notes.ToLower().Contains(lower))
                || (x.BiLotNo != null && x.BiLotNo.ToLower().Contains(lower))
                || (x.BiResult != null && x.BiResult.ToLower().Contains(lower))
                || x.Items.Any(i =>
                    i.ItemName.ToLower().Contains(lower)
                    || (i.DepartmentName != null && i.DepartmentName.ToLower().Contains(lower))));
        }

        return q;
    }
}

