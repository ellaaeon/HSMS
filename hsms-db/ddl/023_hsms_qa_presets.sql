/*
  Module 6 - QA Presets (Saved Filters)
    - qa_test_presets: per-user saved filter sets for Test Records module

  Idempotent: re-runnable. No data loss.
*/
USE [HSMS];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRAN;

  IF OBJECT_ID('dbo.qa_test_presets', 'U') IS NULL
  BEGIN
    CREATE TABLE dbo.qa_test_presets
    (
      preset_id INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_qa_test_presets PRIMARY KEY,
      account_id INT NOT NULL,
      name NVARCHAR(128) NOT NULL,
      is_default BIT NOT NULL CONSTRAINT DF_qa_test_presets_is_default DEFAULT (0),
      preset_json NVARCHAR(MAX) NOT NULL CONSTRAINT DF_qa_test_presets_preset_json DEFAULT (N'{}'),
      created_at DATETIME2(0) NOT NULL CONSTRAINT DF_qa_test_presets_created_at DEFAULT (SYSUTCDATETIME()),
      updated_at DATETIME2(0) NOT NULL CONSTRAINT DF_qa_test_presets_updated_at DEFAULT (SYSUTCDATETIME())
    );
  END

  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_test_presets_account')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_test_presets ADD CONSTRAINT FK_qa_test_presets_account FOREIGN KEY (account_id) REFERENCES dbo.tbl_account_login(account_id)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_qa_test_presets_account_name' AND [object_id] = OBJECT_ID('dbo.qa_test_presets'))
  BEGIN
    EXEC(N'CREATE UNIQUE INDEX UQ_qa_test_presets_account_name ON dbo.qa_test_presets(account_id, name)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_qa_test_presets_default' AND [object_id] = OBJECT_ID('dbo.qa_test_presets'))
  BEGIN
    EXEC(N'CREATE INDEX IX_qa_test_presets_default ON dbo.qa_test_presets(account_id, is_default)');
  END

  COMMIT TRAN;
  PRINT 'ddl/023_hsms_qa_presets.sql applied.';
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRAN;
  THROW;
END CATCH;

