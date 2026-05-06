namespace HSMS.Shared.Contracts;

/// <summary>
/// BI log "time in" defaults from Register Load: UTC instant when the cycle is persisted, for BI-tracked loads.
/// </summary>
public static class RegisterLoadBiTimeInRules
{
    public static bool ShouldStampBiTimeIn(SterilizationUpsertDto request)
    {
        if (!string.IsNullOrWhiteSpace(request.BiLotNo))
        {
            return true;
        }

        var br = request.BiResult?.Trim();
        if (string.IsNullOrEmpty(br))
        {
            return false;
        }

        return !string.Equals(br, "N/A", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Register Load should not stamp BI log "time in" anymore.
    /// Staff who perform the BI log sheet check will enter the time in the BI Log Sheets grid.
    /// </summary>
    public static DateTime? BiTimeInUtcForCreate(SterilizationUpsertDto request) => null;

    /// <summary>Backfills time-in on update only when still null (does not overwrite BI log sheet edits).</summary>
    public static DateTime? BiTimeInUtcForUpdate(DateTime? existingBiTimeIn, SterilizationUpsertDto request)
    {
        if (existingBiTimeIn.HasValue)
        {
            return null;
        }

        // Do not backfill automatically; BI time is owned by staff entry in the BI Log Sheets grid.
        return null;
    }
}
