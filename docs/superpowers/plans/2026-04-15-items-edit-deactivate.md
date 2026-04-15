# Items Edit & Deactivate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Admin and Accountant users edit item details and toggle item active status from the Items list page, with a "Show inactive" toggle to reveal deactivated items.

**Architecture:** Two new backend endpoints (`PUT /api/items/{id}` and `PATCH /api/items/{id}/status`) restricted to Admin + Accountant. The `GET /api/items` endpoint gains an optional `?includeInactive=true` param (default false keeps existing BOM-dropdown behaviour). The Items list page always fetches all items (including inactive) and filters client-side based on a "Show inactive" toggle. Deactivated items are hidden by default and shown when toggled.

**Tech Stack:** ASP.NET Core 8, EF Core 8 (PostgreSQL), React 19, TypeScript 5, TanStack Query v5, react-hook-form + Zod, Vitest + React Testing Library.

**Spec:** `docs/superpowers/specs/2026-04-15-items-edit-deactivate-design.md`

---

## File Structure

### New files
```
BomPriceApproval.Tests/Items/ItemEditTests.cs
bom-web/src/features/items/EditItemModal.tsx
```

### Modified files
```
BomPriceApproval.API/Features/Items/ItemDtos.cs          — add UpdateItemRequest, UpdateItemStatusRequest
BomPriceApproval.API/Features/Items/ItemsController.cs   — update GetAll, add PUT /{id}, add PATCH /{id}/status
bom-web/src/features/items/itemsApi.ts                   — update useItems (includeInactive), add useUpdateItem, useUpdateItemStatus
bom-web/src/features/items/ItemListPage.tsx              — actions column, show-inactive toggle, edit modal wiring
bom-web/src/features/items/ItemListPage.test.tsx         — add 2 tests
```

---

## Task 1: Write failing backend tests

**Files:**
- Create: `BomPriceApproval.Tests/Items/ItemEditTests.cs`

The seeded test users are (from `BomPriceApproval.API/Program.cs`):
- `admin@test.com` / `Admin@1234` — Admin, branchId = null
- `ali@test.com` / `Test@1234` — SalesPerson, branchId = 1 (Fujairah)
- `sara@test.com` / `Test@1234` — Accountant, branchId = 1 (Fujairah)
- `bob@test.com` / `Test@1234` — BomCreator, branchId = 1

Branches: Id=1 (Fujairah), Id=2 (Al Ain). To create a branch-2 item for the 403 test, register a branch-2 SalesPerson via `POST /api/users` (Admin-only endpoint), then create an item as that user.

- [ ] **Step 1: Create the test file**

