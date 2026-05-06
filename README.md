# HSMS (Hospital Sterilization Management System)

Production rewrite starter for offline hospital LAN deployment.

## Stack
- Desktop UI: C# / .NET 8 / WPF
- Backend API: ASP.NET Core Web API (.NET 8)
- Database: SQL Server Express
- Reporting: RDLC
- File storage: local server filesystem (`D:\HSMS\Receipts`)

## Repository layout
- `hsms-db/ddl/001_hsms_init.sql` - SQL Server schema
- `hsms-spec/` - architecture and module specs
- `hsms-spec/legacy/HSMS_EXE_CONFIG_PARITY.md` - mapping from legacy `hsms.exe.config` to this app
- `hsms-spec/legacy/LEGACY_DEBUG6_FUNCTION_CHECKLIST.md` - full legacy `Debug (6)` scan → features to implement (upgraded UX)
- `hsms.exe.config` - legacy desktop config (reference only)
- `src/HSMS.Api` - ASP.NET Core API project (optional; desktop can run standalone)
- `src/HSMS.Desktop` - WPF desktop project (**standalone**: talks to SQL Server directly)
- `src/HSMS.Application` - shared business services and role checks
- `src/HSMS.Persistence` - EF Core `HsmsDbContext`, entities, audit writer
- `src/HSMS.Shared` - shared DTO contracts

## Prerequisites
- **.NET 8 SDK** (e.g. `dotnet --version` shows 8.0.x).
- SQL Server Express running locally or on LAN server.

## Build and run

```powershell
dotnet build .\HSMS.sln -c Release
```

### Standalone desktop (default product path)

The WPF app uses **`ConnectionStrings:SqlServer`** in `src/HSMS.Desktop/appsettings.json` (same database as the API would use). **No API process is required** for sign-in, cycles, or Admin Setup.

```powershell
dotnet run --project .\src\HSMS.Desktop\HSMS.Desktop.csproj
```

Adjust **`Server=`** in that connection string for your instance (e.g. `localhost\\SQLEXPRESS`). Apply DDL scripts (`001`, optional `002`/`003`, and `004` for least-privilege) against the `HSMS` database first.

### Optional API (Swagger / future sync)

```powershell
dotnet run --project .\src\HSMS.Api\HSMS.Api.csproj
```

API listens on **http://localhost:5080** (see `src/HSMS.Api/Properties/launchSettings.json`). Swagger: `http://localhost:5080/swagger`. The desktop app **does not** call this API in the current standalone build.

### Development sign-in (first run)

The **desktop app** does not seed the database. Create `admin` (or run `HSMS.Api` once in **Development** so `SeedDevelopmentAdminAsync` runs) before signing in standalone.

With `ASPNETCORE_ENVIRONMENT=Development` and an empty `tbl_account_login`, the API seeds:

- **Username:** `admin`
- **Password:** `ChangeMe123`

If your database was created with an older seed, the password may still be `ChangeMe!123` until you reset the `admin` account in SQL.

It also seeds a default sterilizer **S1** (id `1`) so cycle creation satisfies the foreign key.

**Change the password and remove or harden this seed before production.**

### Production checklist (before hospital deployment)

- Run the API with **`ASPNETCORE_ENVIRONMENT=Production`**. The dev **admin / sterilizer seed only runs in Development**; in Production you must create accounts and master data via SQL or an admin tool.
- Set a long random **`Jwt:SigningKey`** (at least **32 characters**; longer is better).
- Use a **least-privilege** Windows or SQL login for `ConnectionStrings:SqlServer` (not your personal admin account if possible).
- Publish API + WPF installers as needed; standalone deployments ship the desktop with a local SQL connection string only.

## Setup
1. Create `HSMS` database in SQL Server Express.
2. Run `hsms-db/ddl/001_hsms_init.sql` against `HSMS` (and optional `002_*`, `003_*` if your environment needs them).
3. For **least-privilege runtime access** and **append-only** `audit_logs` / `print_logs` at the database, run `hsms-db/ddl/004_hsms_security_runtime_role_audit.sql`, then create a dedicated SQL or Windows login and add it as a member of `HSMS_Runtime`. Point the **desktop** connection string (and API if used) at that login (not `sa`). Use a separate elevated principal for migrations (`HSMS_Migrator` / `dbo` per your IT policy).
4. Set **`ConnectionStrings:SqlServer`** in `src/HSMS.Desktop/appsettings.json` (required for standalone). Optionally mirror the same in `src/HSMS.Api/appsettings.json` if you run the API.
5. For the API only: edit `src/HSMS.Api/appsettings.json` — `Jwt:SigningKey`, `Storage:ReceiptsRootPath`, and ensure the service account can read/write the receipts folder.

### SQL Server: keep HSMS separate from `hsms_db`

This solution **never references `hsms_db`**. The **desktop app** and (if you run it) the **API** both open the **`HSMS`** database via their own `ConnectionStrings:SqlServer`.

- In `src/HSMS.Desktop/appsettings.json` and `src/HSMS.Api/appsettings.json`, keep **`Database=HSMS`** (or **`Initial Catalog=HSMS`**).
- If you have another app using **`hsms_db`**, leave it as-is; just **do not** put `hsms_db` in this project’s `SqlServer` connection string.
- You may change **`Server=`** (instance name, e.g. `localhost`, `localhost\\SQLEXPRESS`, or a LAN host) to match your machine; the important part is **`Database=HSMS`** for HSMS data.
- Overrides (optional): use **User Secrets** or an environment variable so production never accidentally shares the other app’s DB, for example:
  - Environment variable: `ConnectionStrings__SqlServer` = your full HSMS-only connection string (still with `Database=HSMS`).

### Troubleshooting: “Error Locating Server/Instance Specified” (error 26)

The API could not reach SQL Server. The value after **`Server=`** must match **how you connect in SSMS**.

- If SSMS shows **`localhost`** or your PC name (e.g. **`cad`**) with no `\INSTANCE` suffix, use **`Server=localhost`** or **`Server=cad`** — both target the **default** instance (`MSSQLSERVER`). The sample `appsettings.json` uses **`Server=cad`** to match a typical “Server name = computer name” setup.
- If you connect as **`localhost\SQLEXPRESS`** (or another name), set **`Server=localhost\SQLEXPRESS`** (double backslash in JSON: `localhost\\SQLEXPRESS`).
- Copy the **exact** server string from SSMS: **Connect → Connection Properties → Server name**, then paste it into `ConnectionStrings:SqlServer` before `;Database=HSMS`.

## Current implementation status
- API:
  - Auth login endpoint (BCrypt verification + JWT)
  - Sterilization create/search/get/update with rowversion conflict checks
  - QA create/update
  - Receipt upload/download with temp-file then move pattern
  - Print log endpoint and audit write service
- WPF:
  - Main shell and cycle entry screen scaffold
  - Keyboard shortcuts: Enter/F5/F9/Esc/Ctrl+S
  - Basic API client wiring for cycle search/create

## Next coding steps
1. Add migrations and repository/service abstractions.
2. Complete WPF MVVM (ViewModels + commands + validation messaging).
3. Implement master data CRUD endpoints and screens.
4. Integrate RDLC viewer and report dataset endpoints.
5. Add reconciliation job for orphan/missing receipt files.
