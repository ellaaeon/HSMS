/*
  HSMS (Hospital Sterilization Management System)
  SQL Server Express schema (offline LAN)

  Notes:
  - Uses dbo schema only (simple ops)
  - Uses ROWVERSION for optimistic concurrency on primary transactional tables
  - Uses UTC timestamps via SYSUTCDATETIME() for consistency across client PCs
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
  BEGIN TRANSACTION;

  /* Create DB is intentionally omitted (env-specific).
     Run this script inside the target HSMS database (set the SSMS database dropdown or USE [your_db]; in a prior batch). */

  /* ---------- Core security ---------- */
  IF OBJECT_ID(N'dbo.tbl_account_login', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_account_login (
      account_id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_account_login PRIMARY KEY,
      username          NVARCHAR(64) NOT NULL,
      password_hash     NVARCHAR(255) NOT NULL,
      role              NVARCHAR(32) NOT NULL,
      is_active         BIT NOT NULL CONSTRAINT DF_tbl_account_login_is_active DEFAULT (1),
      last_login_at     DATETIME2(0) NULL,

      first_name        NVARCHAR(80) NULL,
      last_name         NVARCHAR(80) NULL,
      email             NVARCHAR(128) NULL,
      phone             NVARCHAR(40) NULL,
      department        NVARCHAR(128) NULL,
      job_title         NVARCHAR(128) NULL,
      employee_id       NVARCHAR(32) NULL,

      created_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_account_login_created_at DEFAULT (SYSUTCDATETIME()),
      created_by        INT NULL,
      updated_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_account_login_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by        INT NULL,
      row_version       ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.tbl_account_login
      ADD CONSTRAINT UQ_tbl_account_login_username UNIQUE (username);

    ALTER TABLE dbo.tbl_account_login
      ADD CONSTRAINT CK_tbl_account_login_role CHECK (role IN (N'Admin', N'Staff'));

    ALTER TABLE dbo.tbl_account_login
      ADD CONSTRAINT CK_tbl_account_login_username_len CHECK (LEN(username) BETWEEN 3 AND 64);
  END

  /* Self-referencing audit fields (nullable for bootstrap/system actions) */
  IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tbl_account_login_created_by'
  )
  BEGIN
    ALTER TABLE dbo.tbl_account_login
      ADD CONSTRAINT FK_tbl_account_login_created_by
      FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);
  END

  IF NOT EXISTS (
    SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_tbl_account_login_updated_by'
  )
  BEGIN
    ALTER TABLE dbo.tbl_account_login
      ADD CONSTRAINT FK_tbl_account_login_updated_by
      FOREIGN KEY (updated_by) REFERENCES dbo.tbl_account_login(account_id);
  END

  /* ---------- Master data ---------- */
  IF OBJECT_ID(N'dbo.tbl_sterilizer_no', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_sterilizer_no (
      sterilizer_id     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_sterilizer_no PRIMARY KEY,
      sterilizer_no     NVARCHAR(32) NOT NULL,
      model             NVARCHAR(64) NULL,
      manufacturer      NVARCHAR(128) NULL,
      purchase_date     DATE NULL,
      is_active         BIT NOT NULL CONSTRAINT DF_tbl_sterilizer_no_is_active DEFAULT (1),

      created_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_sterilizer_no_created_at DEFAULT (SYSUTCDATETIME()),
      created_by        INT NULL,
      updated_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_sterilizer_no_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by        INT NULL,
      row_version       ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.tbl_sterilizer_no
      ADD CONSTRAINT UQ_tbl_sterilizer_no_sterilizer_no UNIQUE (sterilizer_no);

    ALTER TABLE dbo.tbl_sterilizer_no
      ADD CONSTRAINT CK_tbl_sterilizer_no_len CHECK (LEN(sterilizer_no) BETWEEN 1 AND 32);

    ALTER TABLE dbo.tbl_sterilizer_no
      ADD CONSTRAINT FK_tbl_sterilizer_no_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.tbl_sterilizer_no
      ADD CONSTRAINT FK_tbl_sterilizer_no_updated_by FOREIGN KEY (updated_by) REFERENCES dbo.tbl_account_login(account_id);
  END

  IF OBJECT_ID(N'dbo.tbl_departments', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_departments (
      department_id     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_departments PRIMARY KEY,
      department_code   NVARCHAR(16) NOT NULL,
      department_name   NVARCHAR(128) NOT NULL,
      is_active         BIT NOT NULL CONSTRAINT DF_tbl_departments_is_active DEFAULT (1),

      created_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_departments_created_at DEFAULT (SYSUTCDATETIME()),
      created_by        INT NULL,
      updated_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_departments_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by        INT NULL,
      row_version       ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.tbl_departments
      ADD CONSTRAINT UQ_tbl_departments_code UNIQUE (department_code);
    ALTER TABLE dbo.tbl_departments
      ADD CONSTRAINT UQ_tbl_departments_name UNIQUE (department_name);

    ALTER TABLE dbo.tbl_departments
      ADD CONSTRAINT CK_tbl_departments_code_len CHECK (LEN(department_code) BETWEEN 1 AND 16);

    ALTER TABLE dbo.tbl_departments
      ADD CONSTRAINT FK_tbl_departments_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.tbl_departments
      ADD CONSTRAINT FK_tbl_departments_updated_by FOREIGN KEY (updated_by) REFERENCES dbo.tbl_account_login(account_id);
  END

  IF OBJECT_ID(N'dbo.tbl_dept_items', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_dept_items (
      dept_item_id      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_dept_items PRIMARY KEY,
      department_id     INT NOT NULL,
      item_code         NVARCHAR(32) NOT NULL,
      item_name         NVARCHAR(256) NOT NULL,
      default_pcs       INT NULL,
      default_qty       INT NULL,
      is_active         BIT NOT NULL CONSTRAINT DF_tbl_dept_items_is_active DEFAULT (1),

      created_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_dept_items_created_at DEFAULT (SYSUTCDATETIME()),
      created_by        INT NULL,
      updated_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_dept_items_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by        INT NULL,
      row_version       ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.tbl_dept_items
      ADD CONSTRAINT FK_tbl_dept_items_department
      FOREIGN KEY (department_id) REFERENCES dbo.tbl_departments(department_id);

    /* Item codes are unique within a department */
    ALTER TABLE dbo.tbl_dept_items
      ADD CONSTRAINT UQ_tbl_dept_items_department_item_code UNIQUE (department_id, item_code);

    ALTER TABLE dbo.tbl_dept_items
      ADD CONSTRAINT CK_tbl_dept_items_pcs_qty CHECK (
        (default_pcs IS NULL OR default_pcs >= 0) AND
        (default_qty IS NULL OR default_qty >= 0)
      );

    ALTER TABLE dbo.tbl_dept_items
      ADD CONSTRAINT FK_tbl_dept_items_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.tbl_dept_items
      ADD CONSTRAINT FK_tbl_dept_items_updated_by FOREIGN KEY (updated_by) REFERENCES dbo.tbl_account_login(account_id);
  END

  IF OBJECT_ID(N'dbo.tbl_doctors_rooms', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_doctors_rooms (
      doctor_room_id    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_doctors_rooms PRIMARY KEY,
      doctor_name       NVARCHAR(128) NOT NULL,
      room              NVARCHAR(64) NULL,
      is_active         BIT NOT NULL CONSTRAINT DF_tbl_doctors_rooms_is_active DEFAULT (1),

      created_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_doctors_rooms_created_at DEFAULT (SYSUTCDATETIME()),
      created_by        INT NULL,
      updated_at        DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_doctors_rooms_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by        INT NULL,
      row_version       ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.tbl_doctors_rooms
      ADD CONSTRAINT UQ_tbl_doctors_rooms_doctor_room UNIQUE (doctor_name, room);

    ALTER TABLE dbo.tbl_doctors_rooms
      ADD CONSTRAINT FK_tbl_doctors_rooms_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.tbl_doctors_rooms
      ADD CONSTRAINT FK_tbl_doctors_rooms_updated_by FOREIGN KEY (updated_by) REFERENCES dbo.tbl_account_login(account_id);
  END

  /* ---------- Sterilization core ---------- */
  IF OBJECT_ID(N'dbo.tbl_sterilization', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_sterilization (
      sterilization_id    INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_sterilization PRIMARY KEY,
      cycle_no            NVARCHAR(32) NOT NULL,
      sterilizer_id       INT NOT NULL,
      sterilization_type  NVARCHAR(32) NOT NULL,
      cycle_program       NVARCHAR(40) NULL,
      cycle_datetime      DATETIME2(0) NOT NULL,
      operator_name       NVARCHAR(128) NOT NULL,
      temperature_c       DECIMAL(6,2) NULL,
      pressure            DECIMAL(8,3) NULL,
      bi_result           NVARCHAR(32) NULL,
      cycle_status        NVARCHAR(32) NOT NULL,
      doctor_room_id      INT NULL,
      implants            BIT NOT NULL CONSTRAINT DF_tbl_sterilization_implants DEFAULT (0),
      notes               NVARCHAR(4000) NULL,

      created_at          DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_sterilization_created_at DEFAULT (SYSUTCDATETIME()),
      created_by          INT NULL,
      updated_at          DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_sterilization_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by          INT NULL,
      row_version         ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.tbl_sterilization
      ADD CONSTRAINT UQ_tbl_sterilization_cycle_no UNIQUE (cycle_no);

    ALTER TABLE dbo.tbl_sterilization
      ADD CONSTRAINT FK_tbl_sterilization_sterilizer
      FOREIGN KEY (sterilizer_id) REFERENCES dbo.tbl_sterilizer_no(sterilizer_id);

    ALTER TABLE dbo.tbl_sterilization
      ADD CONSTRAINT FK_tbl_sterilization_doctor_room
      FOREIGN KEY (doctor_room_id) REFERENCES dbo.tbl_doctors_rooms(doctor_room_id);

    ALTER TABLE dbo.tbl_sterilization
      ADD CONSTRAINT FK_tbl_sterilization_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.tbl_sterilization
      ADD CONSTRAINT FK_tbl_sterilization_updated_by FOREIGN KEY (updated_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.tbl_sterilization
      ADD CONSTRAINT CK_tbl_sterilization_status CHECK (cycle_status IN (N'Draft', N'Completed', N'Voided'));

    CREATE INDEX IX_tbl_sterilization_datetime ON dbo.tbl_sterilization(cycle_datetime DESC);
    CREATE INDEX IX_tbl_sterilization_sterilizer ON dbo.tbl_sterilization(sterilizer_id, cycle_datetime DESC);
    CREATE INDEX IX_tbl_sterilization_status ON dbo.tbl_sterilization(cycle_status, cycle_datetime DESC);
  END

  IF OBJECT_ID(N'dbo.tbl_str_items', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.tbl_str_items (
      sterilization_item_id  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_str_items PRIMARY KEY,
      sterilization_id       INT NOT NULL,
      dept_item_id           INT NULL,
      department_name        NVARCHAR(256) NULL,
      doctor_or_room         NVARCHAR(256) NULL,
      item_name              NVARCHAR(256) NOT NULL,
      pcs                    INT NOT NULL CONSTRAINT DF_tbl_str_items_pcs DEFAULT (1),
      qty                    INT NOT NULL,

      created_at             DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_str_items_created_at DEFAULT (SYSUTCDATETIME()),
      created_by             INT NULL,
      updated_at             DATETIME2(0) NOT NULL CONSTRAINT DF_tbl_str_items_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by             INT NULL,
      row_version            ROWVERSION NOT NULL,

      /* Table-level CHECKs here: separate ALTER CHECK(pcs) in the same batch can compile before CREATE columns exist (Msg 207). */
      CONSTRAINT CK_tbl_str_items_qty CHECK (qty > 0),
      CONSTRAINT CK_tbl_str_items_pcs CHECK (pcs > 0)
    );

    ALTER TABLE dbo.tbl_str_items
      ADD CONSTRAINT FK_tbl_str_items_sterilization
      FOREIGN KEY (sterilization_id) REFERENCES dbo.tbl_sterilization(sterilization_id);

    ALTER TABLE dbo.tbl_str_items
      ADD CONSTRAINT FK_tbl_str_items_dept_item
      FOREIGN KEY (dept_item_id) REFERENCES dbo.tbl_dept_items(dept_item_id);

    ALTER TABLE dbo.tbl_str_items
      ADD CONSTRAINT FK_tbl_str_items_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.tbl_str_items
      ADD CONSTRAINT FK_tbl_str_items_updated_by FOREIGN KEY (updated_by) REFERENCES dbo.tbl_account_login(account_id);

    CREATE INDEX IX_tbl_str_items_sterilization ON dbo.tbl_str_items(sterilization_id);
  END

  /* ---------- QA tests ---------- */
  IF OBJECT_ID(N'dbo.qa_tests', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.qa_tests (
      qa_test_id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_qa_tests PRIMARY KEY,
      sterilization_id  INT NOT NULL,
      test_type         NVARCHAR(32) NOT NULL,
      test_datetime     DATETIME2(0) NOT NULL,
      result            NVARCHAR(32) NOT NULL,
      measured_value    DECIMAL(10,3) NULL,
      unit              NVARCHAR(16) NULL,
      notes             NVARCHAR(2000) NULL,
      performed_by      NVARCHAR(128) NULL,

      created_at        DATETIME2(0) NOT NULL CONSTRAINT DF_qa_tests_created_at DEFAULT (SYSUTCDATETIME()),
      created_by        INT NULL,
      updated_at        DATETIME2(0) NOT NULL CONSTRAINT DF_qa_tests_updated_at DEFAULT (SYSUTCDATETIME()),
      updated_by        INT NULL,
      row_version       ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.qa_tests
      ADD CONSTRAINT FK_qa_tests_sterilization
      FOREIGN KEY (sterilization_id) REFERENCES dbo.tbl_sterilization(sterilization_id);

    ALTER TABLE dbo.qa_tests
      ADD CONSTRAINT CK_qa_tests_type CHECK (test_type IN (N'Leak', N'BowieDick'));

    ALTER TABLE dbo.qa_tests
      ADD CONSTRAINT CK_qa_tests_result CHECK (result IN (N'Pass', N'Fail'));

    ALTER TABLE dbo.qa_tests
      ADD CONSTRAINT FK_qa_tests_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.qa_tests
      ADD CONSTRAINT FK_qa_tests_updated_by FOREIGN KEY (updated_by) REFERENCES dbo.tbl_account_login(account_id);

    CREATE INDEX IX_qa_tests_sterilization ON dbo.qa_tests(sterilization_id, test_datetime DESC);
    CREATE INDEX IX_qa_tests_type_datetime ON dbo.qa_tests(test_type, test_datetime DESC);
  END

  /* ---------- File attachments (receipts) ---------- */
  IF OBJECT_ID(N'dbo.cycle_receipts', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.cycle_receipts (
      receipt_id        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_cycle_receipts PRIMARY KEY,
      sterilization_id  INT NOT NULL,
      file_path         NVARCHAR(400) NOT NULL,
      file_name         NVARCHAR(260) NOT NULL,
      content_type      NVARCHAR(64) NOT NULL,
      file_size_bytes   BIGINT NOT NULL,
      sha256            CHAR(64) NULL,
      captured_at       DATETIME2(0) NOT NULL CONSTRAINT DF_cycle_receipts_captured_at DEFAULT (SYSUTCDATETIME()),
      captured_by       INT NULL,

      created_at        DATETIME2(0) NOT NULL CONSTRAINT DF_cycle_receipts_created_at DEFAULT (SYSUTCDATETIME()),
      created_by        INT NULL,
      row_version       ROWVERSION NOT NULL
    );

    ALTER TABLE dbo.cycle_receipts
      ADD CONSTRAINT FK_cycle_receipts_sterilization
      FOREIGN KEY (sterilization_id) REFERENCES dbo.tbl_sterilization(sterilization_id);

    ALTER TABLE dbo.cycle_receipts
      ADD CONSTRAINT FK_cycle_receipts_captured_by FOREIGN KEY (captured_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.cycle_receipts
      ADD CONSTRAINT FK_cycle_receipts_created_by FOREIGN KEY (created_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.cycle_receipts
      ADD CONSTRAINT UQ_cycle_receipts_sterilization_path UNIQUE (sterilization_id, file_path);

    ALTER TABLE dbo.cycle_receipts
      ADD CONSTRAINT CK_cycle_receipts_size CHECK (file_size_bytes > 0);

    CREATE INDEX IX_cycle_receipts_sterilization ON dbo.cycle_receipts(sterilization_id, captured_at DESC);
  END

  /* ---------- Audit and print logs ---------- */
  IF OBJECT_ID(N'dbo.audit_logs', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.audit_logs (
      audit_id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_audit_logs PRIMARY KEY,
      event_at          DATETIME2(0) NOT NULL CONSTRAINT DF_audit_logs_event_at DEFAULT (SYSUTCDATETIME()),
      actor_account_id  INT NULL,
      module            NVARCHAR(64) NOT NULL,
      entity_name       NVARCHAR(64) NOT NULL,
      entity_id         NVARCHAR(64) NOT NULL,
      action            NVARCHAR(96) NOT NULL,
      old_values_json   NVARCHAR(MAX) NULL,
      new_values_json   NVARCHAR(MAX) NULL,
      client_machine    NVARCHAR(64) NULL,
      correlation_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_audit_logs_correlation DEFAULT (NEWID())
    );

    ALTER TABLE dbo.audit_logs
      ADD CONSTRAINT FK_audit_logs_actor FOREIGN KEY (actor_account_id) REFERENCES dbo.tbl_account_login(account_id);

    CREATE INDEX IX_audit_logs_event_at ON dbo.audit_logs(event_at DESC);
    CREATE INDEX IX_audit_logs_entity ON dbo.audit_logs(entity_name, entity_id, event_at DESC);
    CREATE INDEX IX_audit_logs_actor ON dbo.audit_logs(actor_account_id, event_at DESC);
    CREATE INDEX IX_audit_logs_module ON dbo.audit_logs(module, event_at DESC);
  END

  IF OBJECT_ID(N'dbo.print_logs', N'U') IS NULL
  BEGIN
    CREATE TABLE dbo.print_logs (
      print_log_id      BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_print_logs PRIMARY KEY,
      printed_at        DATETIME2(0) NOT NULL CONSTRAINT DF_print_logs_printed_at DEFAULT (SYSUTCDATETIME()),
      printed_by        INT NULL,
      report_type       NVARCHAR(64) NOT NULL,
      sterilization_id  INT NULL,
      qa_test_id        INT NULL,
      printer_name      NVARCHAR(256) NULL,
      copies            INT NOT NULL CONSTRAINT DF_print_logs_copies DEFAULT (1),
      parameters_json   NVARCHAR(MAX) NULL,
      correlation_id    UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_print_logs_correlation DEFAULT (NEWID())
    );

    ALTER TABLE dbo.print_logs
      ADD CONSTRAINT FK_print_logs_printed_by FOREIGN KEY (printed_by) REFERENCES dbo.tbl_account_login(account_id);

    ALTER TABLE dbo.print_logs
      ADD CONSTRAINT FK_print_logs_sterilization FOREIGN KEY (sterilization_id) REFERENCES dbo.tbl_sterilization(sterilization_id);

    ALTER TABLE dbo.print_logs
      ADD CONSTRAINT FK_print_logs_qa_test FOREIGN KEY (qa_test_id) REFERENCES dbo.qa_tests(qa_test_id);

    ALTER TABLE dbo.print_logs
      ADD CONSTRAINT CK_print_logs_copies CHECK (copies >= 1 AND copies <= 20);

    CREATE INDEX IX_print_logs_printed_at ON dbo.print_logs(printed_at DESC);
    CREATE INDEX IX_print_logs_report_type ON dbo.print_logs(report_type, printed_at DESC);
  END

  /* ---------- Helpful views (optional) ---------- */
  IF OBJECT_ID(N'dbo.vw_sterilization_latest_receipt', N'V') IS NULL
  BEGIN
    EXEC(N'
      CREATE VIEW dbo.vw_sterilization_latest_receipt
      AS
      SELECT
        s.sterilization_id,
        s.cycle_no,
        r.receipt_id,
        r.file_path,
        r.content_type,
        r.captured_at
      FROM dbo.tbl_sterilization s
      OUTER APPLY (
        SELECT TOP (1) *
        FROM dbo.cycle_receipts r
        WHERE r.sterilization_id = s.sterilization_id
        ORDER BY r.captured_at DESC, r.receipt_id DESC
      ) r;
    ');
  END

  COMMIT TRANSACTION;
END TRY
BEGIN CATCH
  IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
  DECLARE @msg NVARCHAR(4000) = ERROR_MESSAGE();
  RAISERROR(N'HSMS schema init failed: %s', 16, 1, @msg);
END CATCH;

