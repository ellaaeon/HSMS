/*
  Module 3 (Master Data CRUD): adds the cycle-programs master, plus sterilizer serial_number and
  maintenance_schedule columns, plus department/sterilizer disabled_at/disabled_by columns for true soft delete.
*/
USE [HSMS];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF OBJECT_ID(N'dbo.tbl_cycle_programs', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_cycle_programs (
      cycle_program_id  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_cycle_programs PRIMARY KEY,
      program_code      NVARCHAR(32) NOT NULL,
      program_name      NVARCHAR(64) NOT NULL,
      sterilization_type NVARCHAR(32) NULL,  -- High temperature / Low temperature / null = any
      default_temperature_c DECIMAL(6,2) NULL,
      default_pressure  DECIMAL(8,3) NULL,
      default_exposure_minutes INT NULL,
      is_active         BIT NOT NULL CONSTRAINT DF_tbl_cycle_programs_is_active DEFAULT (1),
      disabled_at       DATETIME2(0) NULL,
      disabled_by       INT NULL,
      created_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_cycle_programs_created_at DEFAULT (SYSUTCDATETIME()),
      created_by        INT NULL,
      updated_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_cycle_programs_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by        INT NULL,
      row_version       ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.tbl_cycle_programs
      ADD CONSTRAINT UQ_tbl_cycle_programs_code UNIQUE (program_code);
    ALTER TABLE dbo.tbl_cycle_programs
      ADD CONSTRAINT UQ_tbl_cycle_programs_name UNIQUE (program_name);
  END

  IF COL_LENGTH('dbo.tbl_sterilizer_no', 'serial_number') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_sterilizer_no ADD serial_number NVARCHAR(64) NULL;
  END

  IF COL_LENGTH('dbo.tbl_sterilizer_no', 'maintenance_schedule') IS NULL
  BEGIN
    -- Free-form text; some hospitals use ISO 17665 cadence (weekly/quarterly), others use vendor SLAs.
    ALTER TABLE dbo.tbl_sterilizer_no ADD maintenance_schedule NVARCHAR(128) NULL;
  END

  IF COL_LENGTH('dbo.tbl_sterilizer_no', 'disabled_at') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_sterilizer_no ADD disabled_at DATETIME2(0) NULL;
    ALTER TABLE dbo.tbl_sterilizer_no ADD disabled_by INT NULL;
  END

  IF COL_LENGTH('dbo.tbl_departments', 'disabled_at') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_departments ADD disabled_at DATETIME2(0) NULL;
    ALTER TABLE dbo.tbl_departments ADD disabled_by INT NULL;
  END

  IF COL_LENGTH('dbo.tbl_doctors_rooms', 'disabled_at') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_doctors_rooms ADD disabled_at DATETIME2(0) NULL;
    ALTER TABLE dbo.tbl_doctors_rooms ADD disabled_by INT NULL;
  END

  IF COL_LENGTH('dbo.tbl_dept_items', 'disabled_at') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_dept_items ADD disabled_at DATETIME2(0) NULL;
    ALTER TABLE dbo.tbl_dept_items ADD disabled_by INT NULL;
  END

  -- Sterilizer serial uniqueness (where set). Conditional unique index lets us roll out without backfilling.
  -- Use dynamic SQL so the index is compiled in a separate batch after ALTER TABLE ADD serial_number
  -- (otherwise SQL Server raises "Invalid column name 'serial_number'" for the whole script batch).
  IF COL_LENGTH(N'dbo.tbl_sterilizer_no', N'serial_number') IS NOT NULL
     AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UQ_tbl_sterilizer_no_serial' AND object_id = OBJECT_ID(N'dbo.tbl_sterilizer_no'))
  BEGIN
    EXEC(N'CREATE UNIQUE INDEX UQ_tbl_sterilizer_no_serial ON dbo.tbl_sterilizer_no(serial_number) WHERE serial_number IS NOT NULL');
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
