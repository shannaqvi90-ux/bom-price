# Mobile V2.0 — MD All-Requisitions List + Approved Item Prices

**Date:** 2026-04-24
**Author:** Shan (with Claude brainstorm)
**Status:** Approved design, ready for implementation plan

---

## Context

Mobile V1 shipped with a Sales stack (create/list/detail requisitions) and an MD stack limited to pending approvals (`status = MdReview`). Post-V1 phone smoke surfaced four enhancement requests:

1. MD should see **all** requisitions with search + filter (currently only pending).
2. Detail page should show **approved item prices** so salesperson can quote customers.
3. PDF should be **requisition-level** (clarified: already is — not an issue).
4. Accountant role needs a mobile flow (**deferred to V2.1** — separate spec).

This document covers items 1 and 2 as **V2.0 — quick wins**. V2.1 (Accountant stack) is deferred.

---

## Scope

### In scope

- **MD list screen:** Enhance existing `(md)/pending.tsx` with search + 6 status chips. Default behavior (tap dashboard → pending list) preserved via default chip = `MD review`.
- **Approved item prices:** Show unit price + line total on item cards when `status === "Approved"`, on both `(sales)/[id].tsx` and `(md)/[id].tsx`.
- **API extensions:** `GET /api/requisitions` accepts multi-value `status` + `search` query params. Verify `GET /api/requisitions/{id}` returns `approval.items[].price` for approved requisitions.

### Out of scope (deferred)

- Accountant mobile stack — V2.1 separate spec.
- Date-range filter on MD list — future polish.
- Chip live counts ("BOM 30") — requires extra API calls; defer.
- Sort/order customization — default `createdAt desc` only.
- Margin display on mobile — explicitly hidden per role-visibility rules.

---

## Role-based visibility constraint

Business rule (stated by Shan during brainstorm, saved to memory):

| Role | Sees |
|---|---|
| Salesperson | Sales data. **HIDE margin, HIDE costing.** |
| BOM creator | BOM data. **HIDE sales price.** |
| Accountant | Everything. |
| MD | Everything. |

V2.0 impact:
- Sales detail shows `unit price + line total` only. No margin. No cost.
- MD detail shows same display. MD already saw margin during approval decision; historical detail view matches Sales for consistency and simplicity.
- Server-side API is the authority — mobile displays what API returns. No client-side role branching in V2.0.

---

## Design

### 1. MD list screen (`bom-mobile/app/(md)/pending.tsx`)

#### Layout (top to bottom)

```
┌──────────────────────────────────┐
│ ScreenHeader                     │
│   label:  "MANAGING DIRECTOR"    │
│   title:  "Requisitions"         │
│   count:  total filtered results │
│   right:  NotificationBell +     │
│           Log out                │
├──────────────────────────────────┤
│ [🔍 Search REQ-xxxx or customer] │ ← TextInput, debounced 300ms
├──────────────────────────────────┤
│ [All][BOM][Costing][MD review▼]  │ ← horizontal scroll chips
│ [Approved][Rejected]              │
├──────────────────────────────────┤
│ <RequisitionCard list — infinite>│ ← existing FlatList
└──────────────────────────────────┘
```

#### Chip-to-status mapping

```ts
const CHIP_STATUSES: Record<ChipLabel, RequisitionStatus[]> = {
  "All":        [],  // no status filter — all
  "BOM":        ["BomPending", "BomInProgress"],
  "Costing":    ["CostingPending", "CostingInProgress"],
  "MD review":  ["MdReview"],
  "Approved":   ["Approved"],
  "Rejected":   ["Rejected"],
};
```

#### State

- `activeChip: ChipLabel` — default `"MD review"` (preserves V1 entry-point behavior)
- `searchQuery: string` — debounced 300ms before query key updates
- `useInfiniteQuery` key: `[...requisitionKeys.list(), "mdList", { statuses, search }]` — changing chip or search triggers refetch
- `pageSize = 20` (unchanged)

#### Visual style

- Chips: matches the sales redesign token style — 1px border, `#cbd5e1` inactive / `#1e40af` active, 5/10 padding, `999` border-radius, horizontal scroll with trailing fade gradient.
- Search input: `Input`-like border treatment, `#f8fafc` bg, border `#cbd5e1`, 10px radius, 9/12 padding, 🔍 prefix.
- Empty states vary by chip (e.g., "All caught up" for `MD review`, "No requisitions in {chip} stage" for others).

#### Header title

Change from `"Pending approvals"` to `"Requisitions"` — neutral title now that all statuses are visible.

---

### 2. Approved item prices (`(sales)/[id].tsx` and `(md)/[id].tsx`)

#### Visual — Approved item card

```
┌─────────────────────────────────────┐
│ {description}              {qty}    │
│ ✓ BOM  ✓ Costing  ✓ Price            │ ← ItemStageBadge
│ ─────────────────────────────────── │ ← 1px #f1f5f9 divider
│ Unit price           AED 125.00     │
│ Line total           AED 2,500.00   │ ← bold, #0f172a
└─────────────────────────────────────┘
```

#### Non-approved statuses

Card unchanged (description + qty + ItemStageBadge). No price block. No visual regression.

