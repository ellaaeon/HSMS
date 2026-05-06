## HSMS RDLC Reporting Spec (2-page with receipt)

### Reporting goals (mandatory)
- Every official report is **2 pages minimum**:
  - **Page 1**: structured sterilization / QA data
  - **Page 2**: scanned machine receipt image
- Reporting must work **offline** and load receipt content from **server-local file storage** via API.

---

## Rendering model (recommended for WPF)
WPF requests a **report dataset payload** from the API and renders RDLC locally (so printing routes to the user’s local printer reliably).

### Data transport to WPF
- API returns a JSON payload with:
  - core data tables (Cycle, Items, QATests)
  - `ReceiptImageBytesBase64` (preferred for RDLC Image(Database))
- WPF converts Base64 → `byte[]` and supplies it to the RDLC dataset.

---

## Receipt image rules (JPG/PNG/PDF)

### JPG/PNG receipt (preferred)
- API reads the file from `D:\HSMS\Receipts\...` and returns bytes.
- RDLC uses Image control:
  - **Source**: Database
  - **MIMEType**: bound field (e.g., `image/jpeg`, `image/png`)
  - **Value**: `ReceiptImageBytes`
  - **Sizing**: FitProportional

### PDF receipt handling (must still be supported)
RDLC cannot natively render PDF pages as images; therefore the system uses **derived image generation**:
- On PDF upload:
  - Store original PDF as a normal receipt record.
  - Generate a derived image (PNG) for printing/preview:
    - `cycle_{cycleNo}_{timestamp}_{guid}_page1.png`
  - Store the derived PNG as a second `cycle_receipts` record (same `sterilization_id`).
- Report printing always uses:
  - latest **image** receipt (png/jpg) if present
  - otherwise uses the derived PNG from PDF

This approach requires **no schema changes** (uses existing `cycle_receipts` table).

---

## Shared dataset definitions (used by all reports)
Each report RDLC contains these datasets (names consistent across reports):

### DataSet: `Cycle`
One row:
- `CycleNo` (string)
- `SterilizerNo` (string)
- `SterilizationType` (string)
- `CycleDateTimeLocalDisplay` (string) // formatted in API or WPF
- `OperatorName` (string)
- `TemperatureC` (decimal/string)
- `Pressure` (decimal/string)
- `BIResult` (string)
- `CycleStatus` (string)
- `DoctorName` (string)
- `Room` (string)
- `Implants` (bool/string)

### DataSet: `Items`
0..N rows:
- `ItemName` (string)
- `Qty` (int)

### DataSet: `QATests`
0..N rows:
- `TestType` (string)
- `TestDateTimeLocalDisplay` (string)
- `Result` (string)
- `MeasuredValue` (decimal/string)
- `Unit` (string)
- `PerformedBy` (string)

### DataSet: `Receipt`
One row:
- `ReceiptContentType` (string) // `image/jpeg` or `image/png`
- `ReceiptImageBytes` (byte[]) // RDLC Image(Database) value
- `ReceiptFileName` (string)
- `CapturedAtLocalDisplay` (string)

---

## Report templates

### 1) Load Record Report (`LoadRecord.rdlc`)
**Page 1 layout**
- Header: Hospital name, report title, print timestamp, page number
- Section A: Cycle header fields (CycleNo, SterilizerNo, Type, DateTime, Operator, Status)
- Section B: Parameters (Temp, Pressure, BI Result, Implants, Doctor/Room)
- Section C: Items table (ItemName, Qty)
- Section D: QA summary (Leak/BowieDick if available)
- Footer: signature lines (Operator, Supervisor)

**Page 2 layout**
- Title: “Machine Receipt”
- Receipt metadata (file name, captured timestamp)
- Image control bound to `Receipt.ReceiptImageBytes`

### 2) Leak Test Report (`LeakTest.rdlc`)
**Page 1**
- Cycle summary + test fields (result, measured value/unit, performed by, notes)
**Page 2**
- Receipt image page (same shared pattern)

### 3) Bowie-Dick Report (`BowieDick.rdlc`)
Same pattern as Leak Test.

### 4) BI Log Sheet Report (`BILogSheet.rdlc`)
**Purpose**
- Daily/weekly BI entries by date range.

**Parameters**
- `FromDate`
- `ToDate`
- optional `SterilizerNo`

**Page 1**
- Table of cycles in range with BI Result and key fields
- Group by date (optional)

**Page 2**
- If a single cycle is selected for printing, include receipt page.
- If printing a batch BI log sheet, include “Receipts not included in batch print” note (to keep output manageable).

---

## RDLC page setup (consistent)
- Paper: A4 (or hospital standard), Portrait
- Margins: 1.0–1.5 cm
- Page breaks:
  - explicit page break between structured page and receipt page
- Fonts: clear, large enough for non-technical users

---

## Printing + audit requirement
- Every print action must be logged:
  - WPF prints locally
  - WPF calls `POST /print-logs` after successful print
  - API writes `print_logs` and also `audit_logs` action=Print
