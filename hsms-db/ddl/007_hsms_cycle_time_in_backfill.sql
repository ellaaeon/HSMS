/* Optional backfill: align legacy cycle_time_in with registration instant when missing. */

IF COL_LENGTH(N'dbo.tbl_sterilization', N'cycle_time_in') IS NOT NULL
   AND COL_LENGTH(N'dbo.tbl_sterilization', N'created_at') IS NOT NULL
BEGIN
  UPDATE dbo.tbl_sterilization SET cycle_time_in = created_at WHERE cycle_time_in IS NULL;
END
GO
