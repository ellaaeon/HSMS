/*
  Adds manufacturer and purchase_date to sterilizer master table.
  Safe for existing databases created before these columns existed.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH(N'dbo.tbl_sterilizer_no', N'manufacturer') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_sterilizer_no ADD manufacturer NVARCHAR(128) NULL;
  END

  IF COL_LENGTH(N'dbo.tbl_sterilizer_no', N'purchase_date') IS NULL
  BEGIN
    ALTER TABLE dbo.tbl_sterilizer_no ADD purchase_date DATE NULL;
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;
