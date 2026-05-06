/*
  Add cycle_program to tbl_sterilization (instruments, Bowie Dick, leak test, warm up).

  Run on HSMS DB. Safe to re-run.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

USE [HSMS];

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH(N'dbo.tbl_sterilization', N'cycle_program') IS NULL
    ALTER TABLE dbo.tbl_sterilization ADD cycle_program NVARCHAR(40) NULL;

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
