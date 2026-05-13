/*
  Module 5 - Enterprise Sterilization QA Test Records:
    - qa_test_records: unified record store for multiple QA categories
    - qa_test_status_events: immutable status history / timeline
    - qa_test_attachments: attachment metadata (file path stored on disk)

  Idempotent: re-runnable. No data loss.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRAN;

  IF OBJECT_ID('dbo.qa_test_records', 'U') IS NULL
  BEGIN
    CREATE TABLE dbo.qa_test_records
    (
      record_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_qa_test_records PRIMARY KEY,
      category NVARCHAR(48) NOT NULL,
      sterilization_id INT NULL,
      sterilizer_id INT NULL,
      test_datetime_utc DATETIME2(0) NOT NULL,
      department NVARCHAR(128) NULL,
      technician NVARCHAR(128) NULL,
      status NVARCHAR(32) NOT NULL,
      result_label NVARCHAR(32) NULL,
      summary NVARCHAR(240) NULL,
      notes NVARCHAR(4000) NULL,
      created_at_utc DATETIME2(0) NOT NULL CONSTRAINT DF_qa_test_records_created_at DEFAULT (SYSUTCDATETIME()),
      created_by INT NULL,
      updated_at_utc DATETIME2(0) NULL,
      updated_by INT NULL,
      reviewed_by INT NULL,
      reviewed_at_utc DATETIME2(0) NULL,
      approved_by INT NULL,
      approved_at_utc DATETIME2(0) NULL,
      archived_at_utc DATETIME2(0) NULL,
      row_version ROWVERSION NOT NULL
    );
  END

  IF OBJECT_ID('dbo.qa_test_status_events', 'U') IS NULL
  BEGIN
    CREATE TABLE dbo.qa_test_status_events
    (
      event_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_qa_test_status_events PRIMARY KEY,
      record_id BIGINT NOT NULL,
      event_at_utc DATETIME2(0) NOT NULL CONSTRAINT DF_qa_test_status_events_event_at DEFAULT (SYSUTCDATETIME()),
      actor_account_id INT NULL,
      from_status NVARCHAR(32) NOT NULL,
      to_status NVARCHAR(32) NOT NULL,
      comment NVARCHAR(500) NULL
    );
  END

  IF OBJECT_ID('dbo.qa_test_attachments', 'U') IS NULL
  BEGIN
    CREATE TABLE dbo.qa_test_attachments
    (
      attachment_id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_qa_test_attachments PRIMARY KEY,
      record_id BIGINT NOT NULL,
      file_path NVARCHAR(400) NOT NULL,
      file_name NVARCHAR(260) NOT NULL,
      content_type NVARCHAR(64) NOT NULL,
      file_size_bytes BIGINT NOT NULL,
      sha256 NVARCHAR(80) NULL,
      captured_at_utc DATETIME2(0) NOT NULL CONSTRAINT DF_qa_test_attachments_captured_at DEFAULT (SYSUTCDATETIME()),
      captured_by INT NULL,
      row_version ROWVERSION NOT NULL
    );
  END

  -- Foreign keys (compile in dynamic batches so they can be added after CREATE)
  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_test_records_sterilization')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_test_records ADD CONSTRAINT FK_qa_test_records_sterilization FOREIGN KEY (sterilization_id) REFERENCES dbo.tbl_sterilization(sterilization_id)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_test_records_sterilizer')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_test_records ADD CONSTRAINT FK_qa_test_records_sterilizer FOREIGN KEY (sterilizer_id) REFERENCES dbo.tbl_sterilizer_no(sterilizer_id)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_test_records_created_by')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_test_records ADD CONSTRAINT FK_qa_test_records_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id)');
  END
  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_test_records_reviewed_by')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_test_records ADD CONSTRAINT FK_qa_test_records_reviewed_by FOREIGN KEY (reviewed_by) REFERENCES dbo.tbl_account_login(account_id)');
  END
  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_test_records_approved_by')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_test_records ADD CONSTRAINT FK_qa_test_records_approved_by FOREIGN KEY (approved_by) REFERENCES dbo.tbl_account_login(account_id)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_test_status_events_record')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_test_status_events ADD CONSTRAINT FK_qa_test_status_events_record FOREIGN KEY (record_id) REFERENCES dbo.qa_test_records(record_id) ON DELETE CASCADE');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_qa_test_attachments_record')
  BEGIN
    EXEC(N'ALTER TABLE dbo.qa_test_attachments ADD CONSTRAINT FK_qa_test_attachments_record FOREIGN KEY (record_id) REFERENCES dbo.qa_test_records(record_id) ON DELETE CASCADE');
  END

  -- Helpful indexes
  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_qa_test_records_category_datetime' AND [object_id] = OBJECT_ID('dbo.qa_test_records'))
  BEGIN
    EXEC(N'CREATE INDEX IX_qa_test_records_category_datetime ON dbo.qa_test_records(category, test_datetime_utc DESC)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_qa_test_records_status' AND [object_id] = OBJECT_ID('dbo.qa_test_records'))
  BEGIN
    EXEC(N'CREATE INDEX IX_qa_test_records_status ON dbo.qa_test_records(status, test_datetime_utc DESC)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_qa_test_records_sterilizer' AND [object_id] = OBJECT_ID('dbo.qa_test_records'))
  BEGIN
    EXEC(N'CREATE INDEX IX_qa_test_records_sterilizer ON dbo.qa_test_records(sterilizer_id, test_datetime_utc DESC)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_qa_test_status_events_record_event_at' AND [object_id] = OBJECT_ID('dbo.qa_test_status_events'))
  BEGIN
    EXEC(N'CREATE INDEX IX_qa_test_status_events_record_event_at ON dbo.qa_test_status_events(record_id, event_at_utc DESC)');
  END

  IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_qa_test_attachments_record' AND [object_id] = OBJECT_ID('dbo.qa_test_attachments'))
  BEGIN
    EXEC(N'CREATE INDEX IX_qa_test_attachments_record ON dbo.qa_test_attachments(record_id, captured_at_utc DESC)');
  END

  COMMIT TRAN;
  PRINT 'ddl/022_hsms_sterilization_qa_module.sql applied.';
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRAN;
  THROW;
END CATCH;

