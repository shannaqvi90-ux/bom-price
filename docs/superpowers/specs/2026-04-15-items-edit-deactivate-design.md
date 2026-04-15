# Items Edit & Deactivate Design

## Goal

Allow Admin and Accountant users to edit item details and toggle item active status from the Items list page.

## Decisions Made

| Question | Decision |
|---|---|
| Who can edit/deactivate | Admin + Accountant |
| Deactivation effect on existing BOMs | None — existing BOM lines keep working; item removed from future BOM dropdowns only |
| UI trigger | Inline row action buttons (✏️ Edit, 🚫 Deactivate / ✅ Reactivate) |
| Inactive items in list | Hidden by default; "Show inactive" toggle reveals them, grayed out |
| Reactivation | ✅ button replaces 🚫 on inactive rows |

---

## Backend

### New Endpoints

Both restricted to `[Authorize(Roles = "Admin,Accountant")]`.

**`PUT /api/items/{id}`** — Edit item fields

Request body (`UpdateItemRequest`):
```json
{
  "code": "RM-PE100",
  "description": "PE100 Resin",
  "type": "RawMaterial",
  "lastPurchasePrice": 4.2500
}
```

- Returns `204 No Content` on success
- Returns `404` if item not found
- Returns `403` if Accountant tries to edit an item outside their branch
- Returns `409` if `code` already exists on a different item in the same branch

**`PATCH /api/items/{id}/status`** — Toggle active state

Request body (`UpdateItemStatusRequest`):
```json
{ "isActive": false }
```

- Returns `204 No Content` on success
- Returns `404` if item not found
- Returns `403` if Accountant tries to update an item outside their branch
- No cascade — existing BOM lines referencing this item are unaffected

### Branch Isolation

- Admin (`branchId = null`): can edit/deactivate any item across all branches
- Accountant (`branchId = N`): scoped to own branch; cross-branch requests return 403

### New DTOs (in `ItemDtos.cs`)

```csharp
public record UpdateItemRequest(string Code, string Description, ItemType Type, decimal? LastPurchasePrice);
public record UpdateItemStatusRequest(bool IsActive);
```

### Modified Files

- `BomPriceApproval.API/Features/Items/ItemsController.cs` — add `PUT /{id}` and `PATCH /{id}/status` actions
- `BomPriceApproval.API/Features/Items/ItemDtos.cs` — add two new request records

### New Test File

`BomPriceApproval.Tests/Items/ItemEditTests.cs`

Test cases:
1. `EditItem_AsAdmin_Succeeds` — Admin edits item across any branch
2. `EditItem_AsAccountant_OwnBranch_Succeeds` — Accountant edits own-branch item
3. `EditItem_AsAccountant_CrossBranch_Returns403`
4. `EditItem_DuplicateCode_Returns409`
5. `DeactivateItem_AsAdmin_ItemDisappearsFromDefaultList`
6. `ReactivateItem_AsAdmin_ItemAppearsAgain`

---

## Frontend

### New File

**`bom-web/src/features/items/EditItemModal.tsx`**

- Mirrors `AddItemModal.tsx` structure
- Pre-populates all form fields from the selected `Item`
- Same Zod validation (code required, description required, type enum, lastPurchasePrice optional positive number)
- On submit: calls `useUpdateItem()` mutation
- On success: closes modal, invalidates item list
- Does not include `isActive` toggle — deactivation is a separate row action

### Modified Files

**`bom-web/src/features/items/ItemListPage.tsx`**

- Adds `actions` column to DataTable, rendered only for Admin + Accountant roles
- ✏️ Edit button: opens `EditItemModal` with the row's item pre-filled
- 🚫 / ✅ button: calls `useUpdateItemStatus()` with toggled value; shows browser `confirm()` dialog before deactivating (`"Deactivate [item description]? It will no longer appear in BOM dropdowns."`)
- "Show inactive" toggle (checkbox or switch) above the table header, default `false`
  - When `false`: filters out rows where `isActive === false` client-side
  - When `true`: shows all rows; inactive rows rendered with `opacity-50`

**`bom-web/src/features/items/itemsApi.ts`**

```typescript
// Edit item fields
export function useUpdateItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: UpdateItemRequest }) =>
      api.put(`/items/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

// Toggle active status
export function useUpdateItemStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, isActive }: { id: number; isActive: boolean }) =>
      api.patch(`/items/${id}/status`, { isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}
```

**`bom-web/src/features/items/ItemListPage.test.tsx`** — add 2 tests:

1. Admin sees edit + deactivate buttons; BomCreator does not
2. Inactive items hidden by default; visible when "Show inactive" is toggled on

### No Changes Needed

- `bom-web/src/types/api.ts` — `Item` interface already has `isActive` and all editable fields
- `GET /api/items` backend — always returns all items (active and inactive). Filtering is done client-side in `ItemListPage` based on the "Show inactive" toggle. This is acceptable because item lists are small (branch-scoped).

---

## File Summary

| File | Change |
|---|---|
| `BomPriceApproval.API/Features/Items/ItemDtos.cs` | Add `UpdateItemRequest`, `UpdateItemStatusRequest` |
| `BomPriceApproval.API/Features/Items/ItemsController.cs` | Add `PUT /{id}` and `PATCH /{id}/status` |
| `BomPriceApproval.Tests/Items/ItemEditTests.cs` | New — 6 integration tests |
| `bom-web/src/features/items/EditItemModal.tsx` | New — edit form modal |
| `bom-web/src/features/items/itemsApi.ts` | Add `useUpdateItem`, `useUpdateItemStatus` |
| `bom-web/src/features/items/ItemListPage.tsx` | Add actions column, Show inactive toggle |
| `bom-web/src/features/items/ItemListPage.test.tsx` | Add 2 tests for role-based buttons and toggle |
