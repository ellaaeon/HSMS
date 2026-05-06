/*
  Backfill tbl_account_login.created_at when it was stored as EF default (0001-01-01)
  or other implausible values, using the earliest Account.Create audit row for that account.

  Safe to re-run: only updates rows that still look invalid.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

USE [HSMS];

BEGIN TRY
  BEGIN TRANSACTION;

  ;WITH first_create AS (
    SELECT
      TRY_CAST(a.entity_id AS INT) AS account_id,
      MIN(a.event_at) AS first_event_at
    FROM dbo.audit_logs AS a
    WHERE a.entity_name = N'tbl_account_login'
      AND a.action = N'Account.Create'
      AND TRY_CAST(a.entity_id AS INT) IS NOT NULL
    GROUP BY TRY_CAST(a.entity_id AS INT)
  )
  UPDATE al
  SET created_at = fc.first_event_at
  FROM dbo.tbl_account_login AS al
  INNER JOIN first_create AS fc ON fc.account_id = al.account_id
  WHERE al.created_at < '2002-01-01';

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
