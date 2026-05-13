# HSMS Operations SOP

This SOP documents the routine operational tasks introduced by Module 8
("Operations Hardening") and the manual recovery hooks available to administrators.

## 1. Scheduled jobs (HSMS.Api)

The API hosts `MaintenanceHostedService` which runs in-process. Cadence is configurable via
`appsettings.json` (`Maintenance` section):

```jsonc
{
  "Maintenance": {
    "ReconciliationEnabled": true,
    "CleanupEnabled": true,
    "SchedulerInterval": "01:00:00",
    "ReconciliationInterval": "1.00:00:00",
    "CleanupInterval": "7.00:00:00"
  }
}
```

| Job | Purpose | Cadence |
| --- | --- | --- |
| Receipt reconciliation | Walks `cycle_receipts` + `cycle_receipt_assets` and detects (a) DB rows whose files are missing on disk and (b) files on disk that no DB row references. | Daily |
| Derived asset cleanup | Removes orphan PNG previews/thumbnails under `*/_derived` directories that no longer have a database row. Originals are never removed automatically. | Weekly |

Both jobs log structured events:

- `Receipt reconciliation complete. total=… missingOriginals=… missingDerived=… orphans=…`
- `Removed N orphan derived files from <ReceiptsRootPath>`

## 2. Ad-hoc operations API

Admins can trigger jobs immediately via HTTPS:

| Method | Path | Description |
| --- | --- | --- |
| `POST` | `/api/operations/reconcile-receipts` | Returns `ReceiptReconciliationResult` JSON with findings. |
| `POST` | `/api/operations/cleanup-derived` | Returns `{ deleted: <count> }`. |

Both endpoints require an `Admin` token (403 otherwise).

## 3. Backup / restore checklist

1. **Database backup** — schedule SQL Server `FULL` backups + transaction log backups per institution policy. The HSMS API is stateless and does not embed backup logic.
2. **Receipt files** — back up `Storage:ReceiptsRootPath` separately (originals + `_derived/`). Use `robocopy` or storage snapshots.
3. **Restore drill (recommended quarterly)**:
   - Restore DB to a staging server.
   - Restore receipt files to the same `Storage:ReceiptsRootPath` layout.
   - Boot HSMS.Api against the staging DB.
   - Run `POST /api/operations/reconcile-receipts` and verify `MissingOriginals == 0`.
   - Open a few cycles in HSMS.Desktop and confirm receipt previews render.

## 4. Manual recovery cheatsheet

- **Print job stuck "Failed"** — check Print history viewer, identify Correlation Id, retry from desktop ▸ Print history (or rerun the original action; idempotency key prevents duplicates).
- **Receipt preview missing** — re-upload (PDF→PNG derivation will be re-queued automatically) or run `POST /api/operations/reconcile-receipts` to detect and `cleanup-derived` to clear stale previews.
- **Master data soft-deleted by mistake** — use admin SQL (`UPDATE … SET is_active=1, disabled_at=NULL, disabled_by=NULL`) or extend the masters CRUD UI to allow re-enable.

## 5. Monitoring suggestions

- Tail HSMS.Api logs (`Microsoft.Extensions.Logging`) for `MaintenanceHostedService` warnings.
- Configure a periodic CSV/Excel export from the Audit + Print history viewers and store offsite.
- For 24x7 sites, alert on `Reconciliation findings detected: N entries` log line.
