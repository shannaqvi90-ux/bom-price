# Per-Field Error Schema — Design Spec

**Date:** 2026-04-16
**Status:** Approved (design); pending implementation plan
**Scope:** Migrate all 4 workflow controllers' manual `BadRequest` guards to RFC 7807 `ValidationProblemDetails`; update frontend to consume per-field errors.

---

## Goal

Replace the ad-hoc `{ message: string }` 400 response shape with ASP.NET Core's standard `ValidationProblemDetails` (RFC 7807) across every manual guard we added during the multi-item validation hardening. On the frontend, surface per-field errors as inline red borders + messages on the specific inputs that failed, while preserving the existing toast-summary UX.

## Non-Goals

- No changes to auth 401s, 403 Forbid, 404 NotFound, or 500 exception responses.
- No changes to model-binding auto-generated validation (ASP.NET already emits ProblemDetails for those when `[ApiController]` is active).
- No i18n / localization of error messages.
- No structured error codes (`type` stays generic — RFC 7231 §6.5.1).
- No server-driven toast / modal flavoring — the toast remains a plain `detail` string.

---

## Design Principles

- **RFC 7807 purity.** Response body is a literal `ValidationProblemDetails`. Content-Type is `application/problem+json`. No custom fields bolted on.
- **Full cutover, no compat layer.** The frontend reads `detail` exclusively; the backend emits only the new shape. Backend tests migrate alongside. No transitional `message` field.
- **Progressive enhancement on the frontend.** The toast (summary) fires for every 400, same as today. Field-level highlighting is an additional improvement where the UI can express it.
- **One entry point for building problems** — a fluent builder in `BomPriceApproval.API/Infrastructure/Validation/Validation.cs` that every guard uses. Callsites stay readable; shape is centralized.

---

## Policy Decisions

| # | Decision | Choice |
|---|---|---|
| 1 | Scope | Full: all 13+ backend guards + all 4 frontend pages |
| 2 | Response shape | ASP.NET Core `ValidationProblemDetails` (RFC 7807) |
| 3 | Toast summary source | `detail` field of the ProblemDetails |
| 4 | Migration strategy | Full cutover — no `message` compat layer |
| 5 | Builder architecture | Fluent static API (Approach 3): `Validation.Detail(...).Field(...).Return()` |

---

## Architecture

```
Backend guard callsites (controllers)
    │ ValidationProblem fluent API
    ▼
BomPriceApproval.API/Infrastructure/Validation/Validation.cs
    • Validation.Detail(msg) → ValidationProblemBuilder
    • .Field(key, msg) → ValidationProblemBuilder
    • .Return() → ActionResult (BadRequest + application/problem+json)
    │ HTTP 400 application/problem+json body (RFC 7807)
    ▼
bom-web/src/lib/apiError.ts
    • extractApiError(err) → string (reads .detail)
    • extractFieldErrors(err) → Record<string,string> (NEW)
    │
    ├─► notify.fromApiError(err) → toast.error(detail)
    ▼
Frontend consumers
    • NewRequisitionPage: react-hook-form setError per field
    • BomEntryPage / CostingEntryPage / MdReviewPage: fieldErrors state +
      per-input red border + inline message
```

**Response body example** (single-field case):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "ExpectedQty must be greater than 0.",
  "errors": {
    "Items[2].ExpectedQty": ["Must be greater than 0."]
  }
}
```

**Response body example** (multi-offender, e.g. missing price for multiple approval items):
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "Missing price for item(s): 3, 7",
  "errors": {
    "Items": ["Missing price for item(s): 3, 7"]
  }
}
```

---

## Layer 1 — Backend: Fluent Builder

Single new file `BomPriceApproval.API/Infrastructure/Validation/Validation.cs`:

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

**API shape rationale:**
- `Detail` is positional and required — every ValidationProblem must have a human-readable summary.
- `Field` is additive; chainable; must be called at least once in most cases but can be omitted for array-level-only errors.
- `Return()` is the terminal; compiler enforces `ActionResult` return.
- No base class, no DI. Pure static; works from any controller.

