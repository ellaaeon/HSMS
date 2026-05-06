## HSMS File + Audit + Safety Spec (offline LAN)

This document defines the operational rules that keep HSMS safe during power loss, multi-user concurrency, and compliance audits.

---

## 1) Receipt file storage (server-local only)

### Root folder (mandatory)
- `D:\HSMS\Receipts\`

### Subfolders (recommended)
- Year: `D:\HSMS\Receipts\YYYY\`
- Optional month for high volume: `D:\HSMS\Receipts\YYYY\MM\`
- Temp: `D:\HSMS\Receipts\_tmp\` (must be on **same volume** as final destination for atomic move)

### Allowed formats
- JPG, PNG, PDF (exactly as required)

### Naming format (collision-safe)
Use a GUID suffix to avoid collisions under concurrent uploads:
- `cycle_{cycleNo}_{yyyyMMdd_HHmmss}_{guid}.{ext}`

Examples
- `cycle_12345_20260429_003012_6c0f6d5f9c7b4a1a9ce0f3a2f3a1d2c3.jpg`

### Database storage rule (mandatory)
- Database stores **ONLY**:
  - server file path (`cycle_receipts.file_path`)
  - file metadata (name/content type/size/hash/time/user)
- Database never stores file content blobs.

---

## 2) Upload / attach receipt flow (power-failure safe)

### Design goals
- No partial/corrupt files in final folders.
- No DB rows referencing files that were never finalized.
- Safe under concurrent multi-user uploads.
- Works offline; no external services.

### Atomic write pattern (mandatory)
1. **Validate request**:
   - extension in {`.jpg`, `.png`, `.pdf`}
   - content type matches extension (best-effort)
   - size limit (configurable; e.g., 20–50 MB)
2. **Build final destination path**:
   - pick `YYYY` (and optionally `MM`) from captured time (or now)
   - compute final file name using the required naming scheme
3. **Stream upload to temp file**:
   - write to `D:\HSMS\Receipts\_tmp\{guid}.uploading`
   - flush stream to disk
   - compute `SHA-256` while streaming (optional but recommended for integrity checks)
4. **DB transaction**:
   - verify `sterilization_id` exists and is not Voided
   - insert `cycle_receipts` row with **final** `file_path`
   - insert `audit_logs` action=Create (module=Sterilization, entity=cycle_receipts)
   - commit
5. **Finalize file**:
   - move/rename temp file to final `file_path` using an **atomic move** (same volume)
6. **Post-condition check**:
   - if move succeeded, operation is complete
   - if move failed after DB commit:
     - API returns error
     - system must remediate via the **reconciliation job** below

### Why DB-before-move is acceptable here
Either ordering has edge cases under sudden power loss. We handle it with reconciliation:
- **DB row exists but file missing**: detected and fixed by reconciliation (most common if move fails).
- **File exists but DB row missing**: detected and optionally auto-imported or cleaned up.

---

## 3) Reconciliation / cleanup (server operation)

### Purpose
Recover from power loss or interrupted uploads without manual SQL work.

### Minimum requirements
Provide an **admin tool** (API endpoint or scheduled service) that:
- scans `D:\HSMS\Receipts\_tmp\` and deletes any `*.uploading` older than N hours (e.g., 24h)
- scans `cycle_receipts` rows and checks file existence:
  - if missing: mark as missing (recommended) OR delete row (not recommended if audit required)

### Recommended DB enhancement (optional)
If you want explicit state tracking, add:
- `cycle_receipts.is_missing BIT DEFAULT 0`
- `cycle_receipts.missing_checked_at DATETIME2 NULL`

If you keep schema as-is, missing files are handled at runtime:
- show “Receipt file not found on server” and allow re-upload
- log an audit entry for the access failure (optional)

---

## 4) PDF handling for printing (derived PNG strategy)

### Requirement
RDLC needs an image for Page 2.

### Mandatory behavior
- If uploaded receipt is **PDF**:
  - keep original PDF stored as normal
  - generate a derived PNG for printing (at upload time or on first print)
  - store derived PNG as an additional `cycle_receipts` row linked to the same cycle

### Printing selection rule
- For “Receipt page” rendering, prefer the **most recent image receipt** (jpg/png).
- If none exists, use the derived PNG from PDF.

---

## 5) Multi-user concurrency + transaction safety

### Core rules (mandatory)
- All create/update operations that touch multiple tables must be **single API calls** wrapped in **one SQL transaction**.
  - Example: update cycle + replace items list → one transaction.
- Use **ROWVERSION** concurrency:
  - client submits `rowVersion` on update
  - server rejects mismatched versions with 409 `CONCURRENCY_CONFLICT`
- Enforce uniqueness in DB:
  - `tbl_sterilization.cycle_no` unique index (already in DDL)

### Concurrency behavior (UX requirement)
On 409, WPF message must be:
- “Someone updated this record. Press F5 to reload.”
Then set focus to F5/Refresh or auto-reload if safe.

---

## 6) Audit logging (mandatory)

### What must be logged
For every Create/Update/Delete (soft delete) in these modules:
- Authentication
- Sterilization cycles + items
- QA tests
- Receipts metadata (attach action)
- Maintenance/master data changes
- Reporting/printing actions

### Minimum audit fields
Captured in `audit_logs`:
- `event_at` (UTC)
- `actor_account_id`
- `module`
- `entity_name`
- `entity_id` (PK as string)
- `action` (Create/Update/Delete/Print/Login/Logout)
- `old_values_json` and `new_values_json` (for Update)
- `client_machine`
- `correlation_id` (one per request/operation)

### Old vs new values rules
- **Create**: `old_values_json` null, `new_values_json` contains created DTO snapshot.
- **Update**: include only changed fields OR full snapshot (choose one approach and keep consistent).
  - Recommended: **changed fields only** for readability.
- **Delete / Deactivate**: store pre-delete snapshot in `old_values_json`, store `{ "isActive": false }` in `new_values_json`.

### Correlation ID rules
- API generates a single `correlation_id` per request.
- All audit logs and print logs written as part of that request share it.

---

## 7) Print logging (mandatory)

### Rule
Every report print must be recorded, even if printing happens on the workstation.

### Flow
1. WPF renders RDLC and prints locally.
2. After successful print, WPF calls `POST /print-logs` including:
   - report type
   - entity ids (cycle and/or qa test)
   - printer name
   - copies
   - parameters used
   - client machine
3. API writes:
   - `print_logs` row
   - `audit_logs` action=Print (module=Reporting)

---

## 8) Operational hardening (offline LAN)

### Backups (SOP requirement)
- Daily SQL backup to offline storage (USB/external drive) + receipts folder copy.
- Keep at least:
  - 7 daily backups
  - 4 weekly backups
  - 12 monthly backups (if required by hospital policy)

### Permissions (server)
- API service account has read/write to `D:\HSMS\Receipts\`
- Clients have **no direct** filesystem access to the receipts folder.

