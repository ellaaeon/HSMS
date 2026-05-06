/*
  Staff profile fields on dbo.tbl_account_login (nullable for existing rows).
  Run against the same database as the desktop connection (e.g. HSMS).

  Safe to run multiple times.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH(N'dbo.tbl_account_login', N'first_name') IS NULL
    ALTER TABLE dbo.tbl_account_login ADD first_name NVARCHAR(80) NULL;

  IF COL_LENGTH(N'dbo.tbl_account_login', N'last_name') IS NULL
    ALTER TABLE dbo.tbl_account_login ADD last_name NVARCHAR(80) NULL;

  IF COL_LENGTH(N'dbo.tbl_account_login', N'email') IS NULL
    ALTER TABLE dbo.tbl_account_login ADD email NVARCHAR(128) NULL;

  IF COL_LENGTH(N'dbo.tbl_account_login', N'phone') IS NULL
    ALTER TABLE dbo.tbl_account_login ADD phone NVARCHAR(40) NULL;

  IF COL_LENGTH(N'dbo.tbl_account_login', N'department') IS NULL
    ALTER TABLE dbo.tbl_account_login ADD department NVARCHAR(128) NULL;

  IF COL_LENGTH(N'dbo.tbl_account_login', N'job_title') IS NULL
    ALTER TABLE dbo.tbl_account_login ADD job_title NVARCHAR(128) NULL;

  IF COL_LENGTH(N'dbo.tbl_account_login', N'employee_id') IS NULL
    ALTER TABLE dbo.tbl_account_login ADD employee_id NVARCHAR(32) NULL;

  /*
    Optional dev default for seeded admin (only if still blank).
    Use dynamic SQL so this batch does not compile UPDATE before ADD columns exist.
  */
  IF COL_LENGTH(N'dbo.tbl_account_login', N'first_name') IS NOT NULL
     AND COL_LENGTH(N'dbo.tbl_account_login', N'last_name') IS NOT NULL
  BEGIN
    EXEC(N'
      UPDATE dbo.tbl_account_login
      SET
        first_name = N''System'',
        last_name = N''Administrator''
      WHERE username = N''admin''
        AND first_name IS NULL
        AND last_name IS NULL;
    ');
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
