using HSMS.Persistence.Entities;

namespace HSMS.Application.Services;

/// <summary>
/// BI log sheet tracks loads where Register Load had Biological indicator = Yes.
/// "No" is persisted as <see cref="Sterilization.BiResult"/> <c>N/A</c> (see WPF <c>GetBiResultForSave</c>).
/// </summary>
internal static class SterilizationBiLogSheetFilter
{
    public static IQueryable<Sterilization> WhereUsesBiologicalIndicator(IQueryable<Sterilization> q) =>
        q.Where(x =>
            x.BiResult != null &&
            x.BiResult.Trim().Length > 0 &&
            // Use ToLower() so EF Core can translate; string.Equals(..., StringComparison) is not supported.
            x.BiResult.ToLower() != "n/a");
}
