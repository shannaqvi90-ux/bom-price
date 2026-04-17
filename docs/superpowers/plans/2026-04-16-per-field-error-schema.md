# Per-Field Error Schema Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate all manual `BadRequest` guards in the 4 workflow controllers from `{ message: string }` to RFC 7807 `ValidationProblemDetails`, and make the frontend surface per-field errors inline on form inputs.

**Architecture:** New `Validation` fluent builder (`BomPriceApproval.API/Infrastructure/Validation/Validation.cs`) wraps `ModelStateDictionary` construction into `Validation.Detail(...).Field(...).Return()`. All 19 guards migrate to it. Frontend `apiError.ts` gains an `extractFieldErrors` helper that normalizes PascalCase-bracket keys to lowercase-dot. `NewRequisitionPage` feeds them into react-hook-form's `setError`; the other 3 pages use a `fieldErrors` state map to render red borders + inline messages.

**Tech Stack:** ASP.NET Core 8, EF Core 8, xUnit + Testcontainers, React 19, TanStack Query, react-hook-form + zod, Vitest + RTL.

**Spec:** `docs/superpowers/specs/2026-04-16-per-field-error-schema-design.md`

---

## Execution order & breaking-state awareness

**Commit order:** T1 → T2 → T3 → T4 → T5 → T6 → T7 → T8 (sequential).

**Transient breaking state during T2–T5:** between these commits, migrated controllers emit ProblemDetails while unmigrated ones still emit `{message}`. The frontend `extractApiError` still reads `.message` (unchanged until T6), so 400 toasts from migrated controllers temporarily show `"Something went wrong"` (fallback) in real use. Automated tests mock responses directly and aren't affected. **Do not deploy between T2 and T6.**

After T6 the frontend expects `.detail` exclusively and all backend guards emit the new shape. System returns to coherent state.

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `BomPriceApproval.API/Infrastructure/Validation/Validation.cs` | Fluent builder for ValidationProblemDetails |
| Modify | `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` | 7 guards → Validation.* |
| Modify | `BomPriceApproval.API/Features/Bom/BomController.cs` | 4 guards → Validation.* |
| Modify | `BomPriceApproval.API/Features/Costing/CostingController.cs` | 3 guards → Validation.* |
| Modify | `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs` | 6 guards → Validation.* |
| Modify | `BomPriceApproval.Tests/Shared/TestDtos.cs` | Add `ValidationProblemResponse` |
| Modify | `BomPriceApproval.Tests/Requisitions/ValidationTests.cs` | Rewrite 8 bodies + add 1 per-field assertion |
| Modify | `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs` | Rewrite 2 bodies |
| Modify | `BomPriceApproval.Tests/Costing/CostingTests.cs` | Rewrite 3 bodies |
| Modify | `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs` | Rewrite 5 bodies + add 1 per-field assertion |
| Modify | `bom-web/src/lib/apiError.ts` | Read `detail`; add `extractFieldErrors` |
| Modify | `bom-web/src/lib/apiError.test.ts` | Update 4 existing tests + add 4 for `extractFieldErrors` |
| Modify | `bom-web/src/features/requisitions/NewRequisitionPage.tsx` | `setError` per field in catch |
| Modify | `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx` | +1 test |
| Modify | `bom-web/src/features/bom/BomEntryPage.tsx` | `fieldErrors` state + red-border inputs |
| Modify | `bom-web/src/features/costing/CostingEntryPage.tsx` | Same |
| Modify | `bom-web/src/features/approvals/MdReviewPage.tsx` | Same |

---

## Task 1: Create `Validation` fluent builder

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Validation/Validation.cs`

- [ ] **Step 1: Create the file**

Create `BomPriceApproval.API/Infrastructure/Validation/Validation.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BomPriceApproval.API.Infrastructure.Validation;

public static class Validation
{
    /// <summary>
    /// Start building a 400 ValidationProblemDetails with the given human-readable summary.
    /// </summary>
    public static ValidationProblemBuilder Detail(string detail) => new(detail);
}

public sealed class ValidationProblemBuilder
{
    private readonly string _detail;
    private readonly ModelStateDictionary _errors = new();

    internal ValidationProblemBuilder(string detail)
    {
        _detail = detail;
    }

    /// <summary>
    /// Add a field-level error. Field keys use bracket notation for arrays
    /// (e.g. "Items[0].ExpectedQty"). Call once per offending field.
    /// </summary>
    public ValidationProblemBuilder Field(string field, string message)
    {
        _errors.AddModelError(field, message);
        return this;
    }

    /// <summary>
    /// Build the 400 ActionResult with Content-Type application/problem+json.
    /// </summary>
    public ActionResult Return()
    {
        var problem = new ValidationProblemDetails(_errors)
        {
            Detail = _detail,
            Status = StatusCodes.Status400BadRequest,
        };
        return new ObjectResult(problem)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" },
        };
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build
```

Expected: succeeds, 0 errors.

- [ ] **Step 3: Add `ValidationProblemResponse` record to shared test DTOs**

In `BomPriceApproval.Tests/Shared/TestDtos.cs`, append:

```csharp
public record ValidationProblemResponse(string Detail, Dictionary<string, string[]> Errors);
```

Keep the existing `ErrorResponse` record as-is (used by auth/404/500 paths that survive).

- [ ] **Step 4: Smoke-test the builder via a one-off integration test**

Append to `BomPriceApproval.Tests/Requisitions/ValidationTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task ValidationProblemDetails_ShapeAndContentType_AreCorrect()
    {
        // This test indirectly verifies the Validation fluent builder by triggering
        // the existing zero-qty check (will be migrated to the builder in Task 2).
        // Before Task 2: the current response is { message: "..." } → this test FAILS.
        // After Task 2: the response is a full ProblemDetails → this test PASSES.

        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 0m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.Should().Contain("ExpectedQty");
        body.Errors.Should().ContainKey("Items[0].ExpectedQty");
    }
```

- [ ] **Step 5: Run — expect FAIL (still using old shape)**

```bash
dotnet test --filter "FullyQualifiedName~ValidationTests.ValidationProblemDetails_ShapeAndContentType_AreCorrect"
```

Expected: test fails because the controller still returns `{message}`. This confirms the test is active and will start passing after Task 2's migration.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Validation/Validation.cs \
        BomPriceApproval.Tests/Shared/TestDtos.cs \
        BomPriceApproval.Tests/Requisitions/ValidationTests.cs
git commit -m "feat(api): add Validation fluent builder for RFC 7807 ProblemDetails"
```

---

## Task 2: Migrate `RequisitionsController` guards

**Files:**
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`
- Modify: `BomPriceApproval.Tests/Requisitions/ValidationTests.cs`

- [ ] **Step 1: Add `using` and migrate `Create` guards**

In `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`, add the using at the top:

```csharp
using BomPriceApproval.API.Infrastructure.Validation;
```

Find the block of guards in `Create` (inserted in the validation-hardening work):

```csharp
        if (req.Items.Count == 0)
            return BadRequest(new { message = "At least one item is required." });

        if (req.Items.Any(i => i.ExpectedQty <= 0))
            return BadRequest(new { message = "ExpectedQty must be greater than 0." });