---

## Layer 2 — Backend: Guard Migrations (field conventions)

| Controller / Method | Guard | Detail (today's `message` verbatim) | Field key |
|---|---|---|---|
| Requisitions.Create | Items empty | `"At least one item is required."` | `Items` |
| Requisitions.Create | ExpectedQty ≤ 0 | `"ExpectedQty must be greater than 0."` | `Items[idx].ExpectedQty` (per offender) |
| Requisitions.Create | Duplicate ItemIds | `"Duplicate items in requisition are not allowed."` | `Items` |
| Requisitions.Create | Unknown/inactive items | `"Unknown or inactive items: 5, 7"` | `Items[idx].ItemId` (per offender) |
| Requisitions.AddItem | ExpectedQty ≤ 0 | same | `ExpectedQty` |
| Requisitions.AddItem | Already in requisition | `"Item already added to this requisition."` | `ItemId` |
| Requisitions.AddItem | Unknown/inactive | `"Unknown or inactive item: {id}"` | `ItemId` |
| Bom.SaveLines | QtyPerKg ≤ 0 | `"QtyPerKg must be greater than 0."` | `Lines[idx].QtyPerKg` (per offender) |
| Bom.SaveLines | WastagePct < 0 | `"WastagePct cannot be negative."` | `Lines[idx].WastagePct` (per offender) |
| Bom.SaveLines | Invalid ProcessId | `"One or more ProcessIds are invalid."` | `Lines[idx].ProcessId` (per offender) |
| Bom.SaveLines | Invalid RawMaterialItemId | `"One or more RawMaterialItemIds are invalid or inactive."` | `Lines[idx].RawMaterialItemId` (per offender) |
| Costing.Submit | CostPerKg < 0 | `"CostPerKg cannot be negative."` | `RawMaterialCosts[idx].CostPerKg` (per offender) |
| Costing.Submit | Unknown BomLineId | `"Unknown BOM line(s): 5, 7"` | `RawMaterialCosts[idx].BomLineId` (per offender) |
| Costing.Submit | Missing BomLineIds | `"Missing cost for BOM line(s): 5, 7"` | `RawMaterialCosts` (array-level — absent rows have no index) |
| Approvals.Approve | Items empty | `"No items provided for approval."` | `Items` |
| Approvals.Approve | SalesPrice ≤ 0 | `"SalesPrice must be greater than 0."` | `Items[idx].SalesPricePerKgAed` (per offender) |
| Approvals.Approve | Duplicate RequisitionItemIds | `"Duplicate items in approval request."` | `Items` |
| Approvals.Approve | Missing items in input | `"Missing price for item(s): 5, 7"` | `Items` (absent inputs) |
| Approvals.Approve | Orphan items in input | `"Unknown item(s) in request: 5, 7"` | `Items[idx].RequisitionItemId` (per offender) |
| Approvals.Approve | Any item has uncosted BOM | `"All items must have a costed BOM before approval."` | `Items` (requisition-state invariant, not input-shape) |

**Rules for index vs array-level:**
- Offender is a **row present in the input** → `Collection[idx].Property`.
- Error is about **input structure** (empty, duplicate, absent rows) or a **requisition-state invariant** (unrelated to input shape) → `Collection` (no index).

**Detail strings are unchanged.** Tests assert on substring of `.Detail`; migration is a one-line swap per assertion.

### Callsite pattern (from Approach 3 presentation)

```csharp
if (req.Items.Any(i => i.ExpectedQty <= 0))
{
    var builder = Validation.Detail("ExpectedQty must be greater than 0.");
    for (int i = 0; i < req.Items.Count; i++)
        if (req.Items[i].ExpectedQty <= 0)
            builder.Field($"Items[{i}].ExpectedQty", "Must be greater than 0.");
    return builder.Return();
}
```

The "find all offenders, add each as a Field, Return" pattern repeats for every per-row guard. Array-level guards drop the loop.

---

## Layer 3 — Frontend: `apiError.ts` Rewrite

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
    .toLowerCase();                // → "items.2.expectedqty"
}
```

- `extractApiError` now reads `detail` (not `message`). `notify.fromApiError` continues to work transparently.
- `extractFieldErrors` is new — normalizes PascalCase bracket keys to lowercase dot notation so they match react-hook-form path conventions and typical JS/TSX consumer expectations.
- First message per field wins (`value[0]`). Multi-message-per-field is legal in RFC 7807 but our builder only emits one.

---

## Layer 4 — Frontend: Per-Page Consumption

### `NewRequisitionPage.tsx` — uses react-hook-form's `setError`

```tsx
import { extractFieldErrors } from "@/lib/apiError";

