-- Minimal database bootstrap to let hsms.exe open screens.
-- Creates hsms_db + core tables referenced by the application.
--
-- Run in SSMS connected to (local)\\SQLEXPRESS (or your SQL Server).

IF DB_ID(N'hsms_db') IS NULL
BEGIN
    CREATE DATABASE hsms_db;
END
GO

USE hsms_db;
GO

-- Accounts
IF OBJECT_ID(N'dbo.tbl_account_login', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_account_login
    (
        id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_account_login PRIMARY KEY,
        username      NVARCHAR(100) NOT NULL,
        password_hash NVARCHAR(128) NOT NULL,
        role          NVARCHAR(50)  NULL,
        fullname      NVARCHAR(200) NULL,
        created_at    DATETIME2(0)  NOT NULL CONSTRAINT DF_tbl_account_login_created_at DEFAULT (SYSUTCDATETIME()),
        is_active     BIT           NOT NULL CONSTRAINT DF_tbl_account_login_is_active DEFAULT (1)
    );

    CREATE UNIQUE INDEX UX_tbl_account_login_username ON dbo.tbl_account_login(username);
END
GO

-- Reference data
IF OBJECT_ID(N'dbo.tbl_departments', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_departments
    (
        id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_departments PRIMARY KEY,
        Department NVARCHAR(200) NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.tbl_dept_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_dept_items
    (
        id            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_dept_items PRIMARY KEY,
        department_id INT NOT NULL,
        Item          NVARCHAR(300) NOT NULL,
        CONSTRAINT FK_tbl_dept_items_department_id
            FOREIGN KEY (department_id) REFERENCES dbo.tbl_departments(id)
    );
END
GO

IF OBJECT_ID(N'dbo.tbl_doctors_rooms', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_doctors_rooms
    (
        id         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_doctors_rooms PRIMARY KEY,
        name       NVARCHAR(200) NOT NULL,
        department NVARCHAR(200) NOT NULL
    );
END
GO

IF OBJECT_ID(N'dbo.tbl_sterilizer_no', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_sterilizer_no
    (
        SterilizerID   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_sterilizer_no PRIMARY KEY,
        SterilizerNo   NVARCHAR(100) NOT NULL,
        Model          NVARCHAR(200) NULL,
        Manufacturer   NVARCHAR(200) NULL,
        PurchaseDate   DATE NULL
    );
END
GO

-- Main table used by most screens/reports
IF OBJECT_ID(N'dbo.tbl_sterilization', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_sterilization
    (
        id                     INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_sterilization PRIMARY KEY,

        sterilization_type     NVARCHAR(50)  NULL,
        datetime               DATETIME      NULL,
        operator               NVARCHAR(200) NULL,
        cycle_no               NVARCHAR(100) NULL,
        sterilizer_no          NVARCHAR(100) NULL,
        bi                     NVARCHAR(10)  NULL,
        temperature            INT           NULL,
        bi_result              NVARCHAR(50)  NULL,
        cycle_status           NVARCHAR(50)  NULL,
        cycle_time_completion  DATETIME      NULL,
        updated_by             NVARCHAR(200) NULL,
        test_cycle             NVARCHAR(50)  NULL,
        cycle_test_result_file NVARCHAR(500) NULL,
        test_result            NVARCHAR(50)  NULL,
        doctor                 NVARCHAR(200) NULL,

        -- load record / BI log fields seen in embedded SQL
        implants               NVARCHAR(50)  NULL,
        bi_lot_no              NVARCHAR(100) NULL,
        exposure_time          NVARCHAR(50)  NULL,
        duration               NVARCHAR(50)  NULL,
        leak_rate              NVARCHAR(50)  NULL,

        bi_time_in             NVARCHAR(50)  NULL,
        bi_time_out            NVARCHAR(50)  NULL,
        bi_proc_24min          NVARCHAR(50)  NULL,
        bi_proc_24hrs          NVARCHAR(50)  NULL,
        bi_contrl_24mins       NVARCHAR(50)  NULL,
        bi_contrl_24hrs        NVARCHAR(50)  NULL,
        bi_operator            NVARCHAR(200) NULL,
        comments               NVARCHAR(MAX) NULL,

        time_in_initial        NVARCHAR(50)  NULL,
        time_out_initial       NVARCHAR(50)  NULL,

        load_qty               INT           NULL
    );
END
GO

IF OBJECT_ID(N'dbo.tbl_str_items', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.tbl_str_items
    (
        id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_tbl_str_items PRIMARY KEY,
        strlzation_id   INT NOT NULL,
        department      NVARCHAR(200) NULL,
        item_description NVARCHAR(500) NULL,
        pcs             INT NULL,
        qty             INT NULL,
        CONSTRAINT FK_tbl_str_items_strlzation_id
            FOREIGN KEY (strlzation_id) REFERENCES dbo.tbl_sterilization(id)
    );
END
GO

-- Create a default admin account if none exists.
-- Password is: admin123 (SHA-256 hex)
IF NOT EXISTS (SELECT 1 FROM dbo.tbl_account_login)
BEGIN
    INSERT INTO dbo.tbl_account_login (username, password_hash, role, fullname, created_at, is_active)
    VALUES
    (
        N'admin',
        N'240BE518FABD2724DDB6F04EEB1DA5967448D7E831C08C8FA822809F74C720A9',
        N'Admin',
        N'Administrator',
        SYSUTCDATETIME(),
        1
    );
END
GO

