# Legacy `hsms.exe.config` → new HSMS (.NET 8) parity

## What the old config actually contains

The legacy file `hsms.exe.config` in this repo only defines:

1. **.NET runtime** — .NET Framework 4.7.2 (`supportedRuntime`).
2. **SQL connection** — three equivalent connection strings, all:
   - `Data Source=localhost`
   - `Initial Catalog=hsms_db`
   - `Integrated Security=True`
   - `TrustServerCertificate=True`
   - `Connect Timeout=5`
3. **One app setting** — `DownloadFolderPath` → `C:\Users\adiza\Downloads`

There are **no feature flags or screen names** in that file. Business behavior lived inside **hsms.exe** (not in this repo), so feature parity is driven by your **CSSD requirements** and the **schema/spec** in `hsms-db` and `hsms-spec/`, not by the config XML alone.

## Direct mapping (implemented or documented)

| Legacy (`hsms.exe.config`) | New HSMS |
|----------------------------|----------|
| `connectionStrings` → `localhost` + **`hsms_db`** | API: `ConnectionStrings:SqlServer` in `src/HSMS.Api/appsettings.json`. This rewrite targets database **`HSMS`** with the new DDL. If your live legacy data is still in **`hsms_db`**, you need a **migration or DBA-approved** plan (same server, two DBs: do not confuse them). |
| `Connect Timeout=5` | Included on the sample `SqlServer` connection string in `appsettings.json`. |
| `DownloadFolderPath` | **API:** `Storage:DownloadFolderPath` in `appsettings.json` (for server-side exports / future use). **Desktop:** `DownloadFolderPath` in `src/HSMS.Desktop/appsettings.json` (for default folders when we wire file pickers / receipts the same way as legacy). |

## Functional parity (what the old app “did” vs this codebase)

From your product brief (and typical legacy CSSD tools), the old **hsms.exe** is expected to align with these **modules** — track each in Git/issues until done:

| Area | Status in this repo (high level) |
|------|----------------------------------|
| Login / roles | API + WPF login; dev seed in Development only |
| Sterilization cycles + items | API + cycle entry screen (improving) |
| Sterilizer master | API `GET /api/masters/sterilizers` + combo on desktop |
| QA (Leak / Bowie-Dick) | API endpoints exist; **WPF screen not built** |
| Receipts attach / path on disk | API upload/download; **WPF attach UI not built** |
| Master data CRUD (depts, items, doctors, sterilizers) | **Not built** (DB tables exist) |
| RDLC reports (load sheet, BI log, leak, Bowie-Dick) | **Not built** |
| Audit / print logs | API + DB tables; **WPF viewers not built** |

## Action items for you

1. **Decide database strategy:** keep new app on **`HSMS`**, or migrate legacy **`hsms_db`** into it (recommended long-term: one DB for this product).
2. **Adjust paths per machine:** update `DownloadFolderPath` on each workstation or use a hospital-standard folder.
3. **Inventory legacy screens** (from `hsms.exe` or user manuals) and open issues per screen until parity matches what staff actually use.
