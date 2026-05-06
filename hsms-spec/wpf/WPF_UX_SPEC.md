## HSMS WPF UX Spec (keyboard-first, non-technical users)

### Core principles (mandatory)
- **Keyboard-first**: user can complete 95% of work without touching the mouse.
- **ENTER drives flow**: ENTER commits current field and advances to next logical field.
- **Instant search**: filters update as the user types; no “Search” button.
- **Minimal screens**: one primary screen (Cycle Entry). Everything else supports it.
- **Short, human error messages**: no stack traces, no technical terms.

---

## Global keyboard map (consistent everywhere)
- **ENTER**: commit field / move next / default action in dialog (OK/Save).
- **SHIFT+ENTER**: move to previous field (where appropriate).
- **TAB / SHIFT+TAB**: standard navigation (must match ENTER order).
- **ESC**: close dialog / cancel edit / clear transient popup.
- **F5**: refresh/reload current view.
- **CTRL+F**: focus search box (if present).
- **ALT+N**: New (where applicable).
- **F9**: Print current record (where applicable).

### Focus rules (mandatory)
- On window open, focus the **first required input**.
- On validation error: focus the **first invalid control** and select its text.
- After successful save: keep focus in a sensible next action control (usually **CycleNo** for next entry).

---

## Screen map and navigation

### 1) Login window
**Controls**
- Username (textbox) → Password (passwordbox) → Login (button)

**Behavior**
- ENTER on Username: moves to Password.
- ENTER on Password: triggers Login.
- On failure: show message near the controls (not a modal if possible), refocus Password, select-all.

### 2) Main window (Shell)
**Layout**
- Top strip: current user, role, station name, server status indicator (green/red).
- Left: module list (very small): `Cycles`, `QA Tests`, `Reports`, `Maintenance`, `Audit` (admin only).
- Main content area: hosts module pages.

**Behavior**
- On open: focus global search (CycleNo search).
- CTRL+F: focus global search from anywhere.
- F5: refresh the currently active page.

---

## Primary workflow: Sterilization Cycle Entry (Cycles module)

### Page layout (simple, not cluttered)
Split into 3 zones:
1) **Cycle header** (key fields)
2) **Items grid** (fast line entry)
3) **Receipts + actions** (attach/preview + save/print)

### Header fields and order (ENTER and TAB must match)
1. `CycleNo` (textbox) **required**
2. `SterilizerNo` (combo with type-ahead) **required**
3. `SterilizationType` (combo) **required**
4. `CycleDateTime` (datetime) default now **required**
5. `Operator` (textbox) default current login **required**
6. `TemperatureC` (numeric) optional
7. `Pressure` (numeric) optional
8. `BIResult` (combo/text) optional
9. `Doctor/Room` (combo with type-ahead) optional
10. `Implants` (checkbox) optional
11. `CycleStatus` (combo: Draft/Completed/Voided) **required**
12. `Notes` (multiline) optional (ENTER should not insert newline by default; use CTRL+ENTER for newline)

### CycleNo behavior (critical)
When user types `CycleNo` and presses **ENTER**:
- If found: load cycle + items + receipts, focus `SterilizerNo`.
- If not found: create a **Draft** cycle in memory (not yet saved) and focus `SterilizerNo`.
- If user tries to save and `CycleNo` duplicates: show message “Cycle number already exists. Open the existing record.” and refocus CycleNo.

### Auto-fill rules
- `CycleDateTime`: default to now (editable).
- `Operator`: default to logged-in username/display name (editable).
- `SterilizerNo`: remember last used on that station (per-client setting).
- `CycleStatus`: default `Draft`.

### Items grid (fast data entry)
**Grid columns** (left to right)
1. `ItemSearch` (editable) – type-to-search master items; dropdown list appears.
2. `ItemName` (readonly display after selection; editable only if “Custom item” mode)
3. `Qty` (numeric)

**Grid behavior**
- ENTER inside a cell commits cell edit and moves to next cell.
- ENTER on `Qty` in last row commits and **creates a new empty row**, focusing `ItemSearch`.
- CTRL+DELETE deletes current row (with confirm only if row has data).
- F2 toggles “Custom item” for the row (when master item not found).
- Instant lookup: ItemSearch filters as user types, supports arrow keys + ENTER to select.

**Validation**
- Qty must be > 0.
- ItemName required.
- Keep errors inline at row level (small message), do not block the entire form.

### Receipts area
**Controls**
- Attach Receipt (button) + hotkey **ALT+A**
- Receipt list (most recent first)
- Preview panel (image thumbnail) with “Open” action for PDF

**Behavior**
- Attach opens file picker filtered to `*.jpg;*.png;*.pdf`
- After selecting file:
  - Show “Uploading…” non-modal status
  - On success: add to receipt list and auto-preview if image
  - On failure: show clear message (“File type not allowed.” / “Upload failed. Please try again.”)

### Save/Print actions
- Primary Save button labeled **Save (ENTER)**; also support **F5** as refresh only (save is ENTER).
- Recommended explicit hotkeys:
  - **CTRL+S**: save
  - **F9**: print current record (Load Record report by default)
- On successful save: show subtle “Saved” indicator (no modal), keep focus on CycleNo ready for next entry.

---

## QA Tests module (Leak / Bowie-Dick)

### QA test entry page
Two modes:
- Start from a cycle (preferred): open cycle → press hotkey **ALT+Q** to create QA test linked to current cycle.
- Search cycle first: `CycleNo` search box at top.

**Common fields order**
1. CycleNo (search/readonly after link)
2. TestType (Leak / BowieDick)
3. TestDateTime (default now)
4. Result (Pass/Fail)
5. MeasuredValue (optional)
6. Unit (optional)
7. PerformedBy (default operator)
8. Notes

**Behavior**
- ENTER moves through fields; on last required field, triggers Save.
- F9 prints the specific QA report (Leak/Bowie-Dick).

---

## Reports module

### Report launcher
Minimal list:
- Load Record Report (by CycleNo)
- BI Log Sheet (date range)
- Leak Test Report (by CycleNo/Test)
- Bowie-Dick Report (by CycleNo/Test)

**Behavior**
- Search box at top; ENTER opens selected report parameters.
- Parameters dialogs are short; ENTER runs report; ESC closes.

---

## Maintenance module (CRUD master data)
**Design**
- One master table per page: Departments, Dept Items, Doctors/Rooms, Sterilizers.
- Top: instant search `q` box (CTRL+F focuses).
- Middle: grid list (arrow keys navigate).
- Bottom/right: form for edit with Save/Cancel.

**Behavior**
- ENTER on list opens edit form.
- ALT+N creates new record, focus first field.
- Soft disable toggle `isActive` instead of hard delete.

---

## Audit (admin only)
- Filters: date range, actor, module, entity name/id, action.
- List shows summary; details panel shows old/new JSON side by side (read-only).

---

## Status and messaging (avoid modal spam)
- Use a small **status strip** for: “Saved”, “Uploading…”, “Printed”, “Server offline”.
- Only use modal dialogs for:
  - Unsaved changes on close
  - Confirmation for voiding a cycle
  - Critical error requiring acknowledgement

