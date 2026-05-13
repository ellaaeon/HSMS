/*
  Module 6 - Instrument Checks: witness approval + photo attachments.

  Adds:
    * witness_approved_at, witness_approved_by columns to tbl_instrument_checks.
    * tbl_instrument_check_attachments table for photo evidence (reuses storage root).

  Idempotent: re-runnable.
*/
USE [HSMS];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRAN;

  IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID('dbo.tbl_instrument_checks') AND name = 'witness_approved_at')
  BEGIN
    ALTER TABLE dbo.tbl_instrument_checks ADD witness_approved_at DATETIME2(0) NULL;
  END

  IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE [object_id] = OBJECT_ID('dbo.tbl_instrument_checks') AND name = 'witness_approved_by')
  BEGIN
    ALTER TABLE dbo.tbl_instrument_checks ADD witness_approved_by INT NULL;
  END

  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_tbl_instrument_checks_witness_approved_by')
  BEGIN
    ALTER TABLE dbo.tbl_instrument_checks
      ADD CONSTRAINT FK_tbl_instrument_checks_witness_approved_by FOREIGN KEY (witness_approved_by) REFERENCES dbo.tbl_account_login(account_id);
  END

  IF OBJECT_ID(N'dbo.tbl_instrument_check_attachments', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_instrument_check_attachments (
      attachment_id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_instrument_check_attachments PRIMARY KEY,
      instrument_check_id  INT NOT NULL,
      file_path            NVARCHAR(400) NOT NULL,
      file_name            NVARCHAR(260) NOT NULL,
      content_type         NVARCHAR(64) NOT NULL,
      file_size_bytes      BIGINT NOT NULL,
      sha256               CHAR(64) NULL,
      captured_at          DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_instrument_check_attachments_captured_at DEFAULT (SYSUTCDATETIME()),
      captured_by          INT NULL,
      row_version          ROWVERSION NOT NULL,

      CONSTRAINT FK_tbl_instrument_check_attachments_instrument_check FOREIGN KEY (instrument_check_id)
        REFERENCES dbo.tbl_instrument_checks(instrument_check_id),
      CONSTRAINT FK_tbl_instrument_check_attachments_captured_by FOREIGN KEY (captured_by)
        REFERENCES dbo.tbl_account_login(account_id)
    );

    CREATE INDEX IX_tbl_instrument_check_attachments_check ON dbo.tbl_instrument_check_attachments(instrument_check_id);
    CREATE INDEX IX_tbl_instrument_check_attachments_sha ON dbo.tbl_instrument_check_attachments(sha256) WHERE sha256 IS NOT NULL;
  END

  COMMIT TRAN;
  PRINT 'ddl/021_hsms_instrument_check_extensions.sql applied.';
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRAN;
  THROW;
END CATCH;
