# Customer Creation by Sales / Accountant — Design Spec

**Date:** 2026-04-23
**Author:** Shan (with Claude assistance)
**Status:** Draft — pending user review
**Approach:** #2 (full scope) from brainstorming session

---

## 1. Problem

Salespersons and Accountants cannot create a customer from within the flow that needs one. Today:

- **Sales (web + mobile):** On **New Requisition**, if the customer is missing from the picker, the user must abandon the form, navigate to the Customers page, add the customer, come back, and restart. Mobile has no Customers page at all.
- **Accountant:** Has no access to the Customers page, cannot create customers, and cannot create requisitions. If an accountant notices a wrong customer on a requisition mid-costing, they have no way to correct it.
- **Backend:** `POST /api/customers` already allows `SalesPerson, Admin` but not `Accountant`. `POST /api/requisitions` is `SalesPerson` only.

## 2. Goals

1. Sales can add a new customer inline from the requisition form (web + mobile) and have it auto-selected on creation.
2. Accountant can add/edit customers and create requisitions (web only for V1).
3. Accountant can change the customer on an existing requisition during the costing stage, with an auditable history.
4. MD and Sales can see when a customer has been changed on a requisition they are reviewing.

## 3. Non-goals

- Mobile Accountant stack / mobile change-customer flow — out of scope for V1 (mobile has no accountant screens yet).
- Branch reassignment — a requisition stays on its original branch even if customer changes.
- Customer merge / dedup / cleanup of the 1000+ test records with `<script>alert(1)</script>` names — pre-existing, tracked separately.
- Customer BranchId modeling — customers remain global (no per-branch isolation).

## 4. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           Backend (ASP.NET)                              │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────┐   │
│  │ CustomersCtrl    │  │ RequisitionsCtrl │  │ CustomerChangeHist   │   │
│  │ +Accountant role │  │ +Accountant role │  │ (new entity + table) │   │
│  │                  │  │ +PATCH /{id}/    │  │                      │   │
│  │                  │  │   customer       │  │                      │   │
│  │                  │  │ +GET /{id}/      │  │                      │   │
│  │                  │  │   customer-      │  │                      │   │
│  │                  │  │   history        │  │                      │   │
│  └──────────────────┘  └──────────────────┘  └──────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
                                  ▲
                                  │ HTTPS
           ┌──────────────────────┼──────────────────────┐
           │                      │                      │
    ┌──────▼──────┐        ┌──────▼──────┐       ┌──────▼──────┐
    │ Web (Sales) │        │ Web (Acct)  │       │ Mobile (Sls)│
    │ New+inline  │        │ Sidebar+    │       │ (sales)/new │
    │ create cust │        │ Costing     │       │ +inline     │
    │             │        │ change cust │       │ create cust │
    └─────────────┘        └─────────────┘       └─────────────┘
