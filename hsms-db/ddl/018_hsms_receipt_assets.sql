/*
  Module 2 (Receipts / Attachments): derived assets per receipt + cycle/captured_by + content hash dedupe support.
*/
USE [HSMS];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF OBJECT_ID(N'dbo.cycle_receipt_assets', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.cycle_receipt_assets (
      asset_id          INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cycle_receipt_assets PRIMARY KEY,
      receipt_id        INT NOT NULL,
      asset_kind        NVARCHAR(16) NOT NULL,
      file_path         NVARCHAR(400) NOT NULL,
      width_px          INT NULL,
      height_px         INT NULL,
      file_size_bytes   BIGINT NOT NULL,
      generated_at_utc  DATETIME2(0) NOT NULL CONSTRAINT DF_cycle_receipt_assets_generated_at DEFAULT (SYSUTCDATETIME())
    );

    ALTER TABLE dbo.cycle_receipt_assets
      ADD CONSTRAINT CK_cycle_receipt_assets_kind CHECK (asset_kind IN (N'preview_png', N'thumbnail_png'));

    ALTER TABLE dbo.cycle_receipt_assets
      ADD CONSTRAINT FK_cycle_receipt_assets_receipt FOREIGN KEY (receipt_id)
      REFERENCES dbo.cycle_receipts(receipt_id) ON DELETE CASCADE;

    CREATE UNIQUE INDEX UQ_cycle_receipt_assets_receipt_kind ON dbo.cycle_receipt_assets(receipt_id, asset_kind);
  END

  IF OBJECT_ID(N'dbo.cycle_receipt_derivation_state', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.cycle_receipt_derivation_state (
      receipt_id        INT NOT NULL CONSTRAINT PK_cycle_receipt_derivation_state PRIMARY KEY,
      state             NVARCHAR(16) NOT NULL,
      attempts          INT NOT NULL CONSTRAINT DF_crds_attempts DEFAULT (0),
      last_error        NVARCHAR(2000) NULL,
      updated_at_utc    DATETIME2(0) NOT NULL CONSTRAINT DF_crds_updated_at DEFAULT (SYSUTCDATETIME())
    );

    ALTER TABLE dbo.cycle_receipt_derivation_state
      ADD CONSTRAINT CK_cycle_receipt_derivation_state_state
        CHECK (state IN (N'Pending', N'Running', N'Completed', N'Failed', N'NotApplicable'));

    ALTER TABLE dbo.cycle_receipt_derivation_state
      ADD CONSTRAINT FK_cycle_receipt_derivation_state_receipt FOREIGN KEY (receipt_id)
      REFERENCES dbo.cycle_receipts(receipt_id) ON DELETE CASCADE;
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
