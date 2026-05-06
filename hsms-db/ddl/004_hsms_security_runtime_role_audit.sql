/*
  HSMS — least-privilege runtime role + append-only audit / print logs

  Run this script against the HSMS database AFTER 001_hsms_init.sql (and optional 002/003).

  What it does
  ------------
  1) Creates database roles:
     - HSMS_Runtime   : day-to-day application SQL user / mapped Windows principal
     - HSMS_Migrator  : schema upgrades / EF migrations (elevated; separate login)

  2) HSMS_Runtime receives SELECT, INSERT, UPDATE, DELETE on operational tables.

  3) dbo.audit_logs and dbo.print_logs are append-only for HSMS_Runtime:
     - GRANT INSERT, SELECT
     - DENY UPDATE, DELETE
     So the app cannot tamper with audit or print history even if compromised at the app layer.

  4) HSMS_Migrator receives ALTER on schema dbo + CREATE TABLE for maintenance scripts.
     For heavy EF migrations, hospital IT may still prefer running as dbo / db_owner;
     document that choice outside this file.

  Binding principals (you must customize)
  ----------------------------------------
  This script does NOT create server logins (requires securityadmin). Example:

    CREATE LOGIN HSMS_Runtime WITH PASSWORD = N'<strong unique password>';
    CREATE USER HSMS_RuntimeApp FOR LOGIN HSMS_Runtime;
    ALTER ROLE HSMS_Runtime ADD MEMBER HSMS_RuntimeApp;

    CREATE LOGIN HSMS_Migrator WITH PASSWORD = N'<different strong password>';
    CREATE USER HSMS_MigratorApp FOR LOGIN HSMS_Migrator;
    ALTER ROLE HSMS_Migrator ADD MEMBER HSMS_MigratorApp;

  Hardening checklist (baseline)
  -------------------------------
  - Use Windows Authentication + gMSA / dedicated service account where possible instead of SQL logins.
  - Store runtime connection string outside repo; prefer DPAPI or OS secret store on the PC.
  - Do not grant HSMS_Runtime ddladmin / db_owner.
  - Run backups with encryption + restricted ACLs; verify RESTORE VERIFYONLY periodically.
  - Bind SQL Express TCP to localhost only if the database is single-machine.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF DATABASE_PRINCIPAL_ID(N'HSMS_Runtime') IS NULL
    CREATE ROLE HSMS_Runtime;

  IF DATABASE_PRINCIPAL_ID(N'HSMS_Migrator') IS NULL
    CREATE ROLE HSMS_Migrator;

  /* ---------- Operational tables (full CRUD for normal workflows) ---------- */
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tbl_account_login TO HSMS_Runtime;
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tbl_sterilizer_no TO HSMS_Runtime;
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tbl_departments TO HSMS_Runtime;
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tbl_dept_items TO HSMS_Runtime;
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tbl_doctors_rooms TO HSMS_Runtime;
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tbl_sterilization TO HSMS_Runtime;
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.tbl_str_items TO HSMS_Runtime;
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.qa_tests TO HSMS_Runtime;
  GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.cycle_receipts TO HSMS_Runtime;

  /* ---------- Append-only compliance tables (enforced in SQL Server) ---------- */
  GRANT SELECT, INSERT ON dbo.audit_logs TO HSMS_Runtime;
  DENY UPDATE, DELETE ON dbo.audit_logs TO HSMS_Runtime;

  GRANT SELECT, INSERT ON dbo.print_logs TO HSMS_Runtime;
  DENY UPDATE, DELETE ON dbo.print_logs TO HSMS_Runtime;

  /* ---------- Read-only reporting view ---------- */
  IF OBJECT_ID(N'dbo.vw_sterilization_latest_receipt', N'V') IS NOT NULL
    GRANT SELECT ON dbo.vw_sterilization_latest_receipt TO HSMS_Runtime;

  /* ---------- Migrator (deployment / DDL) — not for runtime app ---------- */
  GRANT ALTER ON SCHEMA::dbo TO HSMS_Migrator;
  GRANT CREATE TABLE TO HSMS_Migrator;

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
  DECLARE @msg NVARCHAR(4000) = ERROR_MESSAGE();
  RAISERROR(N'HSMS security DDL failed: %s', 16, 1, @msg);
END CATCH;
