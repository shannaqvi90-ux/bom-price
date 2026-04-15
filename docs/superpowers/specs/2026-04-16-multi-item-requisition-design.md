# Multi-Item Requisition — Design Spec

**Date:** 2026-04-16
**Feature:** Allow multiple items per quotation requisition

---

## Overview

Currently a `QuotationRequest` holds a single `ItemId` + `ExpectedQty`, and the entire BOM → Costing → Approval pipeline is scoped to one item. This change introduces a `RequisitionItem` join table so a requisition can contain any number of items, each with its own BOM, costing, and sales price — while the requisition-level status machine stays unchanged.

---

## Data Model

### New entity: `RequisitionItem`

```csharp
public class RequisitionItem
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public int ItemId { get; set; }
    public decimal ExpectedQty { get; set; }
    public int SortOrder { get; set; }          // preserves entry order on PDF

    public QuotationRequest QuotationRequest { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public BomHeader? BomHeader { get; set; }
    public ApprovalItem? ApprovalItem { get; set; }
}
```

### Modified: `QuotationRequest`

Remove `ItemId` (int) and `ExpectedQty` (decimal).  
Add navigation: `ICollection<RequisitionItem> Items`.

### Modified: `BomHeader`

Replace `QuotationRequestId` (int) + `ItemId` (int) with:

```csharp
public int RequisitionItemId { get; set; }
public RequisitionItem RequisitionItem { get; set; } = null!;
```

Requisition is reachable via `RequisitionItem.QuotationRequestId`. One `BomHeader` per `RequisitionItem` — same cardinality as before.

### New entity: `ApprovalItem`

```csharp
public class ApprovalItem
{
    public int Id { get; set; }
    public int QuotationApprovalId { get; set; }
    public int RequisitionItemId { get; set; }
    public decimal SalesPricePerKgAed { get; set; }
    public decimal? SalesPricePerKgForeign { get; set; }
    public decimal ProfitMarginPct { get; set; }
    public decimal MaterialCostPct { get; set; }
    public decimal OtherCostPct { get; set; }

    public QuotationApproval QuotationApproval { get; set; } = null!;
    public RequisitionItem RequisitionItem { get; set; } = null!;
}
```

### Modified: `QuotationApproval`

Remove: `SalesPricePerKgAed`, `SalesPricePerKgForeign`, `ProfitMarginPct`, `MaterialCostPct`, `OtherCostPct`.  
Add navigation: `ICollection<ApprovalItem> Items`.  
Keeps: `QuotationRequestId`, `ApprovedByUserId`, `ApprovedAt`, `Notes`, `IsApproved`.

---

## Backend API

### RequisitionsController

**`POST /api/requisitions`**  
`CreateRequisitionRequest` replaces top-level `ItemId`/`ExpectedQty` with:
```json
{
  "customerId": 1,
  "currencyCode": "AED",
  "items": [
    { "itemId": 5, "expectedQty": 1000 }
  ]
}
```
Minimum 1 item required. Creates `QuotationRequest` + one `RequisitionItem` per entry.

**`POST /api/requisitions/{id}/items`**  
Add an item to a **Draft** requisition.  
Body: `{ "itemId": 6, "expectedQty": 500 }`

**`DELETE /api/requisitions/{id}/items/{itemId}`**  
Remove an item from a **Draft** requisition. Returns 400 if it is the last item.

**`GET /api/requisitions/{id}`**  
`RequisitionDetail` replaces `itemId`/`itemDescription`/`expectedQty` with:
```json
"items": [
  { "id": 1, "itemId": 5, "itemDescription": "...", "expectedQty": 1000 }
]
```

### BomController

| Endpoint | Purpose |
|----------|---------|
| `POST /api/bom/{reqId}/items/{itemId}/start` | Create `BomHeader` for one `RequisitionItem`. First call flips requisition status → BomInProgress. |
| `GET /api/bom/{reqId}` | Returns all items with their BOM status and lines. |
| `PUT /api/bom/{reqId}/items/{itemId}/lines` | Save/replace BOM lines for one item (draft save, no status change). |
| `POST /api/bom/{reqId}/submit` | Validate all items have ≥1 BOM line, then status → CostingPending. |

### CostingController

| Endpoint | Purpose |
|----------|---------|
| `POST /api/costing/{reqId}/items/{itemId}/start` | Mark costing started for one item. First call flips status → CostingInProgress. |
| `GET /api/costing/{reqId}` | Returns all items with their cost data. |
| `POST /api/costing/{reqId}/items/{itemId}/submit` | Submit costs for one item. When ALL items have submitted costs → status → MdReview. |

