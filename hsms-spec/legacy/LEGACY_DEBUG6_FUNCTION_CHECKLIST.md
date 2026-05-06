# Legacy `Debug (6)` inventory → new HSMS implementation checklist

This document is derived from scanning **`C:\Users\adiza\HSMS\Debug (6)\Debug\`**: shipped binaries, RDLC reports, `hsms.exe.config`, `setup_hsms_db.sql`, and dependency DLLs. There is **no legacy source code** in that folder, so **screen-level names** are inferred from artifacts + schema; treat this as a **parity backlog** to validate with staff who use `hsms.exe` daily.

---

## 1) What the legacy build actually ships (evidence)

| Artifact | What it implies |
|----------|------------------|
| `hsms.exe` + `hsms.pdb` | .NET Framework **4.7.2** WPF (or WinForms hybrid) desktop client. |
| `hsms.exe.config` | `hsms_db` on `localhost`, `DownloadFolderPath`, 5s connect timeout. |
| `setup_hsms_db.sql` | Minimal **hsms_db** bootstrap: accounts, departments, dept items, doctors/rooms, sterilizers, **tbl_sterilization** (wide row), **tbl_str_items**. Default **admin** with **SHA-256** password hash. |
| **RDLC** | `BILogSheet.rdlc`, `BowieDick.rdlc`, `LeakTest.rdlc`, `LoadRecord.rdlc`, `LoadRecordTemp.rdlc` → five report outputs (one appears to be a draft/temp variant). |
| `Microsoft.ReportViewer.WinForms.dll` | **RDLC / ReportViewer** rendering (WinForms host; legacy may host reports in a viewer control). |
| `Xceed.Wpf.AvalonDock*.dll` | **Dockable panes / MDI-style** layout (multiple tools open at once). |
| `Xceed.Wpf.Toolkit.dll` | Rich WPF inputs (masked text, date/time, etc.). |
| `EPPlus.dll` | **Excel** read/write (export/import of logs or templates). |
| `BoldReports.WebForms.dll` | Bundled dependency (may or may not be used by this exe; note for parity if any “Bold” report exists in UI). |
| `Newtonsoft.Json.dll` | JSON for config/API/files — pattern for new app: `System.Text.Json` is fine. |
| `Microsoft.SqlServer.Types.dll` + `SqlServerTypes\` | SQL spatial / types support (often pulled in by ReportViewer or DB types). |
| `archived\*.jpg/png` | Sample / test **receipt or attachment** images (Bowie-Dick, scans) → parity: attach image/PDF to cycle and show on report page 2. |

---

## 2) Data / domain capabilities implied by `setup_hsms_db.sql`

Implement in the **new** app against the **`HSMS`** schema (normalized), with **migration** from legacy `hsms_db` where column names differ.

### 2.1 Security & users
- [ ] **Login** (legacy: `tbl_account_login` with `username`, `password_hash`, `role`, `fullname`, `is_active`).
- [ ] **Upgrade**: hash with **BCrypt/Argon2** (not SHA-256); optional **fullname** display; lockout / inactive messaging.
- [ ] **Admin**: create/edit/deactivate users (if legacy allowed it).

### 2.2 Master data
- [ ] **Departments** (`tbl_departments` — legacy column `Department`).
- [ ] **Department items** (`tbl_dept_items` — legacy `Item` per department).
- [ ] **Doctors / rooms** (`tbl_doctors_rooms` — legacy `name`, `department`).
- [ ] **Sterilizers** (`tbl_sterilizer_no` — legacy adds **Manufacturer**, **PurchaseDate**; new schema can extend entity + UI).
- [ ] **Upgrade**: instant search, keyboard-first grids, soft-delete / `is_active`, clear validation text.

### 2.3 Sterilization core (wide legacy row → map to new model)

Legacy `tbl_sterilization` packs many concerns in one table. New app should support the same **business outcomes**, using `tbl_sterilization` + `tbl_str_items` + `qa_tests` + `cycle_receipts` + audit/print logs.

**Identity / cycle**
- [ ] `sterilization_type`, `datetime`, `operator`, `cycle_no`, `sterilizer_no` (or FK), `cycle_status`, `cycle_time_completion`, `updated_by`.

**BI / indicators (may overlap QA module)**
- [ ] `bi`, `bi_result`, `bi_lot_no`, `exposure_time`, `duration`, `leak_rate`, `test_cycle`, `test_result`, `bi_operator`, BI time fields (`bi_time_in`, `bi_time_out`, `bi_proc_24min`, …), `comments`.

**Clinical / load record**
- [ ] `doctor`, `implants`, `load_qty`.

**Attachments**
- [ ] `cycle_test_result_file` (path string) → new: `cycle_receipts` + server disk path; still support **JPG/PNG/PDF**.

**Items (load list)**
- [ ] `tbl_str_items`: `department`, `item_description`, `pcs`, `qty`, link to sterilization.

**Upgrade**
- [ ] One **guided “Cycle” screen** with tabs or expanders: *General*, *Load*, *QA/BI*, *Attachments*, *Audit* — instead of one overwhelming grid of columns.
- [ ] **ENTER** flow, **F5** refresh, **F9** print, clear errors, defaults from last cycle / logged-in user.

---

## 3) Reporting (RDLC files in `Debug (6)`)

| Legacy RDLC | New app deliverable |
|---------------|---------------------|
| `LoadRecord.rdlc` (+ `LoadRecordTemp.rdlc`) | **Load Record** official report; decide whether **Temp** is draft watermark vs separate report → implement one production path + optional “draft”. |
| `BILogSheet.rdlc` | **BI log sheet** (date/cycle range, signatures). |
| `BowieDick.rdlc` | **Bowie–Dick** QA report (linked to cycle). |
| `LeakTest.rdlc` | **Leak test** QA report (linked to cycle). |

**Upgrade (all reports)**
- [ ] Page 1: structured fields from DB.
- [ ] Page 2: **receipt image** from stored path (see `RDLC_SPEC.md`); PDF → derived PNG if needed.
- [ ] **Print log** + **audit** on print.
- [ ] Printer choice remembered per station; large fonts; hospital header block configurable.

---

## 4) Non-report features implied by dependencies

| Dependency | Likely legacy behavior | New app |
|------------|-------------------------|---------|
| **AvalonDock** | Multiple documents/panels (e.g. cycle list + detail + reports). | Optional: docked **Cycle list | Detail | QA | Attachments**; keep simple default for non-technical users. |
| **Extended WPF Toolkit** | Date/time pickers, numeric editors, busy indicators. | Use modern WPF **Fluent** patterns; same keyboard-first rules as `WPF_UX_SPEC.md`. |
| **EPPlus** | Export **Excel** (BI log, load list, audit) or import templates. | Add **Export to .xlsx** where staff today use Excel; match column order they expect. |
| **DownloadFolderPath** | Default folder for saved exports/downloads. | Already mirrored in `appsettings`; wire every “Save file” dialog to it. |

---

## 5) Cross-cutting (production)

- [ ] **Offline LAN only** — no cloud; configurable **API base URL** per workstation.
- [ ] **Multi-user** — optimistic concurrency (`rowversion`), duplicate `cycle_no` prevention.
- [ ] **Audit** — who/when/old vs new (mandatory for compliance).
- [ ] **Backup / restore** SOP (SQL + receipt folder).
- [ ] **Installer** — xcopy or MSI; place `appsettings.json` per machine; service account for API + folder ACLs `D:\HSMS\Receipts`.

---

## 6) Suggested implementation order (upgraded UX)

1. **Sterilization cycle + items + sterilizer pick** (core path) — mostly started in new repo.  
2. **Receipt attach + preview** + API upload (parity with `cycle_test_result_file` / archived scans).  
3. **QA entry** (Leak + Bowie–Dick) bound to cycle — API exists; **WPF** + validation.  
4. **RDLC** four reports + migrate layouts from legacy `.rdlc` into new project (adjust data sets to new schema).  
5. **Master data CRUD** (departments, items, doctors/rooms, sterilizers incl. manufacturer/date).  
6. **Excel export** (EPPlus parity) for logs staff use today.  
7. **Optional docking** power layout (only if users ask; default stays simple).

---

## 7) Follow-up with you (to remove guesswork)

If you can provide **screenshots of every menu** in `hsms.exe` or a short **screen list** from the manual, this checklist can be turned into **named screens** with acceptance criteria per screen.