```csharp
// BomPriceApproval.Tests/Items/ItemEditTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Items;

public class ItemEditTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    private async Task<int> CreateItemAsAliAsync(string code)
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var resp = await _client.PostAsJsonAsync("/api/items", new
        {
            Code = code,
            Description = $"Test Item {code}",
            Type = 1, // ItemType.RawMaterial
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<ItemDto>();
        return body!.Id;
    }

    [Fact]
    public async Task EditItem_AsAdmin_Succeeds()
    {
        var code = $"ADM-{Guid.NewGuid():N}"[..12];
        var id = await CreateItemAsAliAsync(code);

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var resp = await _client.PutAsJsonAsync($"/api/items/{id}", new
        {
            Code = code,
            Description = "Updated Description",
            Type = "RawMaterial",
            LastPurchasePrice = 5.25m
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var items = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items?includeInactive=true");
        items!.First(i => i.Id == id).Description.Should().Be("Updated Description");
        items!.First(i => i.Id == id).LastPurchasePrice.Should().Be(5.25m);
    }

    [Fact]
    public async Task EditItem_AsAccountant_OwnBranch_Succeeds()
    {
        var code = $"ACC-{Guid.NewGuid():N}"[..12];
        var id = await CreateItemAsAliAsync(code); // ali is branch 1, sara is also branch 1

        var saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saraToken);

        var resp = await _client.PutAsJsonAsync($"/api/items/{id}", new
        {
            Code = code,
            Description = "Sara Updated",
            Type = "RawMaterial",
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task EditItem_AsAccountant_CrossBranch_Returns403()
    {
        // Create a SalesPerson at branch 2, create item there
        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var spEmail = $"sp2-{Guid.NewGuid():N}"[..12] + "@test.com";
        await _client.PostAsJsonAsync("/api/users", new
        {
            Name = "Branch2 SP",
            Email = spEmail,
            Password = "Test@1234",
            Role = 0, // UserRole.SalesPerson
            BranchId = 2
        });

        var sp2Token = await LoginAsync(spEmail, "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp2Token);
        var createResp = await _client.PostAsJsonAsync("/api/items", new
        {
            Code = $"B2-{Guid.NewGuid():N}"[..12],
            Description = "Branch 2 Item",
            Type = 1,
            LastPurchasePrice = (decimal?)null
        });
        var b2Item = await createResp.Content.ReadFromJsonAsync<ItemDto>();

        // Sara is branch 1 Accountant — should get 403 on branch 2 item
        var saraToken = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saraToken);

        var resp = await _client.PutAsJsonAsync($"/api/items/{b2Item!.Id}", new
        {
            Code = b2Item.Code,
            Description = "Forbidden Update",
            Type = "RawMaterial",
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task EditItem_DuplicateCode_Returns409()
    {
        var codeA = $"DUP-A-{Guid.NewGuid():N}"[..14];
        var codeB = $"DUP-B-{Guid.NewGuid():N}"[..14];
        await CreateItemAsAliAsync(codeA);
        var idB = await CreateItemAsAliAsync(codeB);

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        // Try to rename item B to use item A's code
        var resp = await _client.PutAsJsonAsync($"/api/items/{idB}", new
        {
            Code = codeA,
            Description = "Duplicate Code Attempt",
            Type = "RawMaterial",
            LastPurchasePrice = (decimal?)null
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeactivateItem_ItemDisappearsFromDefaultList()
    {
        var code = $"DEACT-{Guid.NewGuid():N}"[..14];
        var id = await CreateItemAsAliAsync(code);

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var patchResp = await _client.PatchAsJsonAsync($"/api/items/{id}/status", new { IsActive = false });
        patchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Default GET (active only) — item gone
        var active = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items");
        active!.Should().NotContain(i => i.Id == id);

        // includeInactive=true — item present
        var all = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items?includeInactive=true");
        all!.Should().Contain(i => i.Id == id && !i.IsActive);
    }

    [Fact]
    public async Task ReactivateItem_ItemAppearsInDefaultList()
    {
        var code = $"REACT-{Guid.NewGuid():N}"[..14];
        var id = await CreateItemAsAliAsync(code);

        var adminToken = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        await _client.PatchAsJsonAsync($"/api/items/{id}/status", new { IsActive = false });
        await _client.PatchAsJsonAsync($"/api/items/{id}/status", new { IsActive = true });

        var active = await _client.GetFromJsonAsync<List<ItemDto>>("/api/items");
        active!.Should().Contain(i => i.Id == id && i.IsActive);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ItemDto(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test BomPriceApproval.Tests --filter "FullyQualifiedName~ItemEditTests" --no-build
```

