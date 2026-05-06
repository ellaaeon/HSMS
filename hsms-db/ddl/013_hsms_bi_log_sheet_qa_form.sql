/*
  BI log sheet (Mediclinic-style QA form): daily flag, incubator note, initials, +/- reads, notes on grid.
  Safe to run multiple times.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_daily') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_daily BIT NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_incubator_temp') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_incubator_temp NVARCHAR(48) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_time_in_initials') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_time_in_initials NVARCHAR(32) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_time_out_initials') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_time_out_initials NVARCHAR(32) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_processed_result_24m') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_processed_result_24m NCHAR(1) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_processed_result_24h') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_processed_result_24h NCHAR(1) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_control_result_24m') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_control_result_24m NCHAR(1) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_control_result_24h') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_control_result_24h NCHAR(1) NULL;

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
