# V3 — Simplified Workflow (MD's New Requirements)

**Spec date:** 2026-04-29
**Branch (planned, Phase A):** `feat/v3-backend-foundation` (off `master` @ `0c42a43`)
**Predecessor:** V2.3-C P2 (admin override C6/C8) + post-merge fixes for cross-branch admin create + BOM RM scoping (PRs #19–#26)
**Mockup:** [`docs/mockups/new-workflow-v3-mockup.html`](../../mockups/new-workflow-v3-mockup.html) v1.3 (MD-approved 2026-04-29)
**Source requirements:** `New Requirements.docx` (handed over by MD 2026-04-29)

---

## 1. Summary

V3 is a major workflow refactor of FPF Quotations driven by the MD's request for a much simpler, fewer-steps quotation process. It collapses the BOM-creator handoff into a single sales-owned screen, restricts scope to the Alain branch only, and introduces a two-stage MD approval (initial pricing → customer-confirmation → final-sign-and-lock).

The end result is a shorter happy path (sales submits combined req+BOM → accountant verifies+costs → MD prices → sales confirms with customer → MD signs and locks) and an immutable signed PDF that is the canonical record.

This is a back-compat-breaking change (state machine renames + role removal + hard cut-over of in-flight requisitions). Implementation is **backend-first** (Phase A) → **frontend overhaul** (Phase B) → **migration cutover** (Phase C). Mobile V3 is deferred to Phase D.

---

## 2. Goal

Make the quotation flow match how the business actually works in 2026-04: one sales person owns a customer relationship end-to-end (including the technical recipe), the accountant is the cost authority, the MD signs off twice (price, then final), and the customer-facing PDF is the legally meaningful artefact — locked and immutable.

**Secondary goals:**
- Drop the `BomCreator` role and its 1 workflow state pair (`BomPending`/`BomInProgress`)
- Auto-generate `Customer.Code` and `Item.Code` so sales never types codes
- Hide non-Alain branches' data without deleting it (reversible scope reduction)
- Preserve all V2.3-C admin override capabilities, adapting them to the new state machine

**Out of scope (deferred to Phase D or later):**
- Mobile V3 (mobile freezes on V2 read-only after cutover)
- Multi-language (notes field stays UTF-8 / English-conventional)
- New roles or permission models beyond the BomCreator drop
- Email-the-customer-the-PDF flow (per D8 — strictly internal emails only)
- Additional reporting / analytics changes
- Multi-branch reactivation tooling (other 4 branches stay hidden indefinitely)
- Re-tooling of V2.3-A branch picker or V2.3-B sales groups (Alain-only scope makes them no-ops but the code stays)

---

## 3. Locked Design Decisions

Recorded during the 2026-04-29 brainstorm session (15 product Qs answered by user + 10 edge case Qs answered round 2). These pin design traceability.

| # | Topic | Decision |
|---|---|---|
| D1 | Customer Code format | Auto `CUST-0001` (zero-padded sequential, branch-agnostic) |
| D2 | Item Code format | Auto `FG-0001` for finished goods, `RM-0001` for raw materials (separate sequences per `ItemType`) |
| D3 | Micron input | Free-text, any number, no enforced list |
| D4 | BOM recipe total per KG | No validation — sales decides (no min/max enforcement) |
| D5 | FX rate source | Existing `ExchangeRates` table (V2.3 mechanism) |
| D6 | Margin amount currency | Quote currency (USD/EUR) — MD enters per-KG margin in customer-facing units |
| D7 | MD pricing screen breakdown | Full breakdown: RM / Printing / FOH / Transport / Commission shown side-by-side per FG (no collapse) |
| D8 | Customer-rejects flow | "Request MD to re-price" goes back to MD only (re-margin); does NOT re-route to Accountant |
| D9 | MD signature | Image upload (one-time per MD) + text below ("Approved by [Name] on [Date]") |
| D10 | Sales notes visibility | Visible to Sales + Accountant + MD (all 3 internal roles); NOT on customer-facing PDF |
| D11 | In-flight reqs migration | Hard cut-over — cancel all in-flight reqs on deploy, sales re-create needed ones in V3 |
| D12 | BOM Creator user accounts | Deactivate (`IsActive=false`); enum value kept for legacy data; no new accounts |
| D13 | Other 4 branches data | Hide entirely (only Alain visible) — Branch.IsActive=false on Dubai/Sharjah/etc; reversible |
| D14 | Mobile scope | Phase 1 web-only; mobile V3 deferred to Phase D |
| D15 | PWA | Continue (web installable PWA stays — iOS/iPadOS staff workflow preserved) |
| D16 | Sales drafts | Enabled (Draft status already exists); list has Drafts tab |
| D17 | Per-FG state independence | Whole-req single state (not per-FG sub-state); accountant/MD process all FGs together |
| D18 | Inline customer creation | Modal on requisition page; auto Code; required Name + Email + Phone + Address |
| D19 | Inline FG/RM creation | Modals; auto Code; Branch auto-set to Alain |
| D20 | Customer-to-FG filter | Implicit (FGs from past requisitions for this customer); empty for new customers (sales creates new FG inline) |
| D21 | FX rate freeze points | Snapshot at MD Stage 1 submit (margin entry); re-snap on customer-rejects re-margin; cost-side foreign RM prices snapshot at accountant submit. Two snapshots per req. |
| D22 | MD final-sign confirmation | Type-to-confirm dialog ("Type SIGN to lock"); no undo window |
| D23 | Customer email policy | NO external customer emails ever. Only internal (Sales + Accountant + MD). Sales downloads PDF and forwards manually. (V2.3-C P2 broad-email policy DOES NOT apply to V3.) |
| D24 | Accountant edits sales BOM | Diff visible to sales + MD ("BOPP qty 0.44 → 0.46"); reuses V2.3 audit infrastructure |
| D25 | Implementation phasing | Backend-first (A) → Frontend (B) → Migration cutover (C); Mobile = Phase D (deferred) |
| D26 | Mobile post-cutover | V2 mobile read-only; only Approved/Signed historical reqs visible (in-flight cancelled by hard cut-over); new req creation disabled until Phase D |
| D27 | V2.3-C admin overrides | Kept but adapted: Unlock BOM removed (no BOM stage); Unlock Costing → "Rollback to Costing" (from MdPricing only); Override Prices works on Signed (creates supersession + new RateSnapshot); Status rollback whitelist updated for new states |

---

## 4. Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│ FRONTEND (web only — Phase B)                                │
│                                                              │
│ /requisitions/new          NewRequisitionPage  (combined)    │
│ /requisitions/{id}/customer-confirm   CustomerConfirmPage    │
│ /approvals/{id}/final      MdFinalSignPage (type-to-confirm) │
│ /profile/signature         ProfileSignaturePage              │
│                                                              │
│ Modals (inline create):                                      │
│   <CreateCustomerModal />                                    │
│   <CreateFinishedGoodModal />                                │
│   <CreateRawMaterialModal />                                 │
└──────────┬───────────────────────────────────────────────────┘
           │ Axios + auth (Bearer JWT)
           ▼
┌──────────────────────────────────────────────────────────────┐
│ BACKEND (Phase A)                                            │
│                                                              │
│ Features/Requisitions/   Create/Update accept BOM payload    │
│ Features/Costing/        Accountant editable BOM + diff log  │
│ Features/Approvals/      Split: SetMargin / AcceptCustomer / │
│                                  FinalSign                   │
│ Features/Customers/      Auto Code on POST                   │
│ Features/Items/          Auto Code on POST (per ItemType)    │
│ Features/Profile/        NEW — signature upload/get          │
│ Features/Admin/*         Adapted (see §11 admin overrides)   │
│                                                              │
│ Infrastructure/Services/                                     │
│   CodeGeneratorService   Atomic counter for codes            │
│   PdfService             Embeds MD signature image           │
│   AdminAuditLogger       Reused as-is                        │
└──────────┬───────────────────────────────────────────────────┘
           │ EF Core 8 + Npgsql
           ▼
┌──────────────────────────────────────────────────────────────┐
│ POSTGRES (Neon prod / localhost dev)                         │
│                                                              │
│ Migration: V3_AddNewStatusValues + V3_SignatureColumn +      │
│            V3_BomLineAuditColumns + V3_CodeCounters          │
│ Migration cutover SQL: §10                                   │
└──────────────────────────────────────────────────────────────┘
```

---

## 5. State Machine

```
                     ┌─────────────────┐
                     │      Draft      │ (sales editing in NewRequisitionPage)
                     └────────┬────────┘
                              │ submit
                              ▼
                     ┌─────────────────┐
                     │     Costing     │ (accountant verifies/edits BOM, enters costs)
                     └────────┬────────┘
                              │ submit
                              ▼
                     ┌─────────────────┐
                     │    MdPricing    │◄──┐ (MD adds margin per FG, optional view BOM)
                     └────────┬────────┘   │
                              │ submit     │
                              ▼            │
                     ┌─────────────────┐   │
                     │ CustomerConfirm │   │ (sales shares price with customer offline)
                     └─┬──────────────┬┘   │
                       │ accept       │ reject (re-margin)
                       ▼              └────┘
                     ┌─────────────────┐
                     │   MdFinalSign   │ (MD reviews + signs)
                     └────────┬────────┘
                              │ sign+lock (type-to-confirm)
                              ▼
                     ┌─────────────────┐
                     │     Signed      │ ◄ TERMINAL (immutable)
                     └─────────────────┘

Side states (terminal):
  ┌───────────┐   ┌───────────┐
  │ Cancelled │   │ Rejected  │
  └───────────┘   └───────────┘

  Cancelled = sales/admin cancels at any non-terminal state (also used by hard cut-over migration)
  Rejected = MD rejects from MdPricing only
```

**Status enum (final V3 values — int slots preserved for back-compat):**

```csharp
public enum RequisitionStatus
{
    Draft = 0,                  // existing — V3 still uses
    BomPending = 1,             // deprecated; legacy V2 only (cancelled at cutover)
    BomInProgress = 2,          // deprecated; legacy V2 only (cancelled at cutover)
    CostingPending = 3,         // deprecated; legacy V2 only (cancelled at cutover)
    CostingInProgress = 4,      // deprecated; legacy V2 only (cancelled at cutover)
    MdReview = 5,               // deprecated; legacy V2 only (cancelled at cutover)
    Approved = 6,               // KEPT for legacy display — V2.3 Approved reqs stay as-is post-cutover (admin overrides on these follow V2.3 rules)
    Rejected = 7,               // existing — V3 still uses (MD-rejected from MdPricing)
    Costing = 8,                // NEW
    MdPricing = 9,              // NEW (replaces MdReview semantics)
    CustomerConfirm = 10,       // NEW
    MdFinalSign = 11,           // NEW
    Signed = 12,                // NEW (terminal, immutable)
    Cancelled = 13              // NEW (terminal — sales/admin cancel + cut-over migration)
}
```

The legacy int slots (1-6) are preserved so EF Core reads of historical rows do not break. **V2.3 `Approved` rows are NOT migrated to `Signed`** — they keep their original status and display with a "Approved (V2 legacy)" label. Admin overrides on `Approved` (legacy) follow V2.3-C rules; admin overrides on `Signed` (V3 new) follow V3 rules per §11.

The V3 cutover migration only flips in-flight statuses (Draft, BomPending, BomInProgress, CostingPending, CostingInProgress, MdReview = int values 0-5) to `Cancelled` (13).

**Transition guards:** centralized in `Domain/Workflow/RequisitionStateMachine.cs` (NEW class). Each transition asserts caller role + current status. See §11 for admin override transitions.

---

## 6. Endpoints

### 6.1 Requisitions slice (Phase A)

| Method | Path | Caller | Purpose |
|---|---|---|---|
| POST | `/api/requisitions` | SalesPerson, Admin | Create — payload now includes per-FG BOM lines (item code + qty/KG of FG + micron + printing flag) and notes. Status → `Draft`. |
| PUT | `/api/requisitions/{id}` | SalesPerson (own), Admin | Update — same payload shape; allowed only on `Draft`. |
| POST | `/api/requisitions/{id}/submit` | SalesPerson (own), Admin | Transition `Draft → Costing` |
| POST | `/api/requisitions/{id}/cancel` | SalesPerson (own), Admin | Transition any non-terminal → `Cancelled`; reason required |
| GET | `/api/requisitions` | All authenticated | List (filters: status, customer, branch, date range, drafts-only) |
| GET | `/api/requisitions/{id}` | All authenticated (per BranchAuthorization) | Detail |

**Payload shape (POST /api/requisitions):**

```json
{
  "customerId": 142,
  "quotationCurrency": "USD",
  "referenceNumber": "PO-9941",
  "notes": "Customer wants delivery before Eid.",
  "finishedGoods": [
    {
      "itemId": 87,                  // FG item id
      "expectedQtyKg": 5000,
      "printing": true,
      "bomLines": [
        { "itemId": 12, "qtyPerKg": 0.44, "micron": "20" },
        { "itemId": 34, "qtyPerKg": 0.36, "micron": "12" },
        { "itemId": 7,  "qtyPerKg": 0.07, "micron": null }
      ]
    },
    { "itemId": 91, "expectedQtyKg": 2000, "printing": false, "bomLines": [...] }
  ]
}
```

The legacy `RequisitionItem` table is reused. Each entry in `finishedGoods` becomes one `RequisitionItem` row; `bomLines` becomes a `BomHeader` (one per `RequisitionItem`) + `BomLine` rows.

### 6.2 Costing slice (Phase A — adapted)

| Method | Path | Caller | Purpose |
|---|---|---|---|
| GET | `/api/costing/{requisitionId}` | Accountant, Admin | Read req with BOM + cost fields |
| PUT | `/api/costing/{requisitionId}/bom` | Accountant, Admin | Edit BOM lines (qty/KG, micron, add/remove RM); writes diff to `BomLine.LastModifiedBy*` and audit |
| PUT | `/api/costing/{requisitionId}/draft` | Accountant, Admin | Save costing draft (wastage %, RM purchase value/KG with currency, printing/FOH/transport/commission per KG) |
| POST | `/api/costing/{requisitionId}/submit` | Accountant, Admin | Transition `Costing → MdPricing`; freezes cost-side FX snapshot |

The BOM-stage endpoints (`/api/bom/...`) are **deleted** in Phase A. Sales submits BOM via the Requisitions create payload.

### 6.3 Approvals slice (Phase A — split)

The legacy `POST /api/approvals/{id}` (single approve+price action) is split into three endpoints:

| Method | Path | Caller | Purpose |
|---|---|---|---|
| POST | `/api/approvals/{id}/set-margin` | ManagingDirector, Admin | Stage 1: MD enters margin/KG (USD) per FG. Creates `QuotationApproval` row with `Stage=InitialPricing`, `IsApproved=false`, `RateSnapshot=today's rate`. Transitions `MdPricing → CustomerConfirm`. |
| POST | `/api/approvals/{id}/accept-customer` | SalesPerson (assigned), Admin | Sales confirms customer accepted. Transitions `CustomerConfirm → MdFinalSign`. Optional `customerFeedback` note. |
| POST | `/api/approvals/{id}/reject-customer` | SalesPerson (assigned), Admin | Sales reports customer rejected price. Transitions `CustomerConfirm → MdPricing`. Marks current `QuotationApproval` as superseded. (D21 — re-margin is the only path.) |
| POST | `/api/approvals/{id}/final-sign` | ManagingDirector, Admin | Stage 2: MD signs off. Marks current `QuotationApproval` as `IsApproved=true, Stage=FinalSign`. Generates signed PDF. Transitions `MdFinalSign → Signed`. Type-to-confirm dialog enforced client-side; backend validates `confirmationToken == "SIGN"`. |
| POST | `/api/approvals/{id}/reject` | ManagingDirector, Admin | MD rejects from `MdPricing` only. Transitions → `Rejected`. |

### 6.4 Customers / Items (Phase A — auto-Code)

| Method | Path | Change |
|---|---|---|
| POST | `/api/customers` | Server generates `Code = "CUST-" + nextCounter()`; payload `code` (if provided) is ignored |
| POST | `/api/items` | Server generates `Code = (Type==FinishedGood ? "FG-" : "RM-") + nextCounter()` |

Codes are immutable post-create (existing convention; no UI to edit Code anyway).

### 6.5 Profile slice (Phase A — NEW)

| Method | Path | Caller | Purpose |
|---|---|---|---|
| POST | `/api/profile/signature` | ManagingDirector | Upload signature image (PNG/JPG, max 500KB, 600×200px recommended). Saves to `User.SignatureImagePath`. |
| GET | `/api/profile/signature` | ManagingDirector (own) | Download URL of own signature |
| GET | `/api/users/{id}/signature` | Authenticated (used by PDF service) | Returns the signature image stream by user id |

Signatures are stored as files on disk (Fly volume in production, local filesystem in dev) at `/data/signatures/{userId}.png`. Path persisted in `User.SignatureImagePath`. One signature per MD user; re-upload overwrites.

### 6.6 Lookup slice (Phase A — additions)

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/customers/{id}/items` | Returns the implicit FG list for this customer (D20 — derived from past `RequisitionItem` rows for this customer; deduped by `itemId`). Used to populate the FG picker on `NewRequisitionPage`. |

### 6.7 Admin overrides (Phase A — adapted)

See §11 for the full adaptation map. Endpoints retained from V2.3-C with new state-machine guards.

---

## 7. Data Model Delta

### 7.1 Migrations (chronological, all in Phase A)

**V3_AddNewStatusValues** — adds enum values `CustomerConfirm`, `MdFinalSign`, `Signed`, `Cancelled` to the `RequisitionStatus` int column. No data backfill yet (cutover SQL handles in-flight rows in Phase C).

**V3_AddSignatureColumn** —
```sql
ALTER TABLE Users ADD SignatureImagePath VARCHAR(500) NULL;
```

**V3_AddBomLineAuditColumns** —
```sql
ALTER TABLE BomLine ADD LastModifiedByUserId INT NULL REFERENCES Users(Id);
ALTER TABLE BomLine ADD LastModifiedAt TIMESTAMPTZ NULL;
```

**V3_AddCodeCounters** —
```sql
CREATE TABLE CodeCounters (
  Sequence VARCHAR(20) PRIMARY KEY,  -- 'CUST', 'FG', 'RM'
  NextValue INT NOT NULL
);
INSERT INTO CodeCounters VALUES
  ('CUST', (SELECT COALESCE(MAX(CAST(SPLIT_PART(Code, '-', 2) AS INT)), 0) + 1
            FROM Customers WHERE Code LIKE 'CUST-%')),
  ('FG',   (SELECT COALESCE(MAX(...), 0) + 1 FROM Items WHERE Code LIKE 'FG-%' AND Type=0)),
  ('RM',   (SELECT COALESCE(MAX(...), 0) + 1 FROM Items WHERE Code LIKE 'RM-%' AND Type=1));
```

**V3_AddApprovalStage** —
```sql
-- 1. Add column nullable so legacy rows are not constrained yet
ALTER TABLE QuotationApproval ADD Stage INT NULL;
-- 2. Backfill legacy V2.3 approvals to FinalSign (they're already final)
UPDATE QuotationApproval SET Stage = 1 WHERE Stage IS NULL; -- 1 = FinalSign
-- 3. Make non-nullable + default for new rows
ALTER TABLE QuotationApproval ALTER COLUMN Stage SET NOT NULL;
ALTER TABLE QuotationApproval ALTER COLUMN Stage SET DEFAULT 0; -- 0 = InitialPricing
-- 4. Cost-side FX snapshot
ALTER TABLE QuotationApproval ADD CostFxSnapshot NUMERIC(18,6) NULL;
```

`Stage`: 0 = `InitialPricing` (Stage 1 — MD margin), 1 = `FinalSign` (Stage 2 — locked).
`CostFxSnapshot` = the FX rate used to convert foreign-currency RM purchase prices to AED at accountant submit time. Distinct from the existing `RateSnapshot` (= sale-side FX rate at MD margin entry).

**V3_AddCancelledFields** —
```sql
ALTER TABLE QuotationRequest ADD CancelledAt TIMESTAMPTZ NULL;
ALTER TABLE QuotationRequest ADD CancelledByUserId INT NULL REFERENCES Users(Id);
ALTER TABLE QuotationRequest ADD CancelReason VARCHAR(500) NULL;
```

### 7.2 New Domain types

**`RequisitionStatus`** — see §5 for new values.

**`ApprovalStage`** — `InitialPricing | FinalSign`.

**`Domain/Workflow/RequisitionStateMachine.cs`** (NEW class) — single source of truth for transition validity. Static methods:
```csharp
public static bool CanTransition(RequisitionStatus from, RequisitionStatus to);
public static bool IsTerminal(RequisitionStatus s);
public static RequisitionStatus[] AdminRollbackTargets(RequisitionStatus from);
```

**`Infrastructure/Services/CodeGeneratorService.cs`** (NEW) — atomic counter increments using a row-level lock on `CodeCounters`:
```csharp
Task<string> NextCustomerCode();        // → "CUST-0143"
Task<string> NextItemCode(ItemType t);  // → "FG-0088" or "RM-0035"
```
Uses `SELECT ... FOR UPDATE` inside the same EF Core transaction as the entity insert to prevent races.

### 7.3 Notification updates

New `NotificationType` enum values (Phase A migration):
- `MarginSet` (Stage 1 done — sent to sales + accountant)
- `CustomerConfirmRequested` (sent to sales)
- `CustomerAccepted` (sent to MD + accountant)
- `CustomerRejected` (sent to MD + accountant — re-margin needed)
- `Signed` (sent to sales + accountant)
- `RequisitionCancelled` (sent to sales)

The legacy `Approved` notification is retired but kept in the enum.

---

## 8. Frontend — Phase B

### 8.1 New pages

| Path | Component | Notes |
|---|---|---|
| `/requisitions/new` | `NewRequisitionPage` | **Major rewrite.** Customer + currency picker + N FG cards (each with `<BomEditorTable>` inline + Printing checkbox + per-FG BOM diff against past usage if customer has history) + Notes textarea + Save Draft / Submit. Uses `<CreateCustomerModal>` and `<CreateFinishedGoodModal>` and `<CreateRawMaterialModal>`. |
| `/requisitions/{id}/customer-confirm` | `CustomerConfirmPage` | Shows MD-priced quotation in tabular form + Customer Accepted button (green) + Request MD to Re-price button (yellow). Optional customer feedback textarea. Sales-only. |
| `/approvals/{id}/final` | `MdFinalSignPage` | PDF preview side-by-side with sign-off panel. Type-to-confirm modal ("Type SIGN to lock"). MD-only. |
| `/profile/signature` | `ProfileSignaturePage` | One-time MD signature upload; preview existing; replace flow. MD-only. |

### 8.2 Inline modals

| Modal | Trigger | Key fields |
|---|---|---|
| `<CreateCustomerModal>` | "+ Create new customer" link in customer picker | Name, Email, Phone, Address (Code preview only — auto-generated server-side) |
| `<CreateFinishedGoodModal>` | "+ Create new" in FG picker | Description (Code preview, Branch=Alain) |
| `<CreateRawMaterialModal>` | "+ Add raw material" → "create new" in BOM table | Description (Code preview, Branch=Alain) |

### 8.3 Removed pages

- `BomCreatorBomEditorPage` — gone
- Any standalone `/bom/...` entry-point pages
- BomCreator-specific dashboard widgets

### 8.4 Changed pages

- `RequisitionDetailPage` — new state-machine-driven action buttons; collapses BOM section into the main view (no separate "BOM tab"); shows BOM diff badges if accountant edited
- `CustomersPage` / `ItemsPage` — Code column read-only; auto-suggest Code preview on inline create
- `Dashboard` — drops BomPending/BomInProgress widgets; adds CustomerConfirm queue (sales) + MdFinalSign queue (MD)
- `AdminAuditLogPage` — adds new `ActionType` filters for V3 ops + new `Status` filter values

### 8.5 PWA service worker — minor update

Cache invalidation: bump `bom-api-list-cache` and `bom-api-detail-cache` to force a fresh fetch after deploy. No new SW strategies needed — V3 reuses existing NetworkFirst rules.

### 8.6 Type updates

- `bom-web/src/types/api.ts` — new `RequisitionStatus` literal, new approval/notification types, `Customer.code` and `Item.code` marked auto-generated
- `bom-web/src/api/queries/*` — new query hooks for `useCustomerImplicitItems(customerId)`, `useSignature(userId)`, `useSetMargin`, `useAcceptCustomer`, `useFinalSign`

---

## 9. Notification Map (D23 internal-only)

| Trigger | Sales | Accountant | MD |
|---|---|---|---|
| Sales submits requisition | — | 📩 | — |
| Accountant submits costs | 📩 | — | 📩 |
| MD sets margin (Stage 1) | 📩 | 📩 | — |
| Sales: customer accepted | — | 📩 | 📩 |
| Sales: customer rejected (re-margin) | — | 📩 | 📩 |
| MD final sign + lock | 📩 | 📩 | — |
| Admin cancels req | 📩 | — | — |
| MD rejects from MdPricing | 📩 | 📩 | — |

**No customer-facing emails ever.** Sales downloads PDF and shares with customer manually via own channels.

---

## 10. Migration Cutover (Phase C)

### 10.1 Pre-deploy

1. **Backup** — `pg_dump` snapshot of Neon prod DB (Neon's automated backup + manual snapshot)
2. **Export in-flight reqs** — admin user runs an export script (one-shot Python or SQL → Excel) capturing all reqs in `Draft / BomPending / BomInProgress / CostingPending / CostingInProgress / MdReview` with their RM lines + costs. Saved to `/exports/v3-cutover-inflight-{date}.xlsx` for sales reference.
3. **Email staff** — heads-up email 24h ahead: "V3 deploy on YYYY-MM-DD, 30-min downtime, in-flight reqs will be cancelled, please save anything important."

### 10.2 Deploy day

1. Announce 30-min maintenance window (status page or Slack)
2. Deploy backend (Phase A artifacts, already on master from Phase A merge)
3. Deploy frontend (Phase B artifacts)
4. Run cutover SQL (Phase C):

```sql
BEGIN;

-- Pre-flight: capture admin user id for audit author
DO $$ DECLARE admin_id INT; BEGIN
  SELECT Id INTO admin_id FROM Users WHERE Email = 'shan@fujairahplastic.com';
  IF admin_id IS NULL THEN RAISE EXCEPTION 'Admin user not found - cutover aborted'; END IF;
END $$;

-- 1. Cancel all in-flight requisitions (D11 hard cut-over)
--    Status int values: Draft=0, BomPending=1, BomInProgress=2,
--    CostingPending=3, CostingInProgress=4, MdReview=5, Cancelled=13
UPDATE QuotationRequest
SET Status = 13,                                    -- Cancelled (V3 enum slot)
    CancelledAt = NOW(),
    CancelledByUserId = (SELECT Id FROM Users WHERE Email = 'shan@fujairahplastic.com'),
    CancelReason = 'V3 hard cut-over migration 2026-XX-XX'
WHERE Status IN (0, 1, 2, 3, 4, 5);

-- 2. Hide non-Alain branches (D13)
UPDATE Branches SET IsActive = false WHERE Name <> 'Alain';

-- 3. Deactivate BomCreator user accounts (D12; UserRole.BomCreator = 2)
UPDATE Users SET IsActive = false WHERE Role = 2;

-- 4. Audit-log the migration (single AdminAuditLog row)
INSERT INTO AdminAuditLog (ActionType, AdminUserId, EntityType, EntityId, Reason, OccurredAt, BeforeJson, AfterJson)
VALUES ('V3CutoverMigration',
        (SELECT Id FROM Users WHERE Email = 'shan@fujairahplastic.com'),
        'System', 0,
        'V3 simplified workflow cutover',
        NOW(),
        json_build_object(
          'affectedReqs',          (SELECT COUNT(*) FROM QuotationRequest WHERE Status = 13 AND CancelReason LIKE 'V3 hard cut-over%'),
          'affectedBranches',      (SELECT COUNT(*) FROM Branches WHERE IsActive = false AND Name <> 'Alain'),
          'affectedBomCreators',   (SELECT COUNT(*) FROM Users WHERE IsActive = false AND Role = 2)
        )::text,
        '{}');

COMMIT;
```

**Idempotency:** re-running this block is safe — step 1 only flips rows still in statuses 0-5 (none after first run), steps 2-3 are no-ops on already-deactivated rows, step 4 inserts a duplicate audit row (harmless but flag for review).

5. **Smoke test (admin user, 10 min):**
   - Login → dashboard renders
   - Create customer (auto-code shows `CUST-XXXX`)
   - Create FG (auto-code `FG-XXXX`)
   - Create requisition with 1 FG + 2 RMs → submit → see `Costing` status
   - Login as accountant → review costs → submit
   - Login as MD → set margin → submit
   - Login as sales → accept customer → submit
   - Login as MD → final sign with type-to-confirm → see `Signed` + signed PDF download
   - Verify notifications fired for each step (no customer email)

6. **All-clear comms** — Slack / Email: "V3 live. Old reqs cancelled (export at /exports/...). Please re-create active quotations."

### 10.3 Rollback plan

If smoke fails at step 5:
- Restore Neon DB from snapshot (~5 min)
- Re-deploy V2.3-C P2 backend + frontend artifacts (held in `previous-prod` Fly slot or git tag `v2.3.0`)
- Re-issue staff comms

We accept up to 30 min of downtime for the rollback path.

---

## 11. Admin Override Adaptation (D27)

| V2.3-C op | V3 status | Adapted behaviour |
|---|---|---|
| C1 Hard delete req | ✅ kept | Works on any state. Cascade unchanged. |
| C2 Status rollback | ✅ adapted | New whitelist: `Signed → MdFinalSign`, `MdFinalSign → CustomerConfirm`, `CustomerConfirm → MdPricing`, `MdPricing → Costing`, `Costing → Draft` (Draft only via SP), `Cancelled` rollback blocked, `Rejected` rollback blocked. |
| C3 Reassign SP | ✅ kept | Works on any non-terminal state. |
| C4 Unlock BOM | ❌ removed | No BOM stage in V3. Endpoint deleted. |
| C5 Unlock Costing → "Rollback to Costing" | ✅ renamed/adapted | Allowed from `MdPricing` only. Status flips back to `Costing`. Endpoint path: `POST /api/admin/requisitions/{id}/rollback-to-costing`. |
| C6 Override Prices | ✅ adapted | Allowed on `Signed` only. Creates new `QuotationApproval` row marking previous as superseded; new `RateSnapshot` per V2.3-C P2 logic. **NEW guard:** type-to-confirm "OVERRIDE" since this breaks the Signed lock. Notes prefixed `[Override]`. |
| C7 Reset password | ✅ kept | Works for any user (incl. deactivated BomCreator if reactivated). |
| C8 Hard delete customer | ✅ kept | Cascade unchanged. |
| C9 Audit log | ✅ kept | Reads `AdminAuditLog`; UI adds new ActionType filter values. |

**Deleted endpoints:**
- `POST /api/admin/requisitions/{id}/unlock-bom` — gone (no BOM stage)

---

## 12. Risks & Mitigations

| # | Risk | Mitigation |
|---|---|---|
| R1 | In-flight req data loss on hard cut-over | Pre-export to Excel/PDF (§10.1 step 2) + email comms; Cancelled state preserves the row for reference (no actual delete) |
| R2 | MD signature image quality (handwritten upload) | UI guidelines: white background, high contrast, ~600×200px, ≤500KB; client-side preview before save; admin can re-trigger upload via reset path |
| R3 | Mobile drift Phase 1→D | V2 mobile read-only post-cutover (only Approved/Signed visible since Q11 cancels in-flight); track via `mobile-shipped-vc<N>` tag (CLAUDE.md discipline); plan Phase D ASAP after Phase 1 stable |
| R4 | PWA stale cache after deploy | Service worker `skip-waiting` + force reload on `controllerchange`; bump cache names; pre-deploy email asks staff to refresh once |
| R5 | 318+263 tests breaking on state-machine rename | Each phase boundary CI must be green before proceeding; backend-first phasing isolates the test refactor surface; pair-with-subagent code reviewer per CLAUDE.md V2.3-C P1 lesson |
| R6 | Customer.Code race on concurrent POST | `CodeGeneratorService` uses row-level `FOR UPDATE` lock on `CodeCounters` inside the same EF Core transaction as the entity insert; tested under concurrent load (simulated 10-thread test) |
| R7 | Admin override C6 on Signed breaks audit narrative | C6 audit row + supersession trail + notes-prefixed "[Override]" preserve forensic chain; matches existing V2.3-C P2 mechanism |
| R8 | Sales loses work mid-draft | Draft autosave on field blur (debounced 1s); Server-side draft persists across sessions; visible as "Drafts" tab on sales dashboard |
| R9 | FX rate drift between cost-side snapshot and sale-side snapshot | Two snapshots stored (`CostFxSnapshot` at accountant submit, `RateSnapshot` at MD margin); UI surfaces both at MdFinalSign for transparency; matches D21 |
| R10 | V2.3-A branch picker code becoming dead | Kept in code (Alain-only filter makes it a no-op); reactivation possible by re-enabling `Branch.IsActive=true` for other branches in the future |

---

## 13. Phasing

### Phase A — Backend foundation (3–4 sessions)

**Branch:** `feat/v3-backend-foundation` off `master`

Tasks (rough order):
1. `RequisitionStatus` enum + migration `V3_AddNewStatusValues`
2. `RequisitionStateMachine` class + unit tests
3. `CodeGeneratorService` + migration `V3_AddCodeCounters` + concurrent-insert test
4. `Customers/Items` POST endpoints — auto-Code; payload Code field ignored
5. `Requisitions` POST/PUT — accept BOM payload; transition guard via state machine
6. `Costing` PUT/SUBMIT — editable BOM with diff to `BomLine.LastModifiedBy*`; FX cost snapshot
7. `Approvals` split into 3 endpoints (`SetMargin`, `AcceptCustomer`, `FinalSign`) + `RejectCustomer`
8. Migration `V3_AddSignatureColumn` + `Profile/SignatureController`
9. `PdfService.GenerateSignedPdf()` — embeds signature image + text
10. Drop `BomCreator` from authorization helpers (keep enum value for legacy data)
11. Adapt admin override controllers (delete UnlockBom, rename UnlockCosting, expand C6 to handle Signed)
12. Notification enum additions + `NotificationService.SendAsync` calls per §9
13. Backend tests green (target: 318 → ~340 with new V3 tests)
14. `dotnet build` clean, `dotnet test` green, manual swagger smoke for all new endpoints

**Exit criteria:** merged to `master`; deployed to a **staging Fly slot** (separate app or `staging` machine on the existing app) for end-to-end testing with the V3 frontend in Phase B; **production stays on V2.3 backend until Phase C cutover** (no feature flag — coordinated A+B+C deploy is simpler than runtime gating). CI green.

### Phase B — Frontend overhaul (3–4 sessions)

**Branch:** `feat/v3-frontend` off Phase A merge

Tasks:
1. New `RequisitionStatus` literals in `types/api.ts` + new query/mutation hooks
2. `<NewRequisitionPage>` rewrite (combined sales screen with BOM editor inline)
3. `<CustomerConfirmPage>` + accept/reject flow
4. `<MdFinalSignPage>` + type-to-confirm dialog
5. `<ProfileSignaturePage>` + image preview/upload
6. 3 inline create modals (Customer, FG, RM)
7. `<RequisitionDetailPage>` rework — new state-driven actions
8. Dashboard refactor (drop BOM widgets, add CustomerConfirm + MdFinalSign queues)
9. Admin pages — `<AdminActionsCard>` updates per §11; audit log filter additions
10. PWA cache bump
11. Web tests green (target: 263 → ~290)
12. `tsc --noEmit` clean, `npm run build` clean, `npm test` green; manual smoke 7-step happy path

**Exit criteria:** end-to-end happy path works against Phase A backend on `staging` Fly slot; CI green.

### Phase C — Migration cutover (1 session)

**Branch:** none — direct production work, scripted

Tasks:
1. Migration SQL file at `BomPriceApproval.API/Infrastructure/Data/V3CutoverScript.sql`
2. Pre-flight export script at `scripts/v3-cutover-export-inflight.py` (or .sql + ClosedXML)
3. Deploy day runbook at `docs/runbooks/v3-cutover-runbook.md`
4. Execute runbook
5. Smoke test
6. All-clear comms

**Exit criteria:** Production on V3, in-flight reqs cancelled+exported, smoke 7/7 passing, no rollback triggered.

### Phase D — Mobile V3 (later, separate brainstorm)

Deferred. Once Phase 1 stable for ≥1 week, brainstorm mobile V3 scope (likely a thinner client — view-only for sales/accountant + MD final-sign push notif + signature on-phone).

---

## 14. Definition of Done

- All backend tests green (target ~340 passing, 0 new flakes)
- All web tests green (target ~290 passing)
- `tsc --noEmit` clean, `dotnet build` clean
- New mockup screens match v1.3 spec (mockup is reference for UX intent)
- Admin can create customer + FG + RM with auto-codes (smoke)
- Sales happy path 7 steps complete (smoke)
- MD final sign produces signed PDF with embedded signature image (smoke)
- No customer-facing emails fire at any stage (verified by SMTP log inspection)
- 318 V2.3 tests still green where applicable; obsolete tests deleted with explicit `// Replaced by V3 ...` reference
- Cutover SQL is idempotent (re-running doesn't double-cancel) — verified by dry-run on a Neon branch DB
- Audit log shows V3 cutover row + at least one of each new action type after smoke
- Production smoke 7/7 within 30-min window of deploy