### ApprovalsController

**`GET /api/approvals/{reqId}`**  
Returns all items with individual cost breakdowns:
```json
"items": [
  {
    "requisitionItemId": 1,
    "itemDescription": "...",
    "expectedQty": 1000,
    "totalCostPerKg": 4.5000,
    "rawMaterialCostPerKg": 3.2000,
    "landedCostPerKg": 0.8000,
    "fohPerKg": 0.5000
  }
]
```

**`POST /api/approvals/{reqId}/approve`**
```json
{
  "items": [
    { "requisitionItemId": 1, "salesPricePerKgAed": 6.0000 },
    { "requisitionItemId": 2, "salesPricePerKgAed": 5.5000 }
  ],
  "notes": "optional"
}
```
Creates one `QuotationApproval` + one `ApprovalItem` per item.

**`POST /api/approvals/{reqId}/reject`** — unchanged.

---

## Workflow

Status transitions remain requisition-level:

| Transition | Trigger |
|------------|---------|
| Draft → BomPending | Salesperson submits requisition (unchanged) |
| BomPending → BomInProgress | BOM Creator starts first item's BOM |
| BomInProgress → CostingPending | BOM Creator submits — all items must have ≥1 BOM line |
| CostingPending → CostingInProgress | Accountant starts first item's costing |
| CostingInProgress → MdReview | Last item's costs are submitted |
| MdReview → Approved | MD approves with per-item prices |
| MdReview → Rejected | MD rejects |

---

## Frontend

### New/modified files
```
bom-web/src/features/requisitions/RequisitionItemsEditor.tsx   — reusable item row list (create + add)
bom-web/src/features/requisitions/AddRequisitionItemModal.tsx  — modal to add item to draft
bom-web/src/features/requisitions/CreateRequisitionPage.tsx    — modified: use ItemsEditor
bom-web/src/features/requisitions/RequisitionDetailPage.tsx    — modified: items table + Add/Remove
bom-web/src/features/bom/BomEntryPage.tsx                      — modified: item selector sidebar
bom-web/src/features/costing/CostingPage.tsx                   — modified: item selector sidebar
bom-web/src/features/approvals/MdReviewPage.tsx                — modified: per-item price inputs
bom-web/src/types/api.ts                                       — add RequisitionItem, ApprovalItem types
```

### `RequisitionItemsEditor`
Reusable component for both create and draft-edit contexts. Renders a list of item rows (Item select + Qty input). Add button appends a new row. Remove button disabled when only 1 row. Validation: all rows need Item + Qty > 0.

### `CreateRequisitionPage`
Replace single Item/Qty fields with `<RequisitionItemsEditor>`.

### `RequisitionDetailPage`
- Replace single item display with items table: Description | Qty | Unit
- Draft status: show "Add Item" button → `AddRequisitionItemModal`; each row has remove button (disabled if last item)

### `BomEntryPage`
- Left panel: list of requisition items, each with a status chip (Not Started / In Progress / Done)
- Right panel: BOM lines editor for the selected item
- "Start" button per item (calls `start` for that item)
- "Submit All" button at bottom — disabled until all items have ≥1 line

### `CostingPage`
- Same item-selector sidebar pattern as BomEntryPage
- Each item's cost form loaded independently
- "Submit" per item — requisition auto-advances to MdReview when last item is submitted

### `MdReviewPage`
- Items listed with individual cost breakdown cards
- Each item has a Sales Price input + live margin pill
- Single Approve / Reject action for the whole requisition
- Approve sends `[{requisitionItemId, salesPricePerKgAed}]` for all items

---

## PDF

Items table replaces the single-item row:

| # | Item Description | Qty (kg) | Unit Price (`{currency}`) | Total (`{currency}`) |
|---|-----------------|----------|--------------------------|----------------------|
| 1 | Polyethylene... | 1,000 | 6.0000 | 6,000.00 |
| 2 | Polypropylene... | 500 | 5.5000 | 2,750.00 |

Grand total row at bottom. Exchange rate note shown once if non-AED currency.

---

## Migrations

1. Add `RequisitionItems` table
2. Add `ApprovalItems` table
3. Modify `BomHeaders`: add `RequisitionItemId`, drop `QuotationRequestId` + `ItemId`
4. Modify `QuotationApprovals`: drop price/margin columns
5. Modify `QuotationRequests`: drop `ItemId`, `ExpectedQty`

**Migration order matters** — existing data must be migrated before dropping old columns. For a fresh dev database, a single migration is sufficient.
