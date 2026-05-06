/*
  Adds timestamp for when BI result was last changed (BI log sheet / register load).
  Run on existing HSMS databases after prior ddl scripts.
*/
SET NOCOUNT ON;

IF COL_LENGTH(N'dbo.tbl_sterilization', N'bi_result_updated_at') IS NULL
BEGIN
  ALTER TABLE dbo.tbl_sterilization ADD bi_result_updated_at DATETIME2(0) NULL;
END
