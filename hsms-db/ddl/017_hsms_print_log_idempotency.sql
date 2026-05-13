/*
  Module 1 (Reporting + Printing): make print_logs.correlation_id unique so the desktop client can safely
  retry POST /api/print-logs without duplicating audit history. Also adds report_version + station_id columns
  used by the audit/print viewer (Module 5).
*/
USE [HSMS];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH('dbo.print_logs', 'report_version') IS NULL
  BEGIN
    ALTER TABLE dbo.print_logs ADD report_version NVARCHAR(32) NULL;
  END

  IF COL_LENGTH('dbo.print_logs', 'station_id') IS NULL
  BEGIN
    ALTER TABLE dbo.print_logs ADD station_id NVARCHAR(64) NULL;
  END

  -- Drop any previous non-unique index on correlation_id, then add unique constraint.
  IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_print_logs_correlation_id' AND object_id = OBJECT_ID(N'dbo.print_logs'))
  BEGIN
    DROP INDEX IX_print_logs_correlation_id ON dbo.print_logs;
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_print_logs_correlation_id' AND object_id = OBJECT_ID(N'dbo.print_logs'))
  BEGIN
    CREATE UNIQUE INDEX UQ_print_logs_correlation_id ON dbo.print_logs(correlation_id);
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_print_logs_printed_by' AND object_id = OBJECT_ID(N'dbo.print_logs'))
  BEGIN
    CREATE INDEX IX_print_logs_printed_by ON dbo.print_logs(printed_by, printed_at DESC);
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_print_logs_sterilization' AND object_id = OBJECT_ID(N'dbo.print_logs'))
  BEGIN
    CREATE INDEX IX_print_logs_sterilization ON dbo.print_logs(sterilization_id) WHERE sterilization_id IS NOT NULL;
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
