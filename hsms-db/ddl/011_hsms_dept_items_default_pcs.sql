

SET NOCOUNT ON;
SET XACT_ABORT ON;

USE [HSMS];

BEGIN TRY
  BEGIN TRANSACTION;

  IF COL_LENGTH(N'dbo.tbl_dept_items', N'default_pcs') IS NULL
    ALTER TABLE dbo.tbl_dept_items ADD default_pcs INT NULL;

  DECLARE @hasDefaultPcs BIT =
    CASE WHEN COL_LENGTH(N'dbo.tbl_dept_items', N'default_pcs') IS NULL THEN 0 ELSE 1 END;

  /* Ensure non-negative constraints exist (or are updated) */
  IF @hasDefaultPcs = 1
  BEGIN
    DECLARE @dropSql NVARCHAR(MAX) = N'';
    SELECT @dropSql = @dropSql + N'ALTER TABLE dbo.tbl_dept_items DROP CONSTRAINT ' + QUOTENAME(cc.name) + N';' + CHAR(10)
    FROM sys.check_constraints cc
    INNER JOIN sys.objects o ON o.object_id = cc.parent_object_id
    WHERE o.object_id = OBJECT_ID(N'dbo.tbl_dept_items')
      AND (
        cc.name = N'CK_tbl_dept_items_qty' OR
        cc.name = N'CK_tbl_dept_items_pcs_qty' OR
        cc.definition LIKE N'%default_qty%' OR
        cc.definition LIKE N'%default_pcs%'
      );

    IF @dropSql <> N''
      EXEC sys.sp_executesql @dropSql;

    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tbl_dept_items_pcs_qty')
      ALTER TABLE dbo.tbl_dept_items
        ADD CONSTRAINT CK_tbl_dept_items_pcs_qty CHECK (
          (default_pcs IS NULL OR default_pcs >= 0) AND
          (default_qty IS NULL OR default_qty >= 0)
        );
  END
  ELSE
  BEGIN
    -- Column add didn't apply (or wrong DB/table). Avoid adding a CHECK referencing a missing column.
    IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_tbl_dept_items_qty')
      ALTER TABLE dbo.tbl_dept_items
        ADD CONSTRAINT CK_tbl_dept_items_qty CHECK (default_qty IS NULL OR default_qty >= 0);
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
  THROW;
END CATCH;

