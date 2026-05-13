/*
  Module 4 - QA Tests workflow:
    * Approval columns (approved_by/approved_at/approved_remarks)
    * Unique constraint to prevent duplicate Leak/BowieDick per cycle per day
    * Helpful indexes for QA listings

  Idempotent: re-runnable. No data loss.
*/
USE [HSMS];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRAN;

  IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID('dbo.qa_tests') AND name = 'approved_by')
  BEGIN
    ALTER TABLE dbo.qa_tests ADD approved_by INT NULL;
  END

  IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID('dbo.qa_tests') AND name = 'approved_at')
  BEGIN
    ALTER TABLE dbo.qa_tests ADD approved_at DATETIME2(0) NULL;
  END

  IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID('dbo.qa_tests') AND name = 'approved_remarks')
  BEGIN
    ALTER TABLE dbo.qa_tests ADD approved_remarks NVARCHAR(500) NULL;
  END

  -- FK references approved_by added above; compile in a separate batch so the column resolves.
  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_tests_approved_by')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_tests ADD CONSTRAINT FK_qa_tests_approved_by FOREIGN KEY (approved_by) REFERENCES dbo.tbl_account_login(account_id)');
  END

  -- Filtered unique index on (sterilization_id, test_type, CAST(test_datetime AS DATE))
  -- Filtered indexes don't allow CAST in key columns directly; we use a computed persisted column.
  IF NOT EXISTS (SELECT 1 FROM sys.computed_columns WHERE name = 'test_date' AND [object_id] = OBJECT_ID('dbo.qa_tests'))
  BEGIN
    ALTER TABLE dbo.qa_tests ADD test_date AS (CAST(test_datetime AS DATE)) PERSISTED;
  END

  -- Indexes on columns introduced in this script: use dynamic SQL so compilation happens after ADD / computed column.
  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_qa_tests_cycle_type_date' AND [object_id] = OBJECT_ID('dbo.qa_tests'))
  BEGIN
    EXEC(N'CREATE UNIQUE INDEX UQ_qa_tests_cycle_type_date ON dbo.qa_tests(sterilization_id, test_type, test_date)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_qa_tests_approved_at' AND [object_id] = OBJECT_ID('dbo.qa_tests'))
  BEGIN
    EXEC(N'CREATE INDEX IX_qa_tests_approved_at ON dbo.qa_tests(approved_at DESC) WHERE approved_at IS NOT NULL');
  END

  COMMIT TRAN;
  PRINT 'ddl/020_hsms_qa_test_approval.sql applied.';
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRAN;
  THROW;
END CATCH;