#### Data source

- Detail endpoint `GET /api/requisitions/{id}` returns `approval.items[]` for approved requisitions.
- Unit price: `approval.items[i].price` (decimal, DB precision 18,4).
- Line total: computed client-side = `item.expectedQty × approvalItem.price`.
- Currency code: `requisition.currencyCode`.

#### Formatting

- Currency prefix: `"{currencyCode} "` (space after).
- Number: `Intl.NumberFormat("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })`.
- Likely a candidate helper in `utils/numbers.ts` — reuse existing or add.

#### Shared component

Extract `<ItemPriceBlock item={item} approvalItem={approvalItem} currencyCode={...} />` to `src/components/ItemPriceBlock.tsx` (parallel to `ItemCardShell`). Render inside the item card body after `ItemStageBadge`, only when `status === "Approved"` and `approvalItem` exists.

#### Edge cases

| Condition | Behavior |
|---|---|
| `status === "Approved"` but `approval.items[i]` missing | Don't render price block. Log once to console.warn. Defensive — API invariant violated. |
| Price is `0` | Render `"AED 0.00"` — MD intentionally set zero. |
| `status === "Rejected"` | No price block. Rose rejection box already handles this context. |
| `status === "MdReview"` | No price block. MD's approval review screen handles pricing. |

---

### 3. API changes

#### `GET /api/requisitions` — query param extensions

**Current:** Supports single `status=<value>` query param.

**Changes:**
1. Accept **multi-value** `status` — `?status=BomPending&status=BomInProgress` binds to `string[]` via `[FromQuery]`.
2. Add **`search`** query param — case-insensitive partial match on `RefNo` OR `Customer.Name`.
3. Branch isolation unchanged (Sales: own branch; MD/Admin/Accountant: all branches).

Pseudocode:
```csharp
public async Task<IActionResult> List(
    [FromQuery] string[]? status,
    [FromQuery] string? search,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 20)
{
    var q = db.Requisitions
        .Include(r => r.Customer)
        .ApplyBranchIsolation(User);

    if (status is { Length: > 0 })
        q = q.Where(r => status.Contains(r.Status));

    if (!string.IsNullOrWhiteSpace(search))
    {
        var term = search.Trim().ToLower();
        q = q.Where(r =>
            EF.Functions.ILike(r.RefNo, $"%{term}%") ||
            EF.Functions.ILike(r.Customer.Name, $"%{term}%"));
    }

    return Ok(await q.OrderByDescending(r => r.CreatedAt)
                     .Skip((page - 1) * pageSize).Take(pageSize)
                     .Select(...).ToListAsync());
}
```

Performance note: 3000+ requisitions with `ILIKE` + 20-row pagination — acceptable without index in development. If prod load becomes an issue, add a composite index on `(BranchId, Status, CreatedAt)` and/or trigram index on `Customer.Name`.

#### `GET /api/requisitions/{id}` — verify approved prices included

Expected: when `status === "Approved"`, response includes `approval.items[]` with `price`. Most likely already works (the approval flow stores per-item prices in `ApprovalItem`). **Implementation plan must verify this against the current DTO/serializer before proceeding** — if missing, extend DTO.

---

## Testing

### Mobile (Jest)

- `mapChipToStatuses` unit test — each chip returns correct status array; `All` returns `[]`.
- Search debounce — simulate typing, assert query fires once after 300ms.
- `ItemPriceBlock` snapshot — renders correctly for Approved, omits for other statuses.
- Format helper — `formatCurrency(125.5, "AED")` → `"AED 125.50"`.

### Backend (xUnit)

- Multi-status query: `?status=BomPending&status=BomInProgress` returns union of matching requisitions.
- Search: matches refNo partial + customer name partial, case-insensitive.
- Branch isolation preserved — SalesPerson scoped to own branch even with new filters.

### Manual phone smoke

- All 6 chips filter correctly on MD list screen.
- Search debounced, matches refNo and customer name.
- Approved Sales detail shows unit price + line total; non-Approved no block.
- Approved MD detail shows same. Rejected detail unchanged (rose box).

---

## Deliverables

- This spec: `docs/superpowers/specs/2026-04-24-mobile-v2-md-list-prices-design.md`
- Implementation plan: `docs/superpowers/plans/YYYY-MM-DD-mobile-v2-md-list-prices.md` (next step via `superpowers:writing-plans`).
- Feature branch: `feature/mobile-v2-md-list-prices` (created at start of implementation).
- Commits: phased (backend first, then mobile list, then mobile price block), verify tsc + jest + build green between phases.
- Phone smoke before merge.
- No push to remote (security blocker still in place — V2.0 is local-merge only).

---

## Open questions

- **Does `GET /api/requisitions/{id}` currently return `approval.items[].price` in the DTO?** Plan phase must verify by reading the existing `RequisitionDetailDto` (or equivalent). If missing, extending it is a ~5-line change but adds to plan scope.
- **Is there an existing `formatCurrency` helper in `bom-mobile/src/utils/numbers.ts`?** Reuse if yes, add if no. Minor.

Both are plan-phase investigations, not blockers for spec approval.