```

## 5. Backend Design

### 5.1 Permission additions

| Endpoint | Current roles | New roles |
|---|---|---|
| `POST /api/customers` | SalesPerson, Admin | SalesPerson, Admin, **Accountant** |
| `PUT /api/customers/{id}` | SalesPerson, Admin | SalesPerson, Admin, **Accountant** |
| `POST /api/requisitions` | SalesPerson | SalesPerson, **Accountant** |
| `POST /api/requisitions/{id}/items` | SalesPerson | SalesPerson, **Accountant** |
| `DELETE /api/requisitions/{id}/items/{rid}` | SalesPerson | SalesPerson, **Accountant** |
| `POST /api/requisitions/{id}/resubmit` | SalesPerson | SalesPerson, **Accountant** |

### 5.2 Accountant requisition creation — branch handling

**Reality check (2026-04-23):** The seeded `sara@test.com` accountant has `BranchId = fujairahBranchId` (see `Program.cs`), and `ItemEditTests` enforce branch-scoped accountants (`EditItem_AsAccountant_CrossBranch_Returns403`). CLAUDE.md's "accountants have null BranchId" claim is outdated.

**Decision:** Accountant uses JWT `BranchId` exactly like SalesPerson. No contract change to `CreateRequisitionRequest`. The existing null-branch guard (`if (CurrentBranchId is null) return 400`) stays — this blocks Admin + any future branchless account from creating requisitions, which matches current behavior.

Validation stays as today:
- Caller must have a branch-assigned JWT — else 400
- Customer existence check as today (accountant bypasses the `SalesPersonId == CurrentUserId` ownership filter, because accountants are not the customer owner — see controller line 162-164 logic, which already allows non-SalesPerson roles to use any customer)

The created requisition's `SalesPersonId` remains set to the caller's UserId regardless of role.

### 5.3 New endpoint: change customer

**`PATCH /api/requisitions/{id}/customer`**

- **Auth:** `[Authorize(Roles = "Accountant,Admin")]`
- **Body:**
  ```json
  {
    "customerId": 42,
    "reason": "Corrected — was assigned to wrong subsidiary"
  }
  ```
  `reason` optional, max 500 chars.
- **Validations:**
  1. Requisition exists → else 404
  2. Status ∈ `{CostingPending, CostingInProgress}` → else 400 `"Customer can only be changed during the costing stage"`
  3. `customerId` exists → else 404
  4. `customerId` != current `CustomerId` → else 400 `"New customer is the same as the current customer"`
- **Effect (single transaction):**
  1. Update `QuotationRequest.CustomerId = req.CustomerId`
  2. Bump `QuotationRequest.UpdatedAt = now`
  3. Insert `CustomerChangeHistory` row
  4. Emit SignalR `customerChanged` event to MD group + originating SalesPerson user group. Payload: `{ requisitionId, oldCustomerId, oldCustomerName, newCustomerId, newCustomerName, changedBy, reason }`
  5. Persist one `Notification` row (type = `customer_changed`, recipient = originating SalesPerson) with a human-readable message like `"Customer on REQ-0042 changed from Acme Ltd to Acme Industries LLC by Sara"`
- **Response:** `204 No Content`

### 5.4 New endpoint: read change history

**`GET /api/requisitions/{id}/customer-history`**

- **Auth:** Any authenticated user who can see the requisition (reuses existing `GET /api/requisitions/{id}` branch-isolation rules — SalesPerson sees own branch; others see all).
- **Response:**
  ```json
  [
    {
      "id": 7,
      "oldCustomerId": 12,
      "oldCustomerName": "Acme Ltd",
      "newCustomerId": 42,
      "newCustomerName": "Acme Industries LLC",
      "changedByUserId": 3,
      "changedByUserName": "Sara",
      "changedAt": "2026-04-23T10:15:00Z",
      "reason": "Corrected — was assigned to wrong subsidiary"
    }
  ]
  ```
  Ordered by `ChangedAt DESC`.

### 5.5 New entity + migration

```csharp
// Domain/Entities/CustomerChangeHistory.cs
public class CustomerChangeHistory
{
    public int Id { get; set; }
    public int RequisitionId { get; set; }
    public int OldCustomerId { get; set; }
    public int NewCustomerId { get; set; }
    public int ChangedByUserId { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public string? Reason { get; set; }

    public QuotationRequest Requisition { get; set; } = null!;
    public Customer OldCustomer { get; set; } = null!;
    public Customer NewCustomer { get; set; } = null!;
    public User ChangedBy { get; set; } = null!;
}
```

**Column types:**
- `Id` — serial PK
- `Reason` — `varchar(500)` nullable
- `ChangedAt` — `timestamptz`, default `now()`

**FKs:**
- `RequisitionId` → `QuotationRequests(Id)` ON DELETE CASCADE
- `OldCustomerId` / `NewCustomerId` → `Customers(Id)` ON DELETE RESTRICT
- `ChangedByUserId` → `Users(Id)` ON DELETE RESTRICT

**Indexes:**
- `IX_CustomerChangeHistory_RequisitionId`
- `IX_CustomerChangeHistory_ChangedAt` (DESC for timeline queries)

**Migration name:** `20260423_AddCustomerChangeHistory`

## 6. Web UI Design

### 6.1 Sidebar.tsx

Add `"Accountant"` to the `roles` array for:
- `Requisitions` link
- `New Requisition` link
- `Customers` link

Verify no other consumer of the role gates. Existing Accountant dashboard continues to show "Costing Queue".

### 6.2 NewRequisitionPage.tsx

- Next to the Customer field, add a small text-link button `+ Add new customer` (right-aligned, subtle primary-color)
- Clicking opens the existing `AddCustomerModal` component (no changes needed)
- On modal success callback:
  - Invalidate `useCustomers` query (so the picker refreshes)
  - Set `customer` form value to the newly created customer (auto-select)
  - Close modal, show success toast `"Customer added"`
- No branch picker — accountant submits with JWT branch like salesperson

### 6.3 Costing flow — Change customer

**Location:** `CostingEntryPage` header card (the summary panel that shows Req No / Customer / Status).

**Gating:**
- Role ∈ `{Accountant, Admin}`
- Requisition status ∈ `{CostingPending, CostingInProgress}`

**UI:**
- Current customer rendered as read-only chip/label
- Right-side: `Change customer` button (ghost variant)
- Click → opens new `ChangeCustomerModal` component:
  - Read-only "Current customer: Acme Ltd (ACM-001)"
  - Required: SearchableSelect for new customer + inline `+ Add new customer` link (reuses AddCustomerModal, same auto-select flow)
  - Optional: Reason textarea (placeholder: "Why is this changing? (optional, visible in audit history)"), max 500 chars
  - Confirm button (disabled until new customer differs from current)
- On submit:
  - PATCH /api/requisitions/{id}/customer
  - On 204 → invalidate requisition detail query, close modal, toast `"Customer changed. Logged in audit history."`
  - On 4xx → show error inside modal (don't close)

### 6.4 Audit history visibility

**New component:** `CustomerHistoryModal` (read-only timeline).

**Where it appears:**
- `CostingEntryPage` header: if history count > 0, small `View history (N)` link next to `Change customer` button
- `RequisitionDetailPage`: amber badge `"Customer changed (N times)"` → opens modal
- `MdReviewPage`: same amber badge → opens modal

Modal content: vertical timeline of entries, each showing old → new customer names, user, timestamp, reason (if any).

### 6.5 Lookups API

No new lookup hooks required. Branch selection dropped per 5.2.

## 7. Mobile UI Design

### 7.1 `(sales)/new.tsx`

- Below the Customer `SearchablePicker`, add a subtle text button `+ New customer` (primary-color, self-start)
- Tap → haptic + opens a Moti slide-up bottom sheet (`CustomerQuickCreateSheet`) with fields:
  - Code (required, max 20)
  - Name (required, max 200)
  - Address (max 500)
  - Email (valid email or empty)
  - Phone (max 50)
- Submit:
  - POST /api/customers
  - On 201: invalidate `useCustomers`, set form `customerId` to new id, close sheet, success haptic
  - On 409 (duplicate code): inline field error in sheet
  - On other 4xx: error banner in sheet
- Tapping backdrop dismisses the sheet (unless form is dirty and mid-submit)

### 7.2 Accountant mobile + change-customer mobile

**Out of scope for V1.** Flag in the mobile plan status doc.

## 8. Error Handling / Edge Cases

| Scenario | Behavior |
|---|---|
| Duplicate customer code on create | 409 → modal shows field error on `Code` |
| Caller (Accountant/Sales) without JWT BranchId | 400 `"A branch-assigned sales person is required"` (existing guard, unchanged) |
| Change customer outside allowed states | 400 → modal shows top error, stays open |
| Change customer to same id | 400 `"No change"` (button should prevent, but backend enforces) |
| Network failure mid-submit | Modal stays open, error banner, user can retry |
| Stale JWT (Accountant role revoked) | 403 → toast + redirect handled by global axios interceptor |
| Customer being changed while MD is mid-review | Not possible — MdReview state blocks the PATCH (400) |
| Two accountants race change-customer simultaneously | Last write wins (no optimistic concurrency V1); both audit rows present |

## 9. Testing Plan

### 9.1 Backend tests (`BomPriceApproval.Tests`)

**Extend `CustomersCrudTests`:**
- `Create_AsAccountant_Succeeds`
- `Update_AsAccountant_Succeeds`

**Extend `RequisitionWorkflowTests`:**
- `Create_AsAccountant_WithJwtBranch_Succeeds` (Sara = branch 1, req created on branch 1)
- `Create_AsAccountant_BranchIsolation_InheritsJwtBranch` (created req's BranchId == JWT BranchId)

**New `Requisitions/ChangeCustomerTests.cs`:**
- `ChangeCustomer_AsAccountant_InCostingPending_Succeeds_AndLogsHistory`
- `ChangeCustomer_AsAccountant_InCostingInProgress_Succeeds`
- `ChangeCustomer_OutsideAllowedStates_Returns400` (parameterized: BomPending, BomInProgress, MdReview, Approved, Rejected)
- `ChangeCustomer_SameCustomer_Returns400`
- `ChangeCustomer_NonExistentCustomer_Returns404`
- `ChangeCustomer_NonExistentRequisition_Returns404`
- `ChangeCustomer_AsSales_Returns403`
- `ChangeCustomer_AsBomCreator_Returns403`
- `ChangeCustomer_AsMd_Returns403`
- `GetCustomerHistory_ReturnsEntriesOrderedDesc`
- `GetCustomerHistory_EmptyWhenNoChanges_Returns200_WithEmptyArray`
- `GetCustomerHistory_BranchIsolation_SalesPersonOnOwnBranch`

### 9.2 Web tests

**`NewRequisitionPage.test.tsx`:**
- `shows_add_customer_button_and_opens_modal`
- `auto_selects_newly_created_customer_after_modal_save`

**`CostingEntryPage.test.tsx`:**
- `shows_change_customer_button_for_accountant_in_costing_pending`
- `hides_change_customer_button_for_bomcreator`
- `hides_change_customer_button_when_status_is_mdreview`
- `change_customer_flow_happy_path`
- `change_customer_shows_validation_error_when_same_customer`

**`RequisitionDetailPage.test.tsx` / `MdReviewPage.test.tsx`:**
- `shows_customer_change_badge_when_history_exists`
- `hides_customer_change_badge_when_no_history`
- `opens_history_modal_on_badge_click`

### 9.3 Mobile tests

- Manual smoke: Sales tap `+ New customer` → fills sheet → saves → new customer appears selected
- Jest unit: validation schema for customer quick-create form (mirrors backend max-length rules)

### 9.4 Integration test — not mocked

Backend tests use the existing Testcontainers Postgres pattern (no database mocking) per CLAUDE.md testing rules.

## 10. Rollout Strategy

Batched commits per `feedback_batched_commits.md` — plan presented, executed end-to-end, reported at close.

Proposed commit order:

1. `feat(api): allow accountant to create/update customers and create requisitions`
2. `feat(api): PATCH /api/requisitions/{id}/customer + CustomerChangeHistory entity + migration`
3. `feat(api): GET /api/requisitions/{id}/customer-history`
4. `feat(web): add customers + new-requisition sidebar links for accountant`
5. `feat(web): inline "add customer" on NewRequisitionPage with branch picker for accountant`
6. `feat(web): change-customer modal on costing + history badge/modal`
7. `feat(mobile): inline "add customer" bottom-sheet on sales/new`

Each commit keeps `dotnet build` green + its own tests passing.

## 11. Open Questions (none outstanding)

All four brainstorm-time questions resolved:

1. Accountant BranchId → form picker, enforced server-side
2. Change-customer reason → optional, max 500 chars
3. Change after MdReview → blocked (400)
4. Notify → SignalR + DB row for SalesPerson + MD

## 12. Out-of-scope carry-overs

- Mobile Accountant stack (no mobile UI for costing yet — future plan)
- Mobile change-customer (depends on Accountant mobile stack)
- Customer merge / dedup tooling
- Cleanup of 1000+ XSS-payload customer names in DB (pre-existing, separate work)
- Backend validation rejecting `<script>` in customer fields (separate security spec)
