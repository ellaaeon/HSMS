-- Adds incubator checkbox to BI log sheet QA form.
-- Run on the HSMS database.
USE [HSMS];
IF COL_LENGTH('dbo.tbl_sterilization', 'bi_incubator_checked') IS NULL
BEGIN
    ALTER TABLE dbo.tbl_sterilization
        ADD bi_incubator_checked bit NULL;
END

