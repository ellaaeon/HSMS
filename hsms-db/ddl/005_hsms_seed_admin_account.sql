/*
  Ensures default admin account exists with known dev credentials.

  Username: admin
  Password: admin   (BCrypt; rotate before production)

  Run after 001_hsms_init.sql (and optional 002–004). Safe to re-run: updates hash/role/active for username admin.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF EXISTS (SELECT 1 FROM dbo.tbl_account_login WHERE username = N'admin')
  BEGIN
    UPDATE dbo.tbl_account_login
    SET
      password_hash = N'$2a$11$ZMbqC3ZFld/waA3W0Suk6uVaBWFYec.00dqjcdnhIgf75tva/L/UC',
      role = N'Admin',
      is_active = 1,
      updated_at = SYSUTCDATETIME()
    WHERE username = N'admin';
  END
  ELSE
  BEGIN
    INSERT INTO dbo.tbl_account_login (username, password_hash, role, is_active, created_by, updated_by)
    VALUES (
      N'admin',
      N'$2a$11$ZMbqC3ZFld/waA3W0Suk6uVaBWFYec.00dqjcdnhIgf75tva/L/UC',
      N'Admin',
      1,
      NULL,
      NULL
    );
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
