/*
  BI log sheet (QA form): numeric readings for +/- fields.
  Adds optional integer columns to store values alongside the existing NCHAR(1) +/− columns.
  Safe to run multiple times.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_processed_value_24m') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_processed_value_24m INT NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_processed_value_24h') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_processed_value_24h INT NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_control_value_24m') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_control_value_24m INT NULL;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_control_value_24h') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD bi_control_value_24h INT NULL;

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;

