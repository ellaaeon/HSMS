/*
  HSMS — relax audit_logs.action for structured audit identifiers

  Problem
  -------
  001_hsms_init.sql defined:
    - action NVARCHAR(16) NOT NULL
    - CK_audit_logs_action allowing only Create, Update, Delete, Print, Login, Logout

  The app now writes structured actions (e.g. Login.Success, Sterilization.Update,
  Masters.Sterilizer.Deactivate) which fail INSERT with CHECK / truncation errors.

  Run this on the SAME database as the desktop connection (e.g. HSMS).
  Safe to run multiple times.

  IMPORTANT: If your SQL tool is connected to "master", you would alter the wrong database.
  This script starts with USE [HSMS] — change the name if your catalog differs from appsettings.
*/
USE [HSMS];
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NULL
    THROW 50008, N'008: dbo.audit_logs not found. Edit USE [HSMS] at top of this script to match ConnectionStrings:SqlServer (your app database).', 1;

  BEGIN TRANSACTION;

  IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_audit_logs_action' AND parent_object_id = OBJECT_ID(N'dbo.audit_logs'))
    ALTER TABLE dbo.audit_logs DROP CONSTRAINT CK_audit_logs_action;

  /* Room for dotted action names; keep NOT NULL. */
  ALTER TABLE dbo.audit_logs ALTER COLUMN action NVARCHAR(96) NOT NULL;

  COMMIT TRANSACTION;

  PRINT N'008 completed: audit_logs.action widened; CK_audit_logs_action removed. Database=' + QUOTENAME(DB_NAME());
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