        var distinctItemIds = req.Items.Select(i => i.ItemId).Distinct().ToList();
        if (distinctItemIds.Count != req.Items.Count)
            return BadRequest(new { message = "Duplicate items in requisition are not allowed." });

        var activeItemIds = await db.Items
            .Where(i => distinctItemIds.Contains(i.Id) && i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();
        var missingItems = distinctItemIds.Except(activeItemIds).ToList();
        if (missingItems.Count > 0)
            return BadRequest(new { message = $"Unknown or inactive items: {string.Join(", ", missingItems)}" });
```

Replace with:

```csharp
        if (req.Items.Count == 0)
            return Validation
                .Detail("At least one item is required.")
                .Field("Items", "At least one item is required.")
                .Return();

        if (req.Items.Any(i => i.ExpectedQty <= 0))
        {
            var builder = Validation.Detail("ExpectedQty must be greater than 0.");
            for (int i = 0; i < req.Items.Count; i++)
                if (req.Items[i].ExpectedQty <= 0)
                    builder.Field($"Items[{i}].ExpectedQty", "Must be greater than 0.");
            return builder.Return();
        }

        var distinctItemIds = req.Items.Select(i => i.ItemId).Distinct().ToList();
        if (distinctItemIds.Count != req.Items.Count)
            return Validation
                .Detail("Duplicate items in requisition are not allowed.")
                .Field("Items", "Duplicate items are not allowed.")
                .Return();

        var activeItemIds = await db.Items
            .Where(i => distinctItemIds.Contains(i.Id) && i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();
        var missingItems = distinctItemIds.Except(activeItemIds).ToList();
        if (missingItems.Count > 0)
        {
            var builder = Validation.Detail($"Unknown or inactive items: {string.Join(", ", missingItems)}");
            for (int i = 0; i < req.Items.Count; i++)
                if (missingItems.Contains(req.Items[i].ItemId))
                    builder.Field($"Items[{i}].ItemId", "Unknown or inactive.");
            return builder.Return();
        }
```

- [ ] **Step 2: Migrate `AddItem` guards**

Find the `AddItem` guards (inserted in the validation-hardening work):

```csharp
        if (q.Status != RequisitionStatus.BomPending)
            return BadRequest(new { message = "Items can only be added when status is BomPending" });

        if (req.ExpectedQty <= 0)
            return BadRequest(new { message = "ExpectedQty must be greater than 0." });

        if (q.Items.Any(i => i.ItemId == req.ItemId))
            return BadRequest(new { message = "Item already added to this requisition." });

        var itemIsValid = await db.Items.AnyAsync(i => i.Id == req.ItemId && i.IsActive);
        if (!itemIsValid)
            return BadRequest(new { message = $"Unknown or inactive item: {req.ItemId}" });
```

Replace with:

```csharp
        if (q.Status != RequisitionStatus.BomPending)
            return Validation
                .Detail("Items can only be added when status is BomPending")
                .Field("Status", "Items can only be added when status is BomPending.")
                .Return();

        if (req.ExpectedQty <= 0)
            return Validation
                .Detail("ExpectedQty must be greater than 0.")
                .Field("ExpectedQty", "Must be greater than 0.")
                .Return();

        if (q.Items.Any(i => i.ItemId == req.ItemId))
            return Validation
                .Detail("Item already added to this requisition.")
                .Field("ItemId", "Item already added.")
                .Return();

        var itemIsValid = await db.Items.AnyAsync(i => i.Id == req.ItemId && i.IsActive);
        if (!itemIsValid)
            return Validation
                .Detail($"Unknown or inactive item: {req.ItemId}")
                .Field("ItemId", "Unknown or inactive.")
                .Return();
```

Also migrate the pre-existing `RemoveItem` guard (it also returns `BadRequest(new { message = ... })`):

```csharp
        if (q.Status != RequisitionStatus.BomPending)
            return BadRequest(new { message = "Items can only be removed when status is BomPending" });

        if (q.Items.Count <= 1)
            return BadRequest(new { message = "Cannot remove the last item" });
```

Replace with:

```csharp
        if (q.Status != RequisitionStatus.BomPending)
            return Validation
                .Detail("Items can only be removed when status is BomPending")
                .Field("Status", "Items can only be removed when status is BomPending.")
                .Return();

        if (q.Items.Count <= 1)
            return Validation
                .Detail("Cannot remove the last item")
                .Field("Items", "Cannot remove the last item.")
                .Return();
```

Also migrate any remaining `BadRequest(new { message = ... })` in this controller. Use Grep to find them all:

```bash
cd "D:\shan projects\BOM_Price_Approval"
```

Use the Grep tool with pattern `BadRequest\(new \{ message` scoped to `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs` to confirm no `{message}` shape remains.

For example the existing "No active exchange rate" guard:
```csharp
if (rate is null) return BadRequest(new { message = $"No active exchange rate for {req.CurrencyCode}" });
```
Replace with:
```csharp
if (rate is null)
    return Validation
        .Detail($"No active exchange rate for {req.CurrencyCode}")
        .Field("CurrencyCode", "No active exchange rate.")
        .Return();
```

And the "branch-assigned sales person is required" guard:
```csharp
if (CurrentBranchId is null)
    return BadRequest(new { message = "A branch-assigned sales person is required to create requisitions." });
```
Replace with:
```csharp
if (CurrentBranchId is null)
    return Validation
        .Detail("A branch-assigned sales person is required to create requisitions.")
        .Field("BranchId", "A branch-assigned sales person is required.")
        .Return();
```

- [ ] **Step 3: Rewrite the 8 existing ValidationTests bodies**

In `BomPriceApproval.Tests/Requisitions/ValidationTests.cs`, for each of these tests:
- `Create_DuplicateItemIds_Returns400`
- `Create_ZeroQty_Returns400`
- `Create_NegativeQty_Returns400`
- `Create_NonExistentItem_Returns400`
- `Create_InactiveItem_Returns400`
- `AddItem_DuplicateItem_Returns400`
- `AddItem_ZeroQty_Returns400`
- `AddItem_ParallelDuplicate_RejectsOneRequest`

Do a mechanical 2-line swap:
- `ReadFromJsonAsync<ErrorResponse>()` → `ReadFromJsonAsync<ValidationProblemResponse>()`
- `.Message.Should().Contain(...)` → `.Detail.Should().Contain(...)`

Example — `Create_DuplicateItemIds_Returns400`:

Before:
```csharp
        var body = await resp.Content.ReadFromJsonAsync<ErrorResponse>();
        body!.Message.Should().Contain("Duplicate");
```

After:
```csharp
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Detail.Should().Contain("Duplicate");
```

For `AddItem_ParallelDuplicate_RejectsOneRequest`: only the success-count assertion matters; the body shape is moot (check whether it reads any body — if not, leave it). Grep the test file to confirm.

- [ ] **Step 4: Add the per-field-errors assertion test**

Append to the `ValidationTests` class:

```csharp
    [Fact]
    public async Task Create_ZeroQty_EmitsPerFieldError()
    {
        var sp = await LoginAsync("ali@test.com", "Test@1234");
        var itemId = await CreateActiveFinishedGoodAsync(sp);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sp);
        var customerId = await GetCustomerIdAsync();

        var resp = await _client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = customerId,
            Items = new[] { new { ItemId = itemId, ExpectedQty = 0m } },
            CurrencyCode = "AED"
        });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Errors.Should().ContainKey("Items[0].ExpectedQty");
        body.Errors["Items[0].ExpectedQty"][0].Should().Contain("greater than 0");
    }
```

- [ ] **Step 5: Run the filtered suite — expect all pass**

```bash
dotnet test --filter "FullyQualifiedName~ValidationTests"
```

Expected: all ValidationTests pass (the previously-failing `ValidationProblemDetails_ShapeAndContentType_AreCorrect` from Task 1 now passes; the 8 migrated tests also pass; the new per-field test passes).

- [ ] **Step 6: Run the full backend suite**

```bash
dotnet test
```

Expected: all backend tests pass. Any failures here indicate an unmigrated `{message}` shape that tests on other feature folders assert — investigate before proceeding.

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs \
        BomPriceApproval.Tests/Requisitions/ValidationTests.cs
git commit -m "feat(api): migrate RequisitionsController guards to ValidationProblemDetails"
```

---

## Task 3: Migrate `BomController.SaveLines` guards

**Files:**
- Modify: `BomPriceApproval.API/Features/Bom/BomController.cs`
- Modify: `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs`

- [ ] **Step 1: Add `using` and migrate `SaveLines` guards**

In `BomPriceApproval.API/Features/Bom/BomController.cs`, add at the top:

```csharp
using BomPriceApproval.API.Infrastructure.Validation;
```

Find the 4 inline guards in `SaveLines` (inserted in the validation-hardening work):

```csharp
        if (request.Lines.Any(l => l.QtyPerKg <= 0))
            return BadRequest(new { message = "QtyPerKg must be greater than 0." });

        if (request.Lines.Any(l => l.WastagePct < 0))
            return BadRequest(new { message = "WastagePct cannot be negative." });

        var processIds = request.Lines.Select(l => l.ProcessId).Distinct().ToList();
        var validProcessCount = await db.Processes.CountAsync(p => processIds.Contains(p.Id));
        if (validProcessCount != processIds.Count)
            return BadRequest(new { message = "One or more ProcessIds are invalid." });

        var rawMatIds = request.Lines.Select(l => l.RawMaterialItemId).Distinct().ToList();
        var validRawMatCount = await db.Items.CountAsync(i => rawMatIds.Contains(i.Id) && i.IsActive);
        if (validRawMatCount != rawMatIds.Count)
            return BadRequest(new { message = "One or more RawMaterialItemIds are invalid or inactive." });
```

Replace with:

```csharp
        if (request.Lines.Any(l => l.QtyPerKg <= 0))
        {
            var builder = Validation.Detail("QtyPerKg must be greater than 0.");
            for (int i = 0; i < request.Lines.Count; i++)
                if (request.Lines[i].QtyPerKg <= 0)
                    builder.Field($"Lines[{i}].QtyPerKg", "Must be greater than 0.");
            return builder.Return();
        }

        if (request.Lines.Any(l => l.WastagePct < 0))
        {
            var builder = Validation.Detail("WastagePct cannot be negative.");
            for (int i = 0; i < request.Lines.Count; i++)
                if (request.Lines[i].WastagePct < 0)
                    builder.Field($"Lines[{i}].WastagePct", "Cannot be negative.");
            return builder.Return();
        }

        var processIds = request.Lines.Select(l => l.ProcessId).Distinct().ToList();
        var validProcessIds = await db.Processes
            .Where(p => processIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();
        var invalidProcessIds = processIds.Except(validProcessIds).ToHashSet();
        if (invalidProcessIds.Count > 0)
        {
            var builder = Validation.Detail("One or more ProcessIds are invalid.");
            for (int i = 0; i < request.Lines.Count; i++)
                if (invalidProcessIds.Contains(request.Lines[i].ProcessId))
                    builder.Field($"Lines[{i}].ProcessId", "Invalid ProcessId.");
            return builder.Return();
        }

        var rawMatIds = request.Lines.Select(l => l.RawMaterialItemId).Distinct().ToList();
        var validRawMatIds = await db.Items
            .Where(i => rawMatIds.Contains(i.Id) && i.IsActive)
            .Select(i => i.Id)
            .ToListAsync();
        var invalidRawMatIds = rawMatIds.Except(validRawMatIds).ToHashSet();
        if (invalidRawMatIds.Count > 0)
        {
            var builder = Validation.Detail("One or more RawMaterialItemIds are invalid or inactive.");
            for (int i = 0; i < request.Lines.Count; i++)
                if (invalidRawMatIds.Contains(request.Lines[i].RawMaterialItemId))
                    builder.Field($"Lines[{i}].RawMaterialItemId", "Invalid or inactive.");
            return builder.Return();
        }
```

(Note the small refactor from `CountAsync` to `ToListAsync() + Except`: we need the actual invalid IDs to emit per-row field errors, not just a count.)

- [ ] **Step 2: Migrate any other `{message}` guards in this controller**

Grep for `BadRequest(new { message` in the controller and migrate each to `Validation.Detail(...).Field(...).Return()`. Apply the same field-key conventions (e.g., for a status guard, `Status` is the field key).

- [ ] **Step 3: Rewrite the 2 existing BomSaveLinesTests bodies**

In `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs`:
- `SaveLines_ZeroQty_Returns400`
- `SaveLines_NegativeWastage_Returns400`

Each currently does:
```csharp
var body = await saveResp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Shared.ErrorResponse>();
body!.Message.Should().Contain("QtyPerKg");
```

Replace with:
```csharp
var body = await saveResp.Content.ReadFromJsonAsync<BomPriceApproval.Tests.Shared.ValidationProblemResponse>();
body!.Detail.Should().Contain("QtyPerKg");
```

(Adjust the `.Contain` assertion text per test — `QtyPerKg` for the first, `wastage` for the second lowercase check.)

- [ ] **Step 4: Run the filtered suite**

```bash
dotnet test --filter "FullyQualifiedName~BomSaveLinesTests"
```

Expected: all 4 tests pass.

- [ ] **Step 5: Run the full backend suite**

```bash
dotnet test
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Bom/BomController.cs \
        BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs
git commit -m "feat(api): migrate BomController.SaveLines guards to ValidationProblemDetails"
```

---

## Task 4: Migrate `CostingController.Submit` guards

**Files:**
- Modify: `BomPriceApproval.API/Features/Costing/CostingController.cs`
- Modify: `BomPriceApproval.Tests/Costing/CostingTests.cs`

- [ ] **Step 1: Add `using` and migrate `Submit` guards**

In `BomPriceApproval.API/Features/Costing/CostingController.cs`, add:

```csharp
using BomPriceApproval.API.Infrastructure.Validation;
```

Find the 3 inline guards in `Submit` (inserted in the validation-hardening work):

```csharp
        if (request.RawMaterialCosts.Any(rc => rc.CostPerKg < 0))
            return BadRequest(new { message = "CostPerKg cannot be negative." });

        var submittedBomLineIds = request.RawMaterialCosts.Select(rc => rc.BomLineId).Distinct().ToList();
        var bomLineIds = bom.Lines.Select(l => l.Id).ToList();
        var unknownBomLines = submittedBomLineIds.Except(bomLineIds).ToList();
        if (unknownBomLines.Count > 0)
            return BadRequest(new { message = $"Unknown BOM line(s): {string.Join(", ", unknownBomLines)}" });

        var missingBomLines = bomLineIds.Except(submittedBomLineIds).ToList();
        if (missingBomLines.Count > 0)
            return BadRequest(new { message = $"Missing cost for BOM line(s): {string.Join(", ", missingBomLines)}" });
```

Replace with:

```csharp
        if (request.RawMaterialCosts.Any(rc => rc.CostPerKg < 0))
        {
            var builder = Validation.Detail("CostPerKg cannot be negative.");
            for (int i = 0; i < request.RawMaterialCosts.Count; i++)
                if (request.RawMaterialCosts[i].CostPerKg < 0)
                    builder.Field($"RawMaterialCosts[{i}].CostPerKg", "Cannot be negative.");
            return builder.Return();
        }

        var submittedBomLineIds = request.RawMaterialCosts.Select(rc => rc.BomLineId).Distinct().ToList();
        var bomLineIds = bom.Lines.Select(l => l.Id).ToList();
        var unknownBomLines = submittedBomLineIds.Except(bomLineIds).ToHashSet();
        if (unknownBomLines.Count > 0)
        {
            var builder = Validation.Detail($"Unknown BOM line(s): {string.Join(", ", unknownBomLines)}");
            for (int i = 0; i < request.RawMaterialCosts.Count; i++)
                if (unknownBomLines.Contains(request.RawMaterialCosts[i].BomLineId))
                    builder.Field($"RawMaterialCosts[{i}].BomLineId", "Unknown BOM line.");
            return builder.Return();
        }

        var missingBomLines = bomLineIds.Except(submittedBomLineIds).ToList();
        if (missingBomLines.Count > 0)
            return Validation
                .Detail($"Missing cost for BOM line(s): {string.Join(", ", missingBomLines)}")
                .Field("RawMaterialCosts", "Missing cost for one or more BOM lines.")
                .Return();
```

- [ ] **Step 2: Migrate any other `{message}` guards in this controller**

Grep for `BadRequest(new { message` in `CostingController.cs` and migrate every occurrence. Examples likely include the "No BOM found" guard, the "No exchange rate for {code}" guard, the status-check guards.

For each, the pattern is `Validation.Detail("...").Field("...", "...").Return()`. Use sensible field keys: `BomHeaderId` for "No BOM found", `CurrencyCode` for "No exchange rate", `Status` for status mismatch.

- [ ] **Step 3: Rewrite the 3 existing CostingTests bodies**

For each of:
- `Submit_NegativeCost_Returns400`
- `Submit_UnknownBomLineId_Returns400`
- `Submit_MissingLineCost_Returns400`

Swap `ErrorResponse` → `ValidationProblemResponse`, `.Message` → `.Detail`.

- [ ] **Step 4: Run the filtered suite**

```bash
dotnet test --filter "FullyQualifiedName~CostingTests"
```

- [ ] **Step 5: Run full suite**

```bash
dotnet test
```

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/Features/Costing/CostingController.cs \
        BomPriceApproval.Tests/Costing/CostingTests.cs
git commit -m "feat(api): migrate CostingController.Submit guards to ValidationProblemDetails"
```

---

## Task 5: Migrate `ApprovalsController.Approve` guards

**Files:**
- Modify: `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`
- Modify: `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs`

- [ ] **Step 1: Add `using` and migrate `Approve` guards**

In `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`, add:

```csharp
using BomPriceApproval.API.Infrastructure.Validation;
```

Find the 6 guards in `Approve` (inserted in the validation-hardening work):

```csharp
        if (request.Items is null || request.Items.Count == 0)
            return BadRequest(new { message = "No items provided for approval." });

        if (request.Items.Any(i => i.SalesPricePerKgAed <= 0))
            return BadRequest(new { message = "SalesPrice must be greater than 0." });

        var inputIds = request.Items.Select(i => i.RequisitionItemId).ToList();
        if (inputIds.Count != inputIds.Distinct().Count())
            return BadRequest(new { message = "Duplicate items in approval request." });

        var requisitionItemIds = req.Items.Select(i => i.Id).ToList();
        var missingInputs = requisitionItemIds.Except(inputIds).ToList();
        if (missingInputs.Count > 0)
            return BadRequest(new { message = $"Missing price for item(s): {string.Join(", ", missingInputs)}" });

        var orphanInputs = inputIds.Except(requisitionItemIds).ToList();
        if (orphanInputs.Count > 0)
            return BadRequest(new { message = $"Unknown item(s) in request: {string.Join(", ", orphanInputs)}" });

        if (req.Items.Any(i => i.BomHeader?.Cost is null))
            return BadRequest(new { message = "All items must have a costed BOM before approval." });
```

Replace with:

```csharp
        if (request.Items is null || request.Items.Count == 0)
            return Validation
                .Detail("No items provided for approval.")
                .Field("Items", "No items provided for approval.")
                .Return();

        if (request.Items.Any(i => i.SalesPricePerKgAed <= 0))
        {
            var builder = Validation.Detail("SalesPrice must be greater than 0.");
            for (int i = 0; i < request.Items.Count; i++)
                if (request.Items[i].SalesPricePerKgAed <= 0)
                    builder.Field($"Items[{i}].SalesPricePerKgAed", "Must be greater than 0.");
            return builder.Return();
        }

        var inputIds = request.Items.Select(i => i.RequisitionItemId).ToList();
        if (inputIds.Count != inputIds.Distinct().Count())
            return Validation
                .Detail("Duplicate items in approval request.")
                .Field("Items", "Duplicate items in request.")
                .Return();

        var requisitionItemIds = req.Items.Select(i => i.Id).ToList();
        var missingInputs = requisitionItemIds.Except(inputIds).ToList();
        if (missingInputs.Count > 0)
            return Validation
                .Detail($"Missing price for item(s): {string.Join(", ", missingInputs)}")
                .Field("Items", "Missing price for one or more items.")
                .Return();

        var orphanInputSet = inputIds.Except(requisitionItemIds).ToHashSet();
        if (orphanInputSet.Count > 0)
        {
            var builder = Validation.Detail($"Unknown item(s) in request: {string.Join(", ", orphanInputSet)}");
            for (int i = 0; i < request.Items.Count; i++)
                if (orphanInputSet.Contains(request.Items[i].RequisitionItemId))
                    builder.Field($"Items[{i}].RequisitionItemId", "Unknown item.");
            return builder.Return();
        }

        if (req.Items.Any(i => i.BomHeader?.Cost is null))
            return Validation
                .Detail("All items must have a costed BOM before approval.")
                .Field("Items", "One or more items have no costed BOM.")
                .Return();
```

- [ ] **Step 2: Migrate any other `{message}` guards in this controller**

Grep for `BadRequest(new { message` in `ApprovalsController.cs`. At minimum, the status-check guards:

```csharp
if (req.Status != RequisitionStatus.MdReview)
    return BadRequest(new { message = "Requisition is not in MdReview status" });
```
Replace with:
```csharp
if (req.Status != RequisitionStatus.MdReview)
    return Validation
        .Detail("Requisition is not in MdReview status")
        .Field("Status", "Requisition is not in MdReview status.")
        .Return();
```

(This appears in both `Approve` and `Reject` — migrate both occurrences.)

- [ ] **Step 3: Rewrite the 5 existing ApprovalValidationTests bodies**

For each of:
- `Approve_ZeroPrice_Returns400`
- `Approve_MissingItemInInput_Returns400`
- `Approve_DuplicateItemInInput_Returns400`
- `Approve_NegativeMargin_Succeeds` (no body change — it asserts 200)
- `Approve_OrphanItemInInput_Returns400`

Swap `ErrorResponse` → `ValidationProblemResponse`, `.Message` → `.Detail`.

- [ ] **Step 4: Add per-field assertion test**

Append:

```csharp
    [Fact]
    public async Task Approve_ZeroPrice_EmitsPerFieldError()
    {
        var (reqId, itemIds) = await BootstrapToMdReviewAsync(itemCount: 1);
        var md = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", md);

        var resp = await _client.PostAsJsonAsync(
            $"/api/approvals/{reqId}/approve",
            new { Items = new[] { new { RequisitionItemId = itemIds[0], SalesPricePerKgAed = 0m } }, Notes = "" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        body!.Errors.Should().ContainKey("Items[0].SalesPricePerKgAed");
        body.Errors["Items[0].SalesPricePerKgAed"][0].Should().Contain("greater than 0");
    }
```

- [ ] **Step 5: Run the filtered suite**

```bash
dotnet test --filter "FullyQualifiedName~ApprovalValidationTests"
```

Expected: all 6 tests pass (5 migrated + 1 new).

- [ ] **Step 6: Run full suite**

```bash
dotnet test
```

Expected: all pass. **Backend is now fully migrated.** No `BadRequest(new { message` should remain in any of the 4 controllers. Verify with grep:

```bash
cd "D:\shan projects\BOM_Price_Approval"
```

Use the Grep tool with pattern `BadRequest\(new \{ message` scoped to `BomPriceApproval.API/Features/` — expected 0 matches.

- [ ] **Step 7: Commit**

```bash
git add BomPriceApproval.API/Features/Approvals/ApprovalsController.cs \
        BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs
git commit -m "feat(api): migrate ApprovalsController.Approve guards to ValidationProblemDetails"
```

---

## Task 6: Frontend `apiError.ts` rewrite + tests

**Files:**
- Modify: `bom-web/src/lib/apiError.ts`
- Modify: `bom-web/src/lib/apiError.test.ts`

- [ ] **Step 1: Rewrite the test file**

Replace the contents of `bom-web/src/lib/apiError.test.ts` with:

```ts
import { describe, it, expect } from "vitest";
import { extractApiError, extractFieldErrors } from "./apiError";

describe("extractApiError", () => {
  it("returns response.data.detail when present", () => {
    const err = { response: { data: { detail: "Bad qty" } } };
    expect(extractApiError(err)).toBe("Bad qty");
  });

  it("returns fallback when no detail", () => {
    const err = { response: { data: {} } };
    expect(extractApiError(err, "fallback")).toBe("fallback");
  });

  it("returns default fallback when no response", () => {
    expect(extractApiError(new Error("boom"))).toBe("Something went wrong");
  });

  it("handles unknown shapes safely", () => {
    expect(extractApiError(null)).toBe("Something went wrong");
    expect(extractApiError(undefined)).toBe("Something went wrong");
    expect(extractApiError("string")).toBe("Something went wrong");
  });
});

describe("extractFieldErrors", () => {
  it("extracts first message per field and normalizes PascalCase bracket → lowercase dot", () => {
    const err = { response: { data: { errors: { "Items[0].ExpectedQty": ["Must be > 0."] } } } };
    expect(extractFieldErrors(err)).toEqual({ "items.0.expectedqty": "Must be > 0." });
  });

  it("handles multi-field payloads", () => {
    const err = {
      response: {
        data: {
          errors: {
            "Items[1].ExpectedQty": ["A"],
            "Items[2].ItemId": ["B"],
          },
        },
      },
    };
    expect(extractFieldErrors(err)).toEqual({
      "items.1.expectedqty": "A",
      "items.2.itemid": "B",
    });
  });

  it("returns empty object when no errors field", () => {
    expect(extractFieldErrors({ response: { data: { detail: "x" } } })).toEqual({});
  });

  it("returns empty object for unknown shapes", () => {
    expect(extractFieldErrors(null)).toEqual({});
    expect(extractFieldErrors(undefined)).toEqual({});
    expect(extractFieldErrors(new Error("x"))).toEqual({});
  });
});
```

- [ ] **Step 2: Run — expect FAILs**

```bash
cd bom-web && npx vitest run src/lib/apiError.test.ts
```

Expected: the 4 new `extractFieldErrors` tests fail (function not exported). The 4 `extractApiError` tests also fail (still reading `.message`).

- [ ] **Step 3: Rewrite the helper**

Replace `bom-web/src/lib/apiError.ts` with:

```ts
export function extractApiError(err: unknown, fallback = "Something went wrong"): string {
  if (err && typeof err === "object" && "response" in err) {
    const resp = (err as { response?: { data?: { detail?: unknown } } }).response;
    const detail = resp?.data?.detail;
    if (typeof detail === "string" && detail.length > 0) return detail;
  }
  return fallback;
}

export function extractFieldErrors(err: unknown): Record<string, string> {
  if (!err || typeof err !== "object" || !("response" in err)) return {};
  const raw = (err as { response?: { data?: { errors?: unknown } } }).response?.data?.errors;
  if (!raw || typeof raw !== "object") return {};

  const out: Record<string, string> = {};
  for (const [key, value] of Object.entries(raw)) {
    if (Array.isArray(value) && typeof value[0] === "string") {
      out[normalizeFieldKey(key)] = value[0];
    }
  }
  return out;
}

function normalizeFieldKey(key: string): string {
  return key
    .replace(/\[(\d+)\]/g, ".$1") // "Items[2].ExpectedQty" → "Items.2.ExpectedQty"
    .toLowerCase(); // → "items.2.expectedqty"
}
```

- [ ] **Step 4: Run — expect 8/8 PASS**

```bash
cd bom-web && npx vitest run src/lib/apiError.test.ts
```

Expected: 8/8 pass.

- [ ] **Step 5: Run full frontend suite**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass. The existing `notify.fromApiError` mock-based page tests are unaffected — they spy on `notify`, not on `extractApiError`'s internals. And the backend is now fully on ProblemDetails (after T5) so any integration-style frontend fetches also work.

- [ ] **Step 6: Commit**

```bash
git add bom-web/src/lib/apiError.ts bom-web/src/lib/apiError.test.ts
git commit -m "refactor(web): read ProblemDetails detail; add extractFieldErrors helper"
```

---

## Task 7: Wire `NewRequisitionPage` per-field errors

**Files:**
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.tsx`
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`

- [ ] **Step 1: Write the failing test**

Append to the existing describe block in `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`:

```ts
  it("highlights the offending row when the server rejects a field", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes("/customers")) return Promise.resolve({ data: [{ id: 1, name: "ACME" }] });
      if (url.includes("/items"))
        return Promise.resolve({
          data: [{ id: 10, code: "A", description: "Item A", type: "FinishedGood", isActive: true }],
        });
      if (url.includes("/exchange-rates/active")) return Promise.resolve({ data: [] });
      return Promise.resolve({ data: [] });
    });

    vi.mocked(api.post).mockRejectedValueOnce({
      response: {
        data: {
          detail: "ExpectedQty must be greater than 0.",
          errors: { "Items[0].ExpectedQty": ["Must be greater than 0."] },
        },
      },
    });

    const user = userEvent.setup();
    wrap(<NewRequisitionPage />);
    await waitFor(() => expect(screen.getByText(/New Requisition/i)).toBeInTheDocument());

    // Select a customer and an item + positive qty (so client-side validation passes).
    // Click Customer picker, select ACME.
    await user.click(screen.getByPlaceholderText(/Search customers/i));
    await user.click(screen.getByText("ACME"));

    // Select Item A.
    await user.click(screen.getAllByPlaceholderText(/Search items/i)[0]);
    await user.click(screen.getByText("Item A"));

    // Enter valid qty (client passes, server rejects).
    await user.type(screen.getAllByPlaceholderText("Qty")[0], "5");
    await user.click(screen.getByRole("button", { name: /^Create$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Must be greater than 0\./i)).toBeInTheDocument(),
    );
  });
```

- [ ] **Step 2: Run — expect FAIL**

```bash
cd bom-web && npx vitest run src/features/requisitions/NewRequisitionPage.test.tsx
```

Expected: new test fails — the server-side error isn't currently wired to `setError`, so nothing renders.

- [ ] **Step 3: Wire `setError` in the catch**

In `bom-web/src/features/requisitions/NewRequisitionPage.tsx`:

**3a.** Import `extractFieldErrors`:

```ts
import { extractApiError, extractFieldErrors } from "@/lib/apiError";
```

(If `extractApiError` isn't currently imported, add `extractFieldErrors` alongside `notify` — whichever import line groups with `@/lib/apiError` and `@/lib/notify`.)

**3b.** In the existing `onSubmit` catch block — after toast migration it currently reads:

```ts
    } catch (e) {
      notify.fromApiError(e, "Failed to create requisition");
    }
```

Replace with:

```ts
    } catch (e) {
      const fields = extractFieldErrors(e);
      for (const [key, msg] of Object.entries(fields)) {
        setError(key as Path<FormValues>, { type: "server", message: msg });
      }
      notify.fromApiError(e, "Failed to create requisition");
    }
```

**3c.** Make sure `setError` and `Path` are destructured/imported. Change the existing `useForm` destructure to include `setError`:

```ts
  const {
    control,
    handleSubmit,
    register,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ ... });
```

And add the type import:

```ts
import type { Path } from "react-hook-form";
```

- [ ] **Step 4: Run — expect the failing test to now pass**

```bash
cd bom-web && npx vitest run src/features/requisitions/NewRequisitionPage.test.tsx
```

Expected: all tests pass.

**Note on RHF path case-sensitivity** (spec risk #2): react-hook-form matches paths by exact string. If `register("items.0.expectedQty", ...)` was camelCase and we set errors with lowercase `items.0.expectedqty`, RHF won't find the field.

Verify empirically: read the current `register(...)` call for the qty input in `NewRequisitionPage.tsx`. If it's `register("items.${index}.expectedQty", ...)` (camelCase), **adjust `normalizeFieldKey` in `bom-web/src/lib/apiError.ts`**: remove the `.toLowerCase()` step so paths stay camelCase after normalization. Then update the `apiError.test.ts` expectations to match (expected: `{ "items.0.expectedQty": "..." }` rather than `{ "items.0.expectedqty": "..." }`). Re-run both test files.

If `register` uses lowercase paths (e.g., `register("items.${index}.expectedqty")`), no adjustment needed.

- [ ] **Step 5: Run full frontend suite**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add bom-web/src/features/requisitions/NewRequisitionPage.tsx \
        bom-web/src/features/requisitions/NewRequisitionPage.test.tsx \
        bom-web/src/lib/apiError.ts \
        bom-web/src/lib/apiError.test.ts
git commit -m "feat(web): surface server field errors on NewRequisitionPage via setError"
```

(If the apiError case-sensitivity fix was needed, those files are included in the same commit.)

---

## Task 8: Wire `fieldErrors` state on BomEntryPage / CostingEntryPage / MdReviewPage

Three sub-commits.

### 8.A — `BomEntryPage`

**Files:**
- Modify: `bom-web/src/features/bom/BomEntryPage.tsx`

- [ ] **Step 1: Wire `fieldErrors` state**

In `bom-web/src/features/bom/BomEntryPage.tsx`:

**1a.** Add the import:

```ts
import { extractFieldErrors } from "@/lib/apiError";
```

**1b.** Near the other `useState` calls, add:

```ts
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
```

**1c.** Update `handleSubmit` to populate `fieldErrors` on error and clear on success. Current (after toast migration):

```ts
  function handleSubmit() {
    submitBom.mutate(requisitionId, {
      onSuccess: () => {
        notify.success("BOM submitted for costing");
        navigate(`/requisitions/${requisitionId}`);
      },
      onError: (err) => notify.fromApiError(err, "Failed to submit BOM"),
    });
  }
```

Replace with:

```ts
  function handleSubmit() {
    setFieldErrors({});
    submitBom.mutate(requisitionId, {
      onSuccess: () => {
        notify.success("BOM submitted for costing");
        navigate(`/requisitions/${requisitionId}`);
      },
      onError: (err) => {
        setFieldErrors(extractFieldErrors(err));
        notify.fromApiError(err, "Failed to submit BOM");
      },
    });
  }
```

**1d.** Render field errors on the BOM line inputs. This page's "lines" live in an array — each has a `qtyPerKg` and `wastagePct` input. The server error keys are `lines.0.qtyperkg`, `lines.0.wastagepct`, `lines.0.processid`, `lines.0.rawmaterialitemid` (assuming lowercase per `normalizeFieldKey`; adjust if Task 7 found case-sensitivity needed the `.toLowerCase()` stripped).

Find the BOM line row render (around lines 440–480). At the existing qty input:

```tsx
                            <input
                              type="number"
                              step="0.0001"
                              min="0"
                              placeholder="0.0000"
                              value={pendingLine.qtyPerKg}
                              onChange={(e) => setPendingLine((p) => ({ ...p, qtyPerKg: e.target.value }))}
                              className="h-10 rounded-md border border-input bg-background px-3 text-sm font-mono"
                              aria-label="Qty per kg"
                            />
```

This is for the pending (new) line. The already-saved lines are in the `sectionLines.map` loop — those are read-only displays, not editable inputs. Server errors from a failed submit apply to the already-saved lines. The page currently shows those as static text:

```tsx
                        {sectionLines.map((line, idx) => (
                          <div ...>
                            <span>{line.rawMaterialDescription}</span>
                            <span className="font-mono">{line.qtyPerKg.toFixed(4)}</span>
                            <span className="font-mono">{line.wastagePct.toFixed(2)}%</span>
                            ...
```

For each row, add red styling + inline helper when a field error exists. Since `sectionLines` indexes are local, you need the overall line index. Replace `sectionLines.map((line, idx) =>` logic by computing the overall `lines` index for each `sectionLine`:

```tsx
                        {sectionLines.map((line) => {
                          const overallIdx = lines.findIndex((l) => l.localId === line.localId);
                          const qtyErr = fieldErrors[`lines.${overallIdx}.qtyperkg`];
                          const wasteErr = fieldErrors[`lines.${overallIdx}.wastagepct`];
                          const procErr = fieldErrors[`lines.${overallIdx}.processid`];
                          const rmErr = fieldErrors[`lines.${overallIdx}.rawmaterialitemid`];
                          return (
                            <div
                              key={line.localId}
                              className="grid grid-cols-[1fr_100px_90px_32px] gap-2 px-4 py-2 text-sm border-b border-border items-start"
                            >
                              <div>
                                <span>{line.rawMaterialDescription}</span>
                                {(procErr || rmErr) && (
                                  <p className="text-xs text-destructive">{procErr ?? rmErr}</p>
                                )}
                              </div>
                              <div>
                                <span className={`font-mono ${qtyErr ? "text-destructive" : ""}`}>{line.qtyPerKg.toFixed(4)}</span>
                                {qtyErr && <p className="text-xs text-destructive">{qtyErr}</p>}
                              </div>
                              <div>
                                <span className={`font-mono ${wasteErr ? "text-destructive" : ""}`}>{line.wastagePct.toFixed(2)}%</span>
                                {wasteErr && <p className="text-xs text-destructive">{wasteErr}</p>}
                              </div>
                              {canEdit && (
                                <button
                                  type="button"
                                  onClick={() => removeLine(line.localId)}
                                  className="text-destructive hover:opacity-70"
                                  aria-label="Remove line"
                                >
                                  <X className="h-4 w-4" />
                                </button>
                              )}
                            </div>
                          );
                        })}
```

**1e.** Clear `fieldErrors` when the user edits (auto-save path `doSave` in the existing code). At the top of `doSave`:

```ts
  function doSave(newLines: LocalLine[], prevLines: LocalLine[]) {
    if (!selectedItemId) return;
    setFieldErrors({});
    setSaveStatus("saving");
    ...
```

This clears stale errors when the user modifies anything.

- [ ] **Step 2: Run tests (no new tests — manual verification only via the full suite)**

```bash
cd bom-web && npx vitest run src/features/bom/BomEntryPage.test.tsx
```

Expected: all existing tests pass. The new wiring affects only the error-path UX which existing tests don't assert on.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/bom/BomEntryPage.tsx
git commit -m "feat(web): surface server field errors on BomEntryPage lines"
```

### 8.B — `CostingEntryPage`

**Files:**
- Modify: `bom-web/src/features/costing/CostingEntryPage.tsx`

- [ ] **Step 1: Wire `fieldErrors` state**

In `bom-web/src/features/costing/CostingEntryPage.tsx`:

**1a.** Add import:

```ts
import { extractFieldErrors } from "@/lib/apiError";
```

**1b.** Add state near the other `useState` calls:

```ts
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
```

**1c.** Update `handleSubmitItem` (after toast migration). Current:

```ts
  function handleSubmitItem() {
    if (!selectedItemId) return;
    submitCostingItem.mutate(
      { ... },
      {
        onSuccess: () => { ... },
        onError: (err: unknown) => notify.fromApiError(err, "Failed to submit costing."),
      },
    );
  }
```

Replace:

```ts
  function handleSubmitItem() {
    if (!selectedItemId) return;
    setFieldErrors({});
    submitCostingItem.mutate(
      { ... (unchanged payload) ... },
      {
        onSuccess: () => { ... (unchanged) ... },
        onError: (err: unknown) => {
          setFieldErrors(extractFieldErrors(err));
          notify.fromApiError(err, "Failed to submit costing.");
        },
      },
    );
  }
```

(Keep the existing `onSuccess` body — refetchCosting, notify.success, conditional navigate.)

**1d.** Render red border on cost inputs when a server error exists. Find the cost input in the `sectionLines.map` loop:

```tsx
                              <input
                                type="number"
                                step="0.0001"
                                min="0"
                                disabled={!canEditItem}
                                value={line.costPerKg || ""}
                                onChange={(e) =>
                                  updateLine(line.bomLineId, { costPerKg: parseFloat(e.target.value) || 0 })
                                }
                                className="h-9 rounded-md border border-input bg-background px-2 text-sm font-mono"
                                aria-label={`Cost per kg for ${line.rawMaterialDescription}`}
                              />
```

Compute the line index once per row in the surrounding `map` and wrap the input. Replace the `sectionLines.map((line) => {` body to compute and use the field error:

```tsx
                        {sectionLines.map((line) => {
                          const overallIdx = lines.findIndex((l) => l.bomLineId === line.bomLineId);
                          const costErr = fieldErrors[`rawmaterialcosts.${overallIdx}.costperkg`];
                          const bomLineErr = fieldErrors[`rawmaterialcosts.${overallIdx}.bomlineid`];
                          const ageDays = line.lastCost ? daysSince(line.lastCost.updatedAt) : null;
                          const stale = ageDays !== null && ageDays > STALE_DAYS;
                          return (
                            <div
                              key={line.bomLineId}
                              className="grid grid-cols-[2fr_80px_80px_120px_90px_2fr] gap-2 px-4 py-2 text-sm border-b border-border items-start"
                            >
                              <span>
                                {line.rawMaterialDescription}
                                {bomLineErr && <p className="text-xs text-destructive">{bomLineErr}</p>}
                              </span>
                              <span className="font-mono text-muted-foreground">{line.qtyPerKg.toFixed(4)}</span>
                              <span className="font-mono text-muted-foreground">{line.wastagePct.toFixed(2)}%</span>
                              <div>
                                <input
                                  type="number"
                                  step="0.0001"
                                  min="0"
                                  disabled={!canEditItem}
                                  value={line.costPerKg || ""}
                                  onChange={(e) => {
                                    setFieldErrors({});
                                    updateLine(line.bomLineId, { costPerKg: parseFloat(e.target.value) || 0 });
                                  }}
                                  className={`h-9 rounded-md border bg-background px-2 text-sm font-mono ${costErr ? "border-destructive" : "border-input"}`}
                                  aria-label={`Cost per kg for ${line.rawMaterialDescription}`}
                                />
                                {costErr && <p className="text-xs text-destructive">{costErr}</p>}
                              </div>
                              <select
                                disabled={!canEditItem}
                                value={line.currencyCode}
                                onChange={(e) => updateLine(line.bomLineId, { currencyCode: e.target.value })}
                                className="h-9 rounded-md border border-input bg-background px-2 text-sm"
                                aria-label={`Currency for ${line.rawMaterialDescription}`}
                              >
                                {currencyOptions.map((c) => (
                                  <option key={c} value={c}>{c}</option>
                                ))}
                              </select>
                              {line.lastCost ? (
                                <span className={`text-xs ${stale ? "text-yellow-400" : "text-muted-foreground"}`}>
                                  {stale && "! "}
                                  {line.lastCost.currencyCode} {line.lastCost.costPerKg.toFixed(4)} · {ageDays} days ago
                                </span>
                              ) : (
                                <span className="text-xs text-muted-foreground/60">No previous price</span>
                              )}
                            </div>
                          );
                        })}
```

Note the `setFieldErrors({});` at the top of the cost input's `onChange` — clears stale server errors when the user edits.

- [ ] **Step 2: Run tests**

```bash
cd bom-web && npx vitest run src/features/costing/CostingEntryPage.test.tsx
```

Expected: all existing tests pass.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/costing/CostingEntryPage.tsx
git commit -m "feat(web): surface server field errors on CostingEntryPage cost inputs"
```

### 8.C — `MdReviewPage`

**Files:**
- Modify: `bom-web/src/features/approvals/MdReviewPage.tsx`

- [ ] **Step 1: Wire `fieldErrors` state**

In `bom-web/src/features/approvals/MdReviewPage.tsx`:

**1a.** Add import:

```ts
import { extractFieldErrors } from "@/lib/apiError";
```

**1b.** Add state near the other `useState` calls:

```ts
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
```

**1c.** Update `handleApprove`. Current (after toast migration):

```ts
  async function handleApprove() {
    const items = data!.items.map((item) => {
      const price = Number(salesPrices[item.requisitionItemId] ?? "");
      return { requisitionItemId: item.requisitionItemId, salesPricePerKgAed: price };
    });
    if (items.some((i) => !Number.isFinite(i.salesPricePerKgAed) || i.salesPricePerKgAed <= 0)) {
      notify.error("Enter a valid sales price for all items.");
      return;
    }
    try {
      await approve.mutateAsync({ ... });
      notify.success("Quotation approved");
      setPageState({ kind: "approved" });
    } catch (e) {
      notify.fromApiError(e, "Failed to approve.");
    }
  }
```

Replace with:

```ts
  async function handleApprove() {
    setFieldErrors({});
    const items = data!.items.map((item) => {
      const price = Number(salesPrices[item.requisitionItemId] ?? "");
      return { requisitionItemId: item.requisitionItemId, salesPricePerKgAed: price };
    });
    if (items.some((i) => !Number.isFinite(i.salesPricePerKgAed) || i.salesPricePerKgAed <= 0)) {
      notify.error("Enter a valid sales price for all items.");
      return;
    }
    try {
      await approve.mutateAsync({
        requisitionId,
        payload: { items, notes: notes || undefined },
      });
      notify.success("Quotation approved");
      setPageState({ kind: "approved" });
    } catch (e) {
      setFieldErrors(extractFieldErrors(e));
      notify.fromApiError(e, "Failed to approve.");
    }
  }
```

(Keep the existing `approve.mutateAsync` payload verbatim — the snippet above uses `{ ... }` for brevity but the real code has the full payload.)

**1d.** Render red border + inline message on the sales-price inputs. Find the per-item render loop (around line 172). For each item, compute the price input's field key:

```tsx
        {data.items.map((item, idx) => {
          const priceStr = salesPrices[item.requisitionItemId] ?? "";
          const price = Number(priceStr);
          const hasValidPrice = Number.isFinite(price) && price > 0;
          const marginPct = hasValidPrice ? ((price - item.totalCostPerKg) / price) * 100 : 0;
          const priceErr = fieldErrors[`items.${idx}.salespriceperkgaed`];
```

Then at the price input:

```tsx
                        <input
                          type="number"
                          step="0.0001"
                          min="0"
                          value={priceStr}
                          onChange={(e) => {
                            setFieldErrors({});
                            setSalesPrices((prev) => ({
                              ...prev,
                              [item.requisitionItemId]: e.target.value,
                            }));
                          }}
                          className={`w-full rounded-md border px-3 py-2 text-sm ${priceErr ? "border-destructive" : ""}`}
                          placeholder="0.0000"
                          aria-label="Sales Price (AED/kg)"
                        />
                        {priceErr && <p className="text-xs text-destructive">{priceErr}</p>}
```

- [ ] **Step 2: Run tests**

```bash
cd bom-web && npx vitest run src/features/approvals/MdReviewPage.test.tsx
```

Expected: all existing tests pass.

- [ ] **Step 3: Run the full frontend suite**

```bash
cd bom-web && npx vitest run
```

Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/approvals/MdReviewPage.tsx
git commit -m "feat(web): surface server field errors on MdReviewPage price inputs"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|---|---|
| Validation fluent builder | 1 |
| `ValidationProblemDetails` response shape, Content-Type application/problem+json | 1 (builder) + verified in all migration tasks |
| `Detail` preserves existing message wording | 2, 3, 4, 5 (all migrations use the verbatim strings) |
| `Field` keys use `Collection[idx].Property` (per-row offender) | 2, 3, 4, 5 |
| `Field` keys use `Collection` (array-level / absent rows / requisition-state) | 2, 3, 4, 5 |
| RequisitionsController migration (Create + AddItem + RemoveItem + aux) | 2 |
| BomController.SaveLines migration | 3 |
| CostingController.Submit migration | 4 |
| ApprovalsController.Approve migration (incl. Reject status-check) | 5 |
| Backend per-field assertion tests | 2 (Create_ZeroQty_EmitsPerFieldError) + 5 (Approve_ZeroPrice_EmitsPerFieldError) |
| All ~18 existing test bodies rewritten to read `ValidationProblemResponse.Detail` | 2, 3, 4, 5 |
| Full cutover — 0 `BadRequest(new { message` remaining | 5 Step 6 grep |
| `apiError.ts`: `extractApiError` reads `detail` | 6 |
| `apiError.ts`: `extractFieldErrors` helper | 6 |
| 4 `extractApiError` tests updated | 6 |
| 4 `extractFieldErrors` tests added | 6 |
| NewRequisitionPage `setError` wiring + test | 7 |
| BomEntryPage `fieldErrors` state + red borders | 8.A |
| CostingEntryPage `fieldErrors` state + red borders | 8.B |
| MdReviewPage `fieldErrors` state + red borders | 8.C |
| Clear field errors on user edit | 8.A (doSave), 8.B (cost input onChange), 8.C (price input onChange) |

**Placeholder scan:** none. Every step contains exact code or precise migration instructions. The case-sensitivity adjustment in Task 7 is flagged as a verification with a specific fallback plan.

**Type consistency:**
- `Validation.Detail(string).Field(string, string).Return(): ActionResult` — defined in T1, used by T2/T3/T4/T5.
- `ValidationProblemResponse(Detail, Errors)` — defined in T1, used in all 4 backend test task rewrites.
- `extractFieldErrors(err): Record<string, string>` — defined in T6, used by T7/T8.A/T8.B/T8.C.
- `fieldErrors: Record<string, string>` — shape consistent across all 3 state-based pages.
- Field key naming convention: lowercase dot notation throughout frontend (subject to T7's case-sensitivity verification).

**Scope:** focused on per-field error schema. No scope drift. The `Reject` status-check migration in T5 is technically outside the "guard hardening" set but falls naturally within "migrate all `{message}` responses in these controllers" — keeping it here avoids a leftover.

**Dependency order:** T1 must run first (builder). T2–T5 run sequentially (each commits its migration + tests). T6 after T5 (frontend cutover once all backend is new-shape). T7 after T6 (needs extractFieldErrors). T8.A/B/C after T7 (depend on apiError + established patterns). No parallel opportunities — the plan runs strictly sequentially for subagent-driven review coherence.
