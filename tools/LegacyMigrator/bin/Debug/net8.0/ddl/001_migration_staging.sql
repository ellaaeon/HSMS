/*
  HSMS Legacy Migration - staging + idempotent mapping tables.
  Run this script on the target HSMS database BEFORE the first import.

  - migration_runs: every migration invocation produces one row (run_id GUID).
  - migration_mappings: per legacy entity -> new id (idempotent re-run).
  - migration_findings: validation issues per row (warnings + errors).
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRAN;

  IF OBJECT_ID(N'dbo.migration_runs', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.migration_runs (
      run_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_migration_runs PRIMARY KEY,
      started_at_utc  DATETIME2(0) NOT NULL CONSTRAINT DF_migration_runs_started_at DEFAULT (SYSUTCDATETIME()),
      completed_at_utc DATETIME2(0) NULL,
      dry_run         BIT NOT NULL,
      status          NVARCHAR(32) NOT NULL,
      summary_json    NVARCHAR(MAX) NULL
    );
  END

  IF OBJECT_ID(N'dbo.migration_mappings', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.migration_mappings (
      mapping_id    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_migration_mappings PRIMARY KEY,
      run_id        UNIQUEIDENTIFIER NOT NULL,
      entity        NVARCHAR(64) NOT NULL,
      legacy_id     NVARCHAR(64) NOT NULL,
      new_id        NVARCHAR(64) NULL,
      action        NVARCHAR(16) NOT NULL,
      created_at_utc DATETIME2(0) NOT NULL CONSTRAINT DF_migration_mappings_created_at DEFAULT (SYSUTCDATETIME()),
      CONSTRAINT FK_migration_mappings_run FOREIGN KEY (run_id) REFERENCES dbo.migration_runs(run_id)
    );
    CREATE UNIQUE INDEX UQ_migration_mappings_entity_legacy ON dbo.migration_mappings(entity, legacy_id);
  END

  IF OBJECT_ID(N'dbo.migration_findings', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.migration_findings (
      finding_id    BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_migration_findings PRIMARY KEY,
      run_id        UNIQUEIDENTIFIER NOT NULL,
      entity        NVARCHAR(64) NOT NULL,
      legacy_id     NVARCHAR(64) NULL,
      severity      NVARCHAR(16) NOT NULL,
      code          NVARCHAR(64) NOT NULL,
      message       NVARCHAR(2000) NOT NULL,
      created_at_utc DATETIME2(0) NOT NULL CONSTRAINT DF_migration_findings_created_at DEFAULT (SYSUTCDATETIME()),
      CONSTRAINT FK_migration_findings_run FOREIGN KEY (run_id) REFERENCES dbo.migration_runs(run_id)
    );
    CREATE INDEX IX_migration_findings_run_severity ON dbo.migration_findings(run_id, severity);
  END

  COMMIT TRAN;
  PRINT 'Migration staging tables ready.';
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRAN;
  THROW;
END CATCH;
