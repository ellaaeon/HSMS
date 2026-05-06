/*
  Adds BI Log Sheet columns to dbo.tbl_sterilization (legacy HSMS parity).
  Safe to run multiple times.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_lot_no') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_lot_no NVARCHAR(64) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'load_qty') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD load_qty INT NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'exposure_time_minutes') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD exposure_time_minutes INT NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'temperature_in_c') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD temperature_in_c DECIMAL(6,2) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'temperature_out_c') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD temperature_out_c DECIMAL(6,2) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'cycle_time_in') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD cycle_time_in DATETIME2(0) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'cycle_time_out') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD cycle_time_out DATETIME2(0) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_time_in') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_time_in DATETIME2(0) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_time_out') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_time_out DATETIME2(0) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_time_cut') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_time_cut DATETIME2(0) NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_strip_no') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_strip_no NVARCHAR(64) NULL;

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;