// inside onSubmit catch
} catch (e) {
  const fields = extractFieldErrors(e);
  for (const [key, msg] of Object.entries(fields)) {
    setError(key as Path<FormValues>, { type: "server", message: msg });
  }
  notify.fromApiError(e, "Failed to create requisition");
}
```

- RHF persistently displays server errors until the user changes the field — automatic cleanup.
- Existing inline error render paths (`errors.items?.[index]?.expectedQty?.message`) pick up server-set errors without any template change.

### `BomEntryPage.tsx`, `CostingEntryPage.tsx`, `MdReviewPage.tsx` — local `fieldErrors` state

```tsx
const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

// mutation onError
onError: (err) => {
  setFieldErrors(extractFieldErrors(err));
  notify.fromApiError(err, "Failed to submit BOM");
},

// mutation onSuccess (or before every mutate)
onSuccess: () => setFieldErrors({}),
```

Each input renders its own error slice:
```tsx
<input
  className={cn(base, fieldErrors[`lines.${idx}.qtyperkg`] && "border-destructive")}
  ...
/>
{fieldErrors[`lines.${idx}.qtyperkg`] && (
  <p className="text-xs text-destructive">{fieldErrors[`lines.${idx}.qtyperkg`]}</p>
)}
```

**Key points:**
- Toast still fires via `notify.fromApiError` — field highlights are additive UX.
- Clear `fieldErrors` on mutation success AND on manual resets (e.g. switching selected item on BomEntryPage).
- Field keys are lowercase + dot-notation throughout the frontend (`lines.0.qtyperkg`, not `Lines[0].QtyPerKg`).

---

## Testing

### Backend

**`BomPriceApproval.Tests/Shared/TestDtos.cs` — add:**
```csharp
public record ValidationProblemResponse(string Detail, Dictionary<string, string[]> Errors);
```
Keep the existing `ErrorResponse` for any non-ProblemDetails paths that survive (none expected, but safer).

**Migrate ~18 existing test bodies** (mechanical 2-line swap):
- `ValidationTests.cs` (8 tests)
- `BomSaveLinesTests.cs` (2 tests)
- `CostingTests.cs` (3 tests)
- `ApprovalValidationTests.cs` (5 tests)

Each: `ReadFromJsonAsync<ErrorResponse>()` → `<ValidationProblemResponse>()`; `.Message.Should().Contain(...)` → `.Detail.Should().Contain(...)`.

**Add 2 new tests** verifying per-field `errors` dictionary population:
- `ValidationTests.Create_ZeroQty_EmitsPerFieldError` — asserts `errors["Items[0].ExpectedQty"]` exists.
- `ApprovalValidationTests.Approve_ZeroPrice_EmitsPerFieldError` — asserts `errors["Items[0].SalesPricePerKgAed"]` exists.

### Frontend

**`apiError.test.ts`:**
- Update 4 existing tests to use `detail` in the mocked error.
- Add 4 new tests for `extractFieldErrors` (single field, multi-field, missing errors, unknown shapes).

**Page test files:**
- Existing `notify.fromApiError` spy assertions unchanged.
- Add 1 new test to `NewRequisitionPage.test.tsx` that simulates a 400 with per-field `errors` and verifies red-border on the right row.
- Optionally add similar tests to the other 3 pages (up to 3 more).

### Test count delta

| Layer | Existing (rewritten) | New | Net |
|---|---|---|---|
| Backend | ~18 | +2 | +2 |
| Frontend | 4 | +4 (helper) + up to +4 (pages) | ~+8 |
| **Total** | | | **+10** |

---

## Files Changed (Summary)

### New
- `BomPriceApproval.API/Infrastructure/Validation/Validation.cs` (~45 lines)

### Modified — Backend
- `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`
- `BomPriceApproval.API/Features/Bom/BomController.cs`
- `BomPriceApproval.API/Features/Costing/CostingController.cs`
- `BomPriceApproval.API/Features/Approvals/ApprovalsController.cs`

### Modified — Backend Tests
- `BomPriceApproval.Tests/Shared/TestDtos.cs` (add `ValidationProblemResponse`)
- `BomPriceApproval.Tests/Requisitions/ValidationTests.cs` (rewrite 8 + add 1)
- `BomPriceApproval.Tests/Bom/BomSaveLinesTests.cs` (rewrite 2)
- `BomPriceApproval.Tests/Costing/CostingTests.cs` (rewrite 3)
- `BomPriceApproval.Tests/Approvals/ApprovalValidationTests.cs` (rewrite 5 + add 1)

### Modified — Frontend
- `bom-web/src/lib/apiError.ts` (rewrite)
- `bom-web/src/lib/apiError.test.ts` (update + expand)
- `bom-web/src/features/requisitions/NewRequisitionPage.tsx` (setError in catch)
- `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx` (+1 test)
- `bom-web/src/features/bom/BomEntryPage.tsx` (fieldErrors state + red-border per input)
- `bom-web/src/features/bom/BomEntryPage.test.tsx` (+1 test optional)
- `bom-web/src/features/costing/CostingEntryPage.tsx` (same treatment)
- `bom-web/src/features/costing/CostingEntryPage.test.tsx` (+1 test optional)
- `bom-web/src/features/approvals/MdReviewPage.tsx` (same treatment)
- `bom-web/src/features/approvals/MdReviewPage.test.tsx` (+1 test optional)

Net diff estimate: ~100 lines backend (helper + guard rewrites), ~250 lines frontend (apiError rewrite + 4 pages gain state + input attrs), ~120 lines tests.

---

## Risks & Open Questions

1. **`ModelStateDictionary.AddModelError(key, ...)` with bracketed keys.** ASP.NET Core serializes the keys verbatim into `errors`, so `Items[0].ExpectedQty` should appear with brackets preserved. Need to verify via a smoke test during Task 1 (build the helper, return it from a test controller, assert JSON body shape).

2. **React-hook-form path syntax.** Setting `setError("items.0.expectedqty", ...)` on a field registered as `items.0.expectedQty` (camelCase) may not match — RHF is case-sensitive. The `normalizeFieldKey` function lowercases; NewRequisitionPage's `register(...)` calls use camelCase. Mitigation: use lower-case paths throughout (`items.0.expectedqty`). Verify during Task 7 (NewRequisitionPage migration) whether RHF matches case-insensitively or we need to keep original camelCase. If the latter, `normalizeFieldKey` drops the `.toLowerCase()` step — shape stays dotted but case-preserved.

3. **Content-Type negotiation.** If any existing frontend axios interceptor expects `application/json` and rejects `application/problem+json`, responses will fail silently. Default axios does NOT filter by content-type; verify during Task 1 smoke test.

4. **`[ApiController]` auto-generated ProblemDetails for binding failures.** ASP.NET Core auto-emits ProblemDetails when `[ApiController]` + failed model binding (e.g., malformed JSON). Those already match the shape we're adopting; no extra work but worth noting the coexistence.
