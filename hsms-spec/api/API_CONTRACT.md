## HSMS API Contract (offline LAN)

This document specifies the ASP.NET Core Web API surface used by WPF clients.

### Conventions
- **Base URL**: `http(s)://{server}:5080/api`
- **Auth**: `Authorization: Bearer {token}`
- **Time**: all timestamps are **UTC** in API and DB; WPF may display local time.
- **Optimistic concurrency**: transactional entities include a `rowVersion` field.
  - `rowVersion` is the SQL Server `rowversion` value encoded as **Base64**.
  - Update requests must include `rowVersion`; server returns **409 Conflict** when mismatched.
- **Error format** (all non-2xx):

```json
{
  "code": "string",
  "message": "Human readable message",
  "details": { }
}
```

### Standard error codes
- `AUTH_INVALID_CREDENTIALS` (401)
- `AUTH_INACTIVE_ACCOUNT` (403)
- `VALIDATION_FAILED` (400)
- `NOT_FOUND` (404)
- `DUPLICATE_CYCLE_NO` (409)
- `CONCURRENCY_CONFLICT` (409)
- `FILE_TYPE_NOT_ALLOWED` (400)
- `FILE_TOO_LARGE` (400)

---

## 1) Authentication

### POST `/auth/login`
Login using username/password.

**Request**

```json
{ "username": "string", "password": "string", "clientMachine": "string" }
```

**Response 200**

```json
{
  "accountId": 1,
  "username": "nurse1",
  "role": "Staff",
  "accessToken": "string",
  "expiresAtUtc": "2026-04-28T16:30:00Z"
}
```

**Behavior**
- Password hashing: **BCrypt** (recommended) or **Argon2**.
- Writes `audit_logs` with action `Login`.

### POST `/auth/change-password`
User changes own password.

**Request**

```json
{ "currentPassword": "string", "newPassword": "string" }
```

**Response 204**

---

## 2) Sterilization cycles

### POST `/sterilizations`
Create a sterilization cycle. `cycleNo` must be unique.

**Request**

```json
{
  "cycleNo": "12345",
  "sterilizerId": 1,
  "sterilizationType": "Steam",
  "cycleDateTimeUtc": "2026-04-28T16:30:00Z",
  "operatorName": "string",
  "temperatureC": 134.0,
  "pressure": 2.100,
  "biResult": "Negative",
  "cycleStatus": "Draft",
  "doctorRoomId": 10,
  "implants": false,
  "notes": "string",
  "items": [
    { "deptItemId": 50, "itemName": "Forceps", "qty": 2 }
  ],
  "clientMachine": "string"
}
```

**Response 201**

```json
{
  "sterilizationId": 101,
  "cycleNo": "12345",
  "rowVersion": "AAAAAAAAB9E="
}
```

**Errors**
- 409 `DUPLICATE_CYCLE_NO`: “Cycle number already exists. Please open the existing record.”

### GET `/sterilizations/{sterilizationId}`
Fetch cycle details including items and latest receipt metadata.

**Response 200**

```json
{
  "sterilizationId": 101,
  "cycleNo": "12345",
  "sterilizerId": 1,
  "sterilizationType": "Steam",
  "cycleDateTimeUtc": "2026-04-28T16:30:00Z",
  "operatorName": "string",
  "temperatureC": 134.0,
  "pressure": 2.100,
  "biResult": "Negative",
  "cycleStatus": "Draft",
  "doctorRoomId": 10,
  "implants": false,
  "notes": "string",
  "items": [
    {
      "sterilizationItemId": 5001,
      "deptItemId": 50,
      "itemName": "Forceps",
      "qty": 2,
      "rowVersion": "AAAAAAAAB9I="
    }
  ],
  "receipts": [
    {
      "receiptId": 900,
      "fileName": "cycle_12345_20260429_003012_...jpg",
      "contentType": "image/jpeg",
      "capturedAtUtc": "2026-04-28T16:31:00Z"
    }
  ],
  "rowVersion": "AAAAAAAAB9E="
}
```

### PUT `/sterilizations/{sterilizationId}`
Update cycle and items in a single transaction.

**Request**

