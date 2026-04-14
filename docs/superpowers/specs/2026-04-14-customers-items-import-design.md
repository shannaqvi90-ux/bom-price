# Customers & Items Management + Import Design

**Date:** 2026-04-14  
**Feature:** Customer & Item CRUD, Excel import, Purchase Ledger import  
**Approach:** Feature-by-feature (Customer first, then Item)

---

## Overview

Add web UI and import capabilities for Customers and Items. Admins can bulk-import from Excel; SalesPersons can manually add their own customers and items. Purchase ledger Excel files from the ERP system can be uploaded to auto-detect and update the last purchase price per item using a column-mapping step.

---

## Phase 1: Customer Feature

### 1.1 Data Model Changes

**Customer entity — modified fields:**

| Field | Change | Notes |
|---|---|---|
| `Code` | Add (string, required) | ERP customer code, unique globally |
| `BranchId` | Remove | Customers are global, not branch-scoped |
| `SalesPersonId` | Add (int?, nullable FK → User) | Auto-filled when SalesPerson creates; null for Admin imports |
| `CreatedByUserId` | Keep | Audit trail |

**Migration notes:**
- Existing customers get `Code` defaulted to `"LEGACY-{Id}"` in migration (to be corrected manually)
- `BranchId` column dropped

**Visibility rules:**
- SalesPerson → customers where `SalesPersonId == CurrentUserId`
- Admin / BomCreator / Accountant / MD → all customers

### 1.2 API Changes

**Modified endpoints (`CustomersController`):**

| Endpoint | Change |
|---|---|
| `GET /api/customers` | Filter by `SalesPersonId == CurrentUserId` for SalesPerson (replaces BranchId filter) |
| `POST /api/customers` | Allow Admin + SalesPerson; auto-set `SalesPersonId = CurrentUserId` for SalesPerson, null for Admin |
| `PUT /api/customers/{id}` | Allow Admin + SalesPerson; Admin can edit any, SalesPerson only their own |

**DTO changes:**
- `CreateCustomerRequest` gains `Code` (required)
- `CustomerResponse` gains `Code` and `SalesPersonId`

**New endpoints (`CustomerImportController` at `api/customers/import`):**

| Endpoint | Role | Purpose |
|---|---|---|
| `GET /api/customers/import/template` | Admin | Download `.xlsx` template with headers: Code, Name, Address, Email, PhoneNumber |
| `POST /api/customers/import` | Admin | Upload `.xlsx` or `.csv`; deduplicate by `Code` globally; return `{imported, skipped, errors}` |

**Deduplication:** Skip row if Customer with same `Code` already exists (no branch filter).

### 1.3 Web UI

**Route:** `/customers`

**Page:**
- Table: Code, Name, Email, Phone, SalesPerson, Created Date
- "Add Customer" button — SalesPerson + Admin
- "Import" button — Admin only

**Add Customer modal:**

| Field | SalesPerson | Admin |
|---|---|---|
| Code | Required, manual | Required, manual |
| Name | Required | Required |
| Address | Optional | Optional |
| Email | Optional | Optional |
| Phone | Optional | Optional |
| SalesPerson | Auto-filled, read-only | Not shown |

**Import modal (Admin only):**
1. Download template link
2. File upload (.xlsx or .csv)
3. Submit → result: `X imported, Y skipped, errors list`

**Sidebar:** "Customers" link visible to all roles.

---

## Phase 2: Item Feature

### 2.1 Data Model Changes

**Item entity — modified fields:**

| Field | Change | Notes |
|---|---|---|
| `LastPurchasePrice` | Add (decimal?, nullable) | Updated via import or purchase ledger |

No other entity changes. Deduplication remains `Code + BranchId`.

### 2.2 API Changes

**Modified endpoints (`ItemsController`):**

| Endpoint | Change |
|---|---|
| `GET /api/items` | Add `LastPurchasePrice` to `ItemResponse` |
| `POST /api/items` | Admin can optionally include `LastPurchasePrice` |

**Updated endpoints (`ItemImportController`):**

| Endpoint | Change |
|---|---|
| `GET /api/items/import/template` | New — download `.xlsx` template (headers: Code, Description, Type, LastPurchasePrice) |
| `POST /api/items/import` | Updated — read `LastPurchasePrice` column; dedup by `Code + BranchId` (unchanged) |

**New purchase ledger endpoints:**

| Endpoint | Role | Purpose |
|---|---|---|
| `POST /api/items/import/ledger/headers` | Admin | Upload ledger file → return list of column headers |
| `POST /api/items/import/ledger` | Admin | Upload file + column mapping → update `LastPurchasePrice` on matched items |

**Ledger import logic:**
- Accept mapping: `{ itemCodeColumn, dateColumn, unitPriceColumn, branchId }`
- Group rows by Item Code; pick row with most recent date
- Match Item Code against `Item.Code` within `branchId`
- Update `LastPurchasePrice` on matched items; skip unmatched
- Return `{ updated, skipped }`

### 2.3 Web UI

**Route:** `/items`

**Page:**
- Table: Code, Description, Type, Last Purchase Price, Branch, Active
- "Add Item" button — SalesPerson + Admin
- "Import" button — Admin only
- "Import from Ledger" button — Admin only

**Add Item modal:**

| Field | Notes |
|---|---|
| Code | Required |
| Description | Required |
| Type | Dropdown: FinishedGood / RawMaterial |
| Last Purchase Price | Optional |

**Import modal (Admin only):**
1. Select Branch
2. Download template link
3. File upload (.xlsx or .csv)
4. Submit → result: `X imported, Y skipped, errors list`

**Import from Ledger modal (Admin only — 3 steps):**

- **Step 1 — Upload:** Select Branch + upload ERP Excel → system returns column headers
- **Step 2 — Map columns:** User selects which header = Item Code, Date, Unit Price (dropdowns populated from Step 1 headers)
- **Step 3 — Result:** System finds last price per item, updates matched items; shows `X updated, Y skipped`

**Sidebar:** "Items" link visible to all roles.

---

## Implementation Order

1. Customer entity migration (add Code, remove BranchId, add SalesPersonId)
2. Customer API updates (controller + DTOs)
3. CustomerImportService + CustomerImportController (template download + import)
4. Customer web UI (list page + add modal + import modal)
5. Item entity migration (add LastPurchasePrice)
6. Item API updates (controller + DTOs + template endpoint)
7. ItemImportService update (handle LastPurchasePrice column)
8. Purchase ledger endpoints (headers + import with column mapping)
9. Item web UI (list page + add modal + import modal + ledger modal)
