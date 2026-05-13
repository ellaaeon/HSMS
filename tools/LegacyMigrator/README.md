# HSMS Legacy Migrator

Console ETL tool that imports data from a legacy HSMS database (`hsms_db` or compatible)
into the new HSMS schema. Designed to be **idempotent** (safe to re-run) and supports
**dry-run** validation, **logs**, **mapping tables**, and **rollback**.

## Quick start

```powershell
cd tools/LegacyMigrator

# 1. Apply staging schema on TARGET HSMS database
sqlcmd -S . -d HSMS -i ddl/001_migration_staging.sql

# 2. Edit appsettings.json (Legacy + Hsms connection strings)

# 3. Validate everything in dry-run (no writes)
dotnet run -- --dry-run

# 4. Apply for real
dotnet run -- --apply

# 5. (If needed) rollback a specific run
dotnet run -- --rollback --run=<GUID>
```

## What gets migrated

| Step | Source (legacy)                               | Target (HSMS)                |
|------|-----------------------------------------------|------------------------------|
| 1    | `tbl_sterilizer_no` / `sterilizers`           | `dbo.tbl_sterilizer_no`      |
| 2    | `tbl_departments` / `departments`             | `dbo.tbl_departments`        |
| 3    | `tbl_doctors_rooms` / `doctors_rooms`         | `dbo.tbl_doctors_rooms`      |
| 4    | `tbl_sterilization` / `sterilizations`        | `dbo.tbl_sterilization`      |
| 5    | `qa_tests`                                    | `dbo.qa_tests`               |

Each step:
- Detects the source table shape automatically (multiple legacy column conventions).
- Validates required fields and emits findings (`Info`/`Warning`/`Error`) with codes.
- Writes idempotent mappings to `dbo.migration_mappings` so subsequent runs are no-ops.
- Logs every action to `logs/migration_<runId>.log`.
- Produces a per-run Markdown report in `reports/migration_<runId>.md`.

## Idempotency model

- Each (entity, legacy_id) pair has at most one row in `dbo.migration_mappings`.
- Steps look up the legacy id in the mapping cache before doing any work; if found, they
  skip cleanly without reinserting.
- Re-running after a failed step resumes from where the previous run left off.

## Rollback

`--rollback --run=<GUID>` walks `dbo.migration_mappings` in reverse FK-safe order
(QaTests -> CycleReceipts -> Sterilizations -> DoctorsRooms -> Departments -> Sterilizers),
deletes inserted rows, and clears the mapping rows for that run. The corresponding
`migration_runs.status` is set to `RolledBack`.

> **Note**: rollback does NOT remove receipt files on disk. Restore the original
> `Storage:ReceiptsRootPath` snapshot per `docs/OPERATIONS_SOP.md`.

## Adding a new step

1. Add a class in `Steps/` implementing `IMigrationStep`.
2. Append it to the `steps` array in `Program.cs` (respect FK ordering).
3. Add a corresponding entry to `Rollback.DeletionOrder` in reverse order.
4. Use `MappingStore.TryGet(parentEntity, legacyParentId, ...)` to translate FKs.

## Safety checklist

- [ ] Take a full SQL backup of the target HSMS database.
- [ ] Run `--dry-run` first; review the report and findings.
- [ ] Review `migration_findings` for `Error` rows; fix legacy data if needed.
- [ ] Run `--apply` during a maintenance window.
- [ ] Verify counts via the Markdown report and `migration_runs.summary_json`.
- [ ] Smoke test a few sterilization cycles + QA tests in the desktop app.
