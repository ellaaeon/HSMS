/*
  HSMS — per-user analytics presets

  Stores user-saved analytics configurations (filters, breakdowns, chart prefs) as JSON per account.
*/
USE [HSMS];
SET NOCOUNT ON;

IF OBJECT_ID(N'dbo.analytics_presets', N'U') IS NULL
BEGIN
  CREATE TABLE dbo.analytics_presets (
    preset_id    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_analytics_presets PRIMARY KEY,
    account_id   INT NOT NULL,
    name         NVARCHAR(128) NOT NULL,
    is_default   BIT NOT NULL CONSTRAINT DF_analytics_presets_is_default DEFAULT (0),
    preset_json  NVARCHAR(MAX) NOT NULL,
    created_at   DATETIME2(0) NOT NULL CONSTRAINT DF_analytics_presets_created_at DEFAULT (SYSUTCDATETIME()),
    updated_at   DATETIME2(0) NOT NULL CONSTRAINT DF_analytics_presets_updated_at DEFAULT (SYSUTCDATETIME())
  );

  ALTER TABLE dbo.analytics_presets
    ADD CONSTRAINT FK_analytics_presets_account FOREIGN KEY (account_id)
      REFERENCES dbo.tbl_account_login(account_id);

  CREATE UNIQUE INDEX UQ_analytics_presets_account_name ON dbo.analytics_presets(account_id, name);
  CREATE INDEX IX_analytics_presets_account_updated ON dbo.analytics_presets(account_id, updated_at DESC);
  CREATE INDEX IX_analytics_presets_default ON dbo.analytics_presets(account_id, is_default) WHERE is_default = 1;
END;