Expected: All 6 tests fail with `404 Not Found` or `405 Method Not Allowed` (endpoints don't exist yet).

---

## Task 2: Implement backend endpoints

**Files:**
- Modify: `BomPriceApproval.API/Features/Items/ItemDtos.cs`
- Modify: `BomPriceApproval.API/Features/Items/ItemsController.cs`

- [ ] **Step 1: Add new DTOs to `ItemDtos.cs`**

Open `BomPriceApproval.API/Features/Items/ItemDtos.cs`. The current content is:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Items;

public record CreateItemRequest(string Code, string Description, ItemType Type, decimal? LastPurchasePrice);
public record ItemResponse(int Id, string Code, string Description, string Type, int BranchId, bool IsActive, decimal? LastPurchasePrice);
public record SimilarItemResult(int Id, string Code, string Description);

public record LedgerHeadersResponse(List<string> Headers);
public record LedgerImportRequest(string ItemCodeColumn, string DateColumn, string UnitPriceColumn, int BranchId);
public record LedgerImportResult(int Updated, int Skipped, List<string> UnmatchedCodes);
```

Add two lines after `CreateItemRequest`:
```csharp
public record UpdateItemRequest(string Code, string Description, ItemType Type, decimal? LastPurchasePrice);
public record UpdateItemStatusRequest(bool IsActive);
```

- [ ] **Step 2: Update `ItemsController.cs`**

Replace the entire file content:

```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items")]
[Authorize]
public class ItemsController(AppDbContext db) : ControllerBase
{
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? type = null, [FromQuery] bool includeInactive = false)
    {
        var query = db.Items.AsQueryable();
        if (CurrentBranchId.HasValue) query = query.Where(i => i.BranchId == CurrentBranchId);
        if (type is not null && Enum.TryParse<ItemType>(type, out var t))
            query = query.Where(i => i.Type == t);
        if (!includeInactive)
            query = query.Where(i => i.IsActive);
        return Ok(await query
            .Select(i => new ItemResponse(i.Id, i.Code, i.Description, i.Type.ToString(), i.BranchId, i.IsActive, i.LastPurchasePrice))
            .ToListAsync());
    }

    [HttpGet("check-similar")]
    public async Task<IActionResult> CheckSimilar([FromQuery] string description)
    {
        var branchId = CurrentBranchId;
        var similar = await db.Items
            .Where(i => (branchId == null || i.BranchId == branchId) &&
                        EF.Functions.ILike(i.Description, $"%{description}%"))
            .Select(i => new SimilarItemResult(i.Id, i.Code, i.Description))
            .Take(5).ToListAsync();
        return Ok(similar);
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Create(CreateItemRequest req)
    {
        if (CurrentBranchId is null)
            return BadRequest(new { message = "A branch-assigned user is required to create items." });

        var item = new Item
        {
            Code = req.Code, Description = req.Description, Type = req.Type,
            BranchId = CurrentBranchId.Value,
            LastPurchasePrice = req.LastPurchasePrice
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll),
            new ItemResponse(item.Id, item.Code, item.Description, item.Type.ToString(), item.BranchId, item.IsActive, item.LastPurchasePrice));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> Update(int id, UpdateItemRequest req)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return NotFound();
        if (CurrentBranchId.HasValue && item.BranchId != CurrentBranchId)
            return Forbid();

        var duplicate = await db.Items.AnyAsync(i => i.Code == req.Code && i.BranchId == item.BranchId && i.Id != id);
        if (duplicate) return Conflict(new { message = "An item with this code already exists in the branch." });

        item.Code = req.Code;
        item.Description = req.Description;
        item.Type = req.Type;
        item.LastPurchasePrice = req.LastPurchasePrice;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateItemStatusRequest req)
    {
        var item = await db.Items.FindAsync(id);
        if (item is null) return NotFound();
        if (CurrentBranchId.HasValue && item.BranchId != CurrentBranchId)
            return Forbid();

        item.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 3: Build**

```bash
dotnet build BomPriceApproval.API
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the new tests**

```bash
dotnet test BomPriceApproval.Tests --filter "FullyQualifiedName~ItemEditTests"
```

Expected: All 6 tests pass.

---

## Task 3: Run all backend tests and commit

**Files:** None (verification only)

- [ ] **Step 1: Run all backend tests**

```bash
dotnet test BomPriceApproval.Tests
```

Expected: All tests pass (22 existing + 6 new = 28 total).

- [ ] **Step 2: Commit**

```bash
git add BomPriceApproval.API/Features/Items/ItemDtos.cs
git add BomPriceApproval.API/Features/Items/ItemsController.cs
git add BomPriceApproval.Tests/Items/ItemEditTests.cs
git commit -m "feat(items): add edit and deactivate/reactivate endpoints"
```

---

## Task 4: Update itemsApi.ts

**Files:**
- Modify: `bom-web/src/features/items/itemsApi.ts`

The Items list page must always receive all items (including inactive) so it can filter client-side based on the "Show inactive" toggle. The BOM entry page uses a separate `useItems()` from `src/api/lookups.ts` which keeps calling `GET /api/items` (active-only default) — no changes needed there.

- [ ] **Step 1: Replace `bom-web/src/features/items/itemsApi.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  CreateItemRequest,
  ImportResult,
  Item,
  LedgerHeadersResponse,
  LedgerImportResult,
} from "@/types/api";

export const itemKeys = {
  all: ["items"] as const,
  list: () => [...itemKeys.all, "list"] as const,
};

export function useItems() {
  return useQuery({
    queryKey: itemKeys.list(),
    queryFn: () =>
      api.get<Item[]>("/items", { params: { includeInactive: true } }).then((r) => r.data),
  });
}

export function useCreateItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateItemRequest) =>
      api.post<Item>("/items", body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function useUpdateItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      data,
    }: {
      id: number;
      data: { code: string; description: string; type: string; lastPurchasePrice: number | null };
    }) => api.put(`/items/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function useUpdateItemStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, isActive }: { id: number; isActive: boolean }) =>
      api.patch(`/items/${id}/status`, { isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function useImportItems() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ file, branchId }: { file: File; branchId: number }) => {
      const fd = new FormData();
      fd.append("file", file);
      fd.append("branchId", String(branchId));
      return api
        .post<ImportResult>("/items/import", fd, {
          headers: { "Content-Type": "multipart/form-data" },
        })
        .then((r) => r.data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function downloadItemTemplate() {
  return api.get("/items/import/template", { responseType: "blob" }).then((r) => {
    const url = window.URL.createObjectURL(r.data as Blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "items-template.xlsx";
    a.click();
    window.URL.revokeObjectURL(url);
  });
}

export function useLedgerHeaders() {
  return useMutation({
    mutationFn: (file: File) => {
      const fd = new FormData();
      fd.append("file", file);
      return api
        .post<LedgerHeadersResponse>("/items/import/ledger/headers", fd, {
          headers: { "Content-Type": "multipart/form-data" },
        })
        .then((r) => r.data);
    },
  });
}

export function useLedgerImport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (args: {
      file: File;
      itemCodeColumn: string;
      dateColumn: string;
      unitPriceColumn: string;
      branchId: number;
    }) => {
      const fd = new FormData();
      fd.append("file", args.file);
      fd.append("itemCodeColumn", args.itemCodeColumn);
      fd.append("dateColumn", args.dateColumn);
      fd.append("unitPriceColumn", args.unitPriceColumn);
      fd.append("branchId", String(args.branchId));
      return api
        .post<LedgerImportResult>("/items/import/ledger", fd, {
          headers: { "Content-Type": "multipart/form-data" },
        })
        .then((r) => r.data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd bom-web && npx tsc --noEmit
```

Expected: No errors.

---

## Task 5: Create EditItemModal.tsx

**Files:**
- Create: `bom-web/src/features/items/EditItemModal.tsx`

This mirrors `AddItemModal.tsx` but pre-populates form fields from the passed `item` prop and calls `useUpdateItem()` instead of `useCreateItem()`.

- [ ] **Step 1: Create the file**

```typescript
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Dialog } from "@/components/ui/Dialog";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import { useUpdateItem } from "./itemsApi";
import type { Item } from "@/types/api";

const schema = z.object({
  code: z.string().min(1, "Code is required"),
  description: z.string().min(1, "Description is required"),
  type: z.enum(["FinishedGood", "RawMaterial"]),
  lastPurchasePrice: z.preprocess(
    (v) => (v === "" || v === null || v === undefined ? null : Number(v)),
    z.number().positive("Must be positive").nullable(),
  ),
});

type FormValues = z.infer<typeof schema>;

interface Props {
  open: boolean;
  item: Item | null;
  onClose: () => void;
}

export function EditItemModal({ open, item, onClose }: Props) {
  const update = useUpdateItem();
  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { code: "", description: "", type: "FinishedGood", lastPurchasePrice: null },
  });

  useEffect(() => {
    if (item) {
      reset({
        code: item.code,
        description: item.description,
        type: item.type as "FinishedGood" | "RawMaterial",
        lastPurchasePrice: item.lastPurchasePrice ?? null,
      });
    }
  }, [item, reset]);

  const onSubmit = handleSubmit(async (values) => {
    if (!item) return;
    await update.mutateAsync({ id: item.id, data: values });
    onClose();
  });

  function handleClose() {
    reset();
    onClose();
  }

  return (
    <Dialog open={open} onClose={handleClose} title="Edit Item">
      <form onSubmit={onSubmit} className="space-y-4" noValidate>
        <div className="space-y-1">
          <Label htmlFor="edit-item-code">Code</Label>
          <Input id="edit-item-code" {...register("code")} />
          {errors.code && <p className="text-xs text-destructive">{errors.code.message}</p>}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-item-desc">Description</Label>
          <Input id="edit-item-desc" {...register("description")} />
          {errors.description && (
            <p className="text-xs text-destructive">{errors.description.message}</p>
          )}
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-item-type">Type</Label>
          <select
            id="edit-item-type"
            className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
            {...register("type")}
          >
            <option value="FinishedGood">Finished Good</option>
            <option value="RawMaterial">Raw Material</option>
          </select>
        </div>

        <div className="space-y-1">
          <Label htmlFor="edit-item-price">Last Purchase Price</Label>
          <Input
            id="edit-item-price"
            type="number"
            step="0.0001"
            placeholder="Optional"
            {...register("lastPurchasePrice")}
          />
          {errors.lastPurchasePrice && (
            <p className="text-xs text-destructive">{errors.lastPurchasePrice.message}</p>
          )}
        </div>

        {update.isError && (
          <p className="text-sm text-destructive">
            {(update.error as { response?: { data?: { message?: string } } })?.response?.data
              ?.message ?? "Failed to update item"}
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="ghost" onClick={handleClose}>
            Cancel
          </Button>
          <Button type="submit" disabled={isSubmitting || update.isPending}>
            {update.isPending ? "Saving…" : "Save"}
          </Button>
        </div>
      </form>
    </Dialog>
  );
}
```

---

## Task 6: Update ItemListPage.tsx

**Files:**
- Modify: `bom-web/src/features/items/ItemListPage.tsx`

Changes:
1. Move column definitions inside the component (so they can reference role and mutation callbacks)
2. Add actions column (Edit + Deactivate/Reactivate buttons) visible to Admin + Accountant
3. Add `showInactive` state (default `false`) and a toggle checkbox above the table
4. Filter `data` client-side based on `showInactive` before passing to DataTable
5. Add `editItem` state to track which item the edit modal is for

- [ ] **Step 1: Replace `bom-web/src/features/items/ItemListPage.tsx`**

```typescript
import { useState, useMemo } from "react";
import type { ColumnDef } from "@tanstack/react-table";
import { Pencil, Ban, CheckCircle } from "lucide-react";
import { DataTable } from "@/components/ui/DataTable";
import { Button } from "@/components/ui/Button";
import { Card, CardContent } from "@/components/ui/Card";
import { useAuthStore } from "@/store/authStore";
import { useBranches } from "@/api/lookups";
import { useItems, useUpdateItemStatus } from "./itemsApi";
import { AddItemModal } from "./AddItemModal";
import { EditItemModal } from "./EditItemModal";
import { ImportItemsModal } from "./ImportItemsModal";
import { ImportLedgerModal } from "./ImportLedgerModal";
import type { Item } from "@/types/api";

export default function ItemListPage() {
  const role = useAuthStore((s) => s.user?.role);
  const { data, isLoading, isError, refetch } = useItems();
  const { data: branches = [] } = useBranches();
  const updateStatus = useUpdateItemStatus();

  const [addOpen, setAddOpen] = useState(false);
  const [editItem, setEditItem] = useState<Item | null>(null);
  const [importOpen, setImportOpen] = useState(false);
  const [ledgerOpen, setLedgerOpen] = useState(false);
  const [showInactive, setShowInactive] = useState(false);

  const canAdd = role === "SalesPerson" || role === "Admin";
  const canImport = role === "Admin";
  const canManage = role === "Admin" || role === "Accountant";

  const filteredItems = useMemo(
    () => (showInactive ? (data ?? []) : (data ?? []).filter((i) => i.isActive)),
    [data, showInactive],
  );

  const columns = useMemo<ColumnDef<Item>[]>(
    () => [
      {
        accessorKey: "code",
        header: "Code",
        cell: (i) => (
          <span className={`font-mono text-xs${!i.row.original.isActive ? " text-muted-foreground" : ""}`}>
            {i.getValue() as string}
          </span>
        ),
      },
      {
        accessorKey: "description",
        header: "Description",
        cell: (i) => (
          <span className={!i.row.original.isActive ? "text-muted-foreground" : ""}>
            {i.getValue() as string}
          </span>
        ),
      },
      { accessorKey: "type", header: "Type" },
      {
        accessorKey: "lastPurchasePrice",
        header: "Last Purchase Price",
        cell: (i) => {
          const v = i.getValue() as number | null;
          return v == null ? "—" : v.toFixed(4);
        },
      },
      {
        accessorKey: "isActive",
        header: "Status",
        cell: (i) =>
          (i.getValue() as boolean) ? (
            <span className="inline-flex items-center rounded-full bg-green-50 px-2 py-0.5 text-xs font-medium text-green-700">
              Active
            </span>
          ) : (
            <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
              Inactive
            </span>
          ),
      },
      ...(canManage
        ? [
            {
              id: "actions",
              header: "",
              cell: ({ row }: { row: { original: Item } }) => {
                const item = row.original;
                return (
                  <div className="flex justify-end gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={`Edit ${item.description}`}
                      onClick={() => setEditItem(item)}
                    >
                      <Pencil className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={item.isActive ? `Deactivate ${item.description}` : `Reactivate ${item.description}`}
                      onClick={() => {
                        if (
                          item.isActive &&
                          !window.confirm(
                            `Deactivate "${item.description}"? It will no longer appear in BOM dropdowns.`,
                          )
                        )
                          return;
                        updateStatus.mutate({ id: item.id, isActive: !item.isActive });
                      }}
                    >
                      {item.isActive ? (
                        <Ban className="h-4 w-4 text-destructive" />
                      ) : (
                        <CheckCircle className="h-4 w-4 text-green-600" />
                      )}
                    </Button>
                  </div>
                );
              },
            } as ColumnDef<Item>,
          ]
        : []),
    ],
    [canManage, updateStatus],
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold tracking-tight">Items</h1>
        <div className="flex gap-2">
          {canImport && (
            <Button variant="ghost" onClick={() => setLedgerOpen(true)}>
              Import from Ledger
            </Button>
          )}
          {canImport && (
            <Button variant="ghost" onClick={() => setImportOpen(true)}>
              Import
            </Button>
          )}
          {canAdd && <Button onClick={() => setAddOpen(true)}>Add Item</Button>}
        </div>
      </div>

      <div className="flex items-center gap-2">
        <input
          id="show-inactive"
          type="checkbox"
          checked={showInactive}
          onChange={(e) => setShowInactive(e.target.checked)}
          className="h-4 w-4 rounded border-input"
        />
        <label htmlFor="show-inactive" className="text-sm text-muted-foreground">
          Show inactive
        </label>
      </div>

      {isError && (
        <Card>
          <CardContent className="flex items-center justify-between">
            <p className="text-destructive">Failed to load items.</p>
            <Button variant="ghost" onClick={() => refetch()}>
              Retry
            </Button>
          </CardContent>
        </Card>
      )}

      <DataTable
        columns={columns}
        data={filteredItems}
        isLoading={isLoading}
        emptyState={<p>No items yet.</p>}
      />

      <AddItemModal open={addOpen} onClose={() => setAddOpen(false)} />
      <EditItemModal
        open={editItem !== null}
        item={editItem}
        onClose={() => setEditItem(null)}
      />
      {canImport && (
        <ImportItemsModal
          open={importOpen}
          onClose={() => setImportOpen(false)}
          branches={branches}
        />
      )}
      {canImport && (
        <ImportLedgerModal
          open={ledgerOpen}
          onClose={() => setLedgerOpen(false)}
          branches={branches}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
cd bom-web && npx tsc --noEmit
```

Expected: No errors.

---

## Task 7: Add frontend tests, run all, commit

**Files:**
- Modify: `bom-web/src/features/items/ItemListPage.test.tsx`

Add two new tests after the existing three. The existing tests only mock `api.get` once (for empty items). The new tests need to set a role and mock items that include an inactive item.

- [ ] **Step 1: Add 2 new tests to `bom-web/src/features/items/ItemListPage.test.tsx`**

Open the file and add the following two `it` blocks inside the `describe("ItemListPage", () => {` block, after the existing three tests:

```typescript
  it("shows Edit and Deactivate buttons for Admin on each row", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "Admin", userId: 1, name: "Admin", branchId: null,
    });
    vi.mocked(api.get).mockResolvedValue({
      data: [
        { id: 1, code: "FG-001", description: "Pipe 20mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
      ],
    });

    render(wrap(<ItemListPage />));

    expect(await screen.findByRole("button", { name: /edit pipe 20mm/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /deactivate pipe 20mm/i })).toBeInTheDocument();
  });

  it("shows no Edit/Deactivate buttons for BomCreator", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "BomCreator", userId: 3, name: "Bob", branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValue({
      data: [
        { id: 1, code: "FG-001", description: "Pipe 20mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
      ],
    });

    render(wrap(<ItemListPage />));

    await waitFor(() =>
      expect(screen.queryAllByTestId("data-table-skeleton-row").length).toBe(0),
    );
    expect(screen.queryByRole("button", { name: /edit pipe 20mm/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /deactivate pipe 20mm/i })).not.toBeInTheDocument();
  });

  it("hides inactive items by default and shows them when 'Show inactive' is toggled", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "Admin", userId: 1, name: "Admin", branchId: null,
    });
    vi.mocked(api.get).mockResolvedValue({
      data: [
        { id: 1, code: "FG-001", description: "Pipe 20mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
        { id: 2, code: "RM-002", description: "PE100 Resin", type: "RawMaterial", branchId: 1, isActive: false, lastPurchasePrice: 4.25 },
      ],
    });

    const user = userEvent.setup();
    render(wrap(<ItemListPage />));

    // Active item visible, inactive item hidden by default
    expect(await screen.findByText("Pipe 20mm")).toBeInTheDocument();
    expect(screen.queryByText("PE100 Resin")).not.toBeInTheDocument();

    // Toggle "Show inactive"
    await user.click(screen.getByLabelText(/show inactive/i));

    // Now inactive item appears
    expect(screen.getByText("PE100 Resin")).toBeInTheDocument();
  });
```

Note: `userEvent` is already imported at the top of the test file (`import userEvent from "@testing-library/user-event"`). `waitFor` is already imported from `@testing-library/react`.

- [ ] **Step 2: Run all frontend tests**

```bash
cd bom-web && npm run test
```

Expected: All tests pass (existing + 3 new = at least 89 tests).

- [ ] **Step 3: Commit**

```bash
git add BomPriceApproval.API/Features/Items/ItemDtos.cs
git add BomPriceApproval.API/Features/Items/ItemsController.cs
git add BomPriceApproval.Tests/Items/ItemEditTests.cs
git add bom-web/src/features/items/itemsApi.ts
git add bom-web/src/features/items/EditItemModal.tsx
git add bom-web/src/features/items/ItemListPage.tsx
git add bom-web/src/features/items/ItemListPage.test.tsx
git commit -m "feat(items): edit, deactivate, and reactivate items"
```
