/*
  Instrument checks (pre/post) log
*/
USE [HSMS];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF OBJECT_ID(N'dbo.tbl_instrument_checks', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_instrument_checks (
      instrument_check_id  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_instrument_checks PRIMARY KEY,
      checked_at_utc       DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_instrument_checks_checked_at_utc DEFAULT (SYSUTCDATETIME()),
      item_name            NVARCHAR(256) NOT NULL,
      serial_reference     NVARCHAR(128) NULL,
      checked_by_name      NVARCHAR(128) NOT NULL,
      witness_by_name      NVARCHAR(128) NULL,
      remarks              NVARCHAR(2000) NULL,

      created_at           DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_instrument_checks_created_at DEFAULT (SYSUTCDATETIME()),
      created_by           INT NULL,
      row_version          ROWVERSION NOT NULL
    );

    CREATE INDEX IX_tbl_instrument_checks_checked_at ON dbo.tbl_instrument_checks(checked_at_utc DESC);
    CREATE INDEX IX_tbl_instrument_checks_item_name ON dbo.tbl_instrument_checks(item_name);

    ALTER TABLE dbo.tbl_instrument_checks
      ADD CONSTRAINT FK_tbl_instrument_checks_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;

