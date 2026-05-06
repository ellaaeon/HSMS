namespace HSMS.Shared.Contracts;

/// <summary>
/// Validates <see cref="SterilizationUpsertDto"/> against database column limits (SQL DECIMAL / NVARCHAR sizes).
/// Messages are written for staff: they explain that the save failed because of invalid input and what to change.
/// </summary>
public static class SterilizationUpsertValidator
{
    /// <summary>Matches <c>DECIMAL(6,2)</c> for temperature columns.</summary>
    public const decimal MaxTemperatureMagnitude = 9999.99m;

    /// <summary>Matches <c>DECIMAL(8,3)</c> for pressure.</summary>
    public const decimal MaxPressureMagnitude = 99999.999m;

    public static string? Validate(SterilizationUpsertDto dto)
    {
        if (dto is null)
        {
            return "Cannot save this record: invalid or incomplete data. Close the screen and try again, or contact support if this keeps happening.";
        }

        if (string.IsNullOrWhiteSpace(dto.CycleNo))
        {
            return "Cannot save this record: invalid input — cycle number is empty. Enter a cycle number.";
        }

        var cycleNo = dto.CycleNo.Trim();
        if (cycleNo.Length > 32)
        {
            return "Cannot save this record: invalid input — cycle number is too long (maximum 32 characters). Shorten it and try again.";
        }

        if (string.IsNullOrWhiteSpace(dto.SterilizationType))
        {
            return "Cannot save this record: invalid input — sterilization type is missing. Choose high or low temperature.";
        }

        if (dto.SterilizationType.Trim().Length > 32)
        {
            return "Cannot save this record: invalid input — sterilization type text is too long for the system. Contact an administrator if you need help.";
        }

        if (dto.CycleProgram is not null && dto.CycleProgram.Trim().Length > 40)
        {
            return "Cannot save this record: invalid input — the cycle / program text is too long (maximum 40 characters).";
        }

        if (string.IsNullOrWhiteSpace(dto.OperatorName))
        {
            return "Cannot save this record: invalid input — operator name is missing. Enter who ran the load.";
        }

        if (dto.OperatorName.Trim().Length > 128)
        {
            return "Cannot save this record: invalid input — operator name is too long (maximum 128 characters). Shorten it and try again.";
        }

        if (dto.TemperatureC is { } t && Math.Abs(t) > MaxTemperatureMagnitude)
        {
            return "Cannot save this record: invalid input — the temperature (°C) you typed is out of range for the system. " +
                   $"Use a value between -{MaxTemperatureMagnitude} and {MaxTemperatureMagnitude} °C. " +
                   "Typical steam readings are well under 200 °C; check that you did not enter the wrong number.";
        }

        if (dto.Pressure is { } p && Math.Abs(p) > MaxPressureMagnitude)
        {
            return "Cannot save this record: invalid input — the pressure you typed is out of range for the system. " +
                   $"Use a value between -{MaxPressureMagnitude} and {MaxPressureMagnitude}.";
        }

        if (dto.ExposureTimeMinutes is { } m && (m < 0 || m > 24 * 60))
        {
            return "Cannot save this record: invalid input — exposure time must be between 0 and 1440 minutes.";
        }

        if (dto.BiLotNo is not null && dto.BiLotNo.Trim().Length > 64)
        {
            return "Cannot save this record: invalid input — BI Lot# is too long (maximum 64 characters). Shorten it and try again.";
        }

        if (dto.BiResult is not null && dto.BiResult.Trim().Length > 32)
        {
            return "Cannot save this record: invalid input — BI result text is too long (maximum 32 characters).";
        }

        if (string.IsNullOrWhiteSpace(dto.CycleStatus))
        {
            return "Cannot save this record: invalid input — cycle status is missing. Choose Draft, Completed, or Voided.";
        }

        if (dto.CycleStatus.Trim().Length > 32)
        {
            return "Cannot save this record: invalid input — cycle status text is too long for the system.";
        }

        if (dto.Notes is not null && dto.Notes.Trim().Length > 4000)
        {
            return "Cannot save this record: invalid input — notes are too long (maximum 4000 characters). Shorten the notes and try again.";
        }

        if (dto.Items is null || dto.Items.Count == 0)
        {
            return "Cannot save this record: invalid input — there must be at least one item on the load. Choose an item description.";
        }

        for (var i = 0; i < dto.Items.Count; i++)
        {
            var line = dto.Items[i];
            if (string.IsNullOrWhiteSpace(line.ItemName))
            {
                return $"Cannot save this record: invalid input — item line {i + 1} has no description. Choose an item.";
            }

            if (line.ItemName.Trim().Length > 256)
            {
                return $"Cannot save this record: invalid input — item line {i + 1} description is too long (maximum 256 characters).";
            }

            if (line.DepartmentName is not null && line.DepartmentName.Trim().Length > 256)
            {
                return $"Cannot save this record: invalid input — item line {i + 1} department text is too long (maximum 256 characters).";
            }

            if (line.DoctorOrRoom is not null && line.DoctorOrRoom.Trim().Length > 256)
            {
                return $"Cannot save this record: invalid input — item line {i + 1} doctor/room text is too long (maximum 256 characters).";
            }
        }

        return null;
    }
}
