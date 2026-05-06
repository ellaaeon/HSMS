/*
  Adds per-line department/doctor labels and separate pcs vs qty (legacy HSMS parity).
  Safe to run on databases created from an older 001_hsms_init.sql (before these columns).

  Note: ADD COLUMN and a separate CHECK (pcs > 0) in the same T-SQL batch can raise
  "Invalid column name 'pcs'" because the batch is compiled before execution. We add
  pcs + CHECK in one ALTER, and use dynamic SQL only when the column already exists
  without the check (e.g. partial run).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH(N'dbo.tbl_str_items', N'pcs') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_str_items ADD
      pcs INT NOT NULL CONSTRAINT DF_tbl_str_items_pcs_upgrade DEFAULT (1),
      CONSTRAINT CK_tbl_str_items_pcs CHECK (pcs > 0);
  END

  IF COL_LENGTH(N'dbo.tbl_str_items', N'department_name') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_str_items ADD department_name NVARCHAR(256) NULL;
  END

  IF COL_LENGTH(N'dbo.tbl_str_items', N'doctor_or_room') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_str_items ADD doctor_or_room NVARCHAR(256) NULL;
  END

  IF COL_LENGTH(N'dbo.tbl_str_items', N'pcs') IS NOT NULL
    AND NOT EXISTS (
      SELECT 1
      FROM sys.check_constraints
      WHERE name = N'CK_tbl_str_items_pcs'
        AND parent_object_id = OBJECT_ID(N'dbo.tbl_str_items')
    )
  BEGIN
    EXEC(N'ALTER TABLE dbo.tbl_str_items ADD CONSTRAINT CK_tbl_str_items_pcs CHECK (pcs > 0)');
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