```json
{
  "rowVersion": "AAAAAAAAB9E=",
  "cycleDateTimeUtc": "2026-04-28T16:30:00Z",
  "operatorName": "string",
  "temperatureC": 134.0,
  "pressure": 2.100,
  "biResult": "Negative",
  "cycleStatus": "Completed",
  "doctorRoomId": 10,
  "implants": false,
  "notes": "string",
  "items": [
    { "sterilizationItemId": 5001, "rowVersion": "AAAAAAAAB9I=", "deptItemId": 50, "itemName": "Forceps", "qty": 3 }
  ],
  "clientMachine": "string"
}
```

**Response 200**

```json
{ "rowVersion": "AAAAAAAAB+Q=" }
```

**Errors**
- 409 `CONCURRENCY_CONFLICT`: “Someone updated this record. Press F5 to reload.”

### GET `/sterilizations/search`
Instant search to support keyboard-first UX.

Query params:
- `cycleNo` (prefix match)
- `fromUtc`, `toUtc`
- `sterilizerId`
- `status`
- `take` (default 50; max 200)

**Response 200**

```json
[
  { "sterilizationId": 101, "cycleNo": "12345", "cycleDateTimeUtc": "2026-04-28T16:30:00Z", "sterilizerNo": "S1", "cycleStatus": "Draft" }
]
```

---

## 3) QA tests

### POST `/qa-tests`
Create a QA test linked to a cycle.

**Request**

```json
{
  "sterilizationId": 101,
  "testType": "Leak",
  "testDateTimeUtc": "2026-04-28T16:40:00Z",
  "result": "Pass",
  "measuredValue": 0.0,
  "unit": "kPa",
  "notes": "string",
  "performedBy": "string",
  "clientMachine": "string"
}
```

**Response 201**

```json
{ "qaTestId": 7001, "rowVersion": "AAAAAAAACAA=" }
```

### PUT `/qa-tests/{qaTestId}`
Update a QA test with concurrency.

**Request**

```json
{
  "rowVersion": "AAAAAAAACAA=",
  "result": "Fail",
  "measuredValue": 2.5,
  "unit": "kPa",
  "notes": "string",
  "clientMachine": "string"
}
```

---

## 4) Receipts (attachments)

### POST `/sterilizations/{sterilizationId}/receipts`
Upload a scanned receipt file. Allowed: `.jpg`, `.png`, `.pdf`.

**Request**: `multipart/form-data`
- field `file`: binary file
- field `capturedAtUtc`: optional (defaults to now)

**Response 201**

```json
{
  "receiptId": 900,
  "fileName": "cycle_12345_20260429_003012_...jpg",
  "contentType": "image/jpeg",
  "fileSizeBytes": 123456,
  "capturedAtUtc": "2026-04-28T16:31:00Z"
}
```

### GET `/sterilizations/{sterilizationId}/receipts/{receiptId}`
Stream the receipt file to WPF for preview and RDLC binding.

**Response 200**
- `Content-Type`: the stored `contentType`
- Body: file bytes

---

## 5) Master data (Maintenance module)
All master tables support full CRUD with soft-disable (`isActive`).

### GET `/masters/departments` (and similar for items/doctors/sterilizers)
Supports `q=` for instant lookup.

### POST/PUT `/masters/departments`
Admin-only (or role-based as configured).

---

## 6) Reporting + print logging

### GET `/reports/load-record/{sterilizationId}/dataset`
Returns strongly typed datasets used by RDLC viewer in WPF.

**Response 200**

```json
{
  "reportType": "LoadRecord",
  "generatedAtUtc": "2026-04-28T16:50:00Z",
  "data": {
    "cycle": { },
    "items": [ ],
    "qaTests": [ ],
    "receipt": {
      "receiptId": 900,
      "contentType": "image/jpeg",
      "imageBytesBase64": "string"
    }
  }
}
```

### POST `/print-logs`
WPF calls this after a successful local print action.

**Request**

```json
{
  "reportType": "LoadRecord",
  "sterilizationId": 101,
  "qaTestId": null,
  "printerName": "HP LaserJet ...",
  "copies": 1,
  "parameters": { "includeReceipt": true },
  "clientMachine": "string"
}
```

**Response 201**

```json
{ "printLogId": 1 }
```

