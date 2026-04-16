# Toast Notifications Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an app-wide toast notification channel using [sonner](https://sonner.emilkowal.ski/) so transient feedback (backend errors, action confirmations, real-time SignalR events) appears as toasts instead of inline error spans on four pages.

**Architecture:** Thin `notify` wrapper over sonner (single entry point, callers never import sonner directly). `<Toaster />` mounted once in `App.tsx`. Four pages migrate from inline `serverError` / `submitError` / `validationError` state to `notify.fromApiError(err)` / `notify.success(...)` calls. `notificationsStore` fires `notify.info(..., { action })` on every SignalR `ReceiveNotification` with a clickable "View" that navigates to the related entity.

**Tech Stack:** React 19, TanStack Query, Zustand, TypeScript, Vitest + RTL, sonner (new).

**Spec:** `docs/superpowers/specs/2026-04-16-toast-notifications-design.md`

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `bom-web/package.json` + `package-lock.json` | Add `sonner` dep |
| Create | `bom-web/src/lib/notify.ts` | `notify.error/success/info/fromApiError` wrapper |
| Create | `bom-web/src/lib/notify.test.ts` | 5 unit tests for the wrapper |
| Modify | `bom-web/src/App.tsx` | Mount `<Toaster />` as sibling of `RouterProvider` |
| Modify | `bom-web/src/features/requisitions/NewRequisitionPage.tsx` | Remove `serverError` state; use `notify` |
| Modify | `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx` | Mock `@/lib/notify` |
| Modify | `bom-web/src/features/bom/BomEntryPage.tsx` | Remove `submitError` state; use `notify` |
| Modify | `bom-web/src/features/bom/BomEntryPage.test.tsx` | Mock `@/lib/notify` |
| Modify | `bom-web/src/features/costing/CostingEntryPage.tsx` | Remove `submitError` state; use `notify` |
| Modify | `bom-web/src/features/costing/CostingEntryPage.test.tsx` | Mock `@/lib/notify` |
| Modify | `bom-web/src/features/approvals/MdReviewPage.tsx` | Remove `validationError` state; use `notify` |
| Modify | `bom-web/src/features/approvals/MdReviewPage.test.tsx` | Swap text assertion to `notify.error` spy |
| Modify | `bom-web/src/store/notificationsStore.ts` | Fire `notify.info` on `ReceiveNotification` + add `pathForNotification` helper |
| Create | `bom-web/src/store/notificationsStore.test.ts` | 2 unit tests covering SignalR → toast handler |

---

## Task 1: Install sonner + create `notify` wrapper with tests

**Files:**
- Modify: `bom-web/package.json`
- Create: `bom-web/src/lib/notify.ts`
- Create: `bom-web/src/lib/notify.test.ts`

- [ ] **Step 1: Install sonner**

```bash
cd bom-web && npm install sonner
```

Expected: `sonner` appears in `package.json` under `dependencies`, `package-lock.json` updated.

- [ ] **Step 2: Write the failing test file**

Create `bom-web/src/lib/notify.test.ts`:

```ts
import { vi, describe, it, expect, beforeEach } from "vitest";
import { toast } from "sonner";
import { notify } from "./notify";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), {
    error: vi.fn(),
    success: vi.fn(),
  }),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("notify", () => {
  it("error calls toast.error with 6s default duration", () => {
    notify.error("oops");
    expect(toast.error).toHaveBeenCalledWith("oops", { duration: 6000 });
  });

  it("success calls toast.success with 3s default duration", () => {
    notify.success("ok");
    expect(toast.success).toHaveBeenCalledWith("ok", { duration: 3000 });
  });

  it("info calls toast with 4s default and optional action", () => {
    const onClick = vi.fn();
    notify.info("hi", { action: { label: "View", onClick } });
    expect(toast).toHaveBeenCalledWith(
      "hi",
      expect.objectContaining({
        duration: 4000,
        action: { label: "View", onClick },
      }),
    );
  });

  it("fromApiError extracts message and calls error", () => {
    const err = { response: { data: { message: "Bad request" } } };
    notify.fromApiError(err);
    expect(toast.error).toHaveBeenCalledWith("Bad request", { duration: 6000 });
  });

  it("fromApiError uses fallback when no message", () => {
    notify.fromApiError(new Error("x"), "Custom fallback");
    expect(toast.error).toHaveBeenCalledWith("Custom fallback", { duration: 6000 });
  });
});
```

- [ ] **Step 3: Run — expect FAIL (module not found)**

```bash
cd bom-web && npx vitest run src/lib/notify.test.ts
```

Expected: all 5 tests fail with "Cannot find module './notify'".

- [ ] **Step 4: Implement the wrapper**

Create `bom-web/src/lib/notify.ts`:

```ts
import { toast } from "sonner";
import { extractApiError } from "./apiError";

export const notify = {
  error(message: string, opts?: { duration?: number }) {
    toast.error(message, { duration: opts?.duration ?? 6000 });
  },

  success(message: string, opts?: { duration?: number }) {
    toast.success(message, { duration: opts?.duration ?? 3000 });
  },

  info(
    message: string,
    opts?: { duration?: number; action?: { label: string; onClick: () => void } },
  ) {
    toast(message, {
      duration: opts?.duration ?? 4000,
      action: opts?.action,
    });
  },

  fromApiError(err: unknown, fallback = "Something went wrong") {
    toast.error(extractApiError(err, fallback), { duration: 6000 });
  },
};
```

- [ ] **Step 5: Run — expect PASS**

```bash
cd bom-web && npx vitest run src/lib/notify.test.ts
```

Expected: 5/5 pass.

- [ ] **Step 6: Commit**

```bash
git add bom-web/package.json bom-web/package-lock.json \
        bom-web/src/lib/notify.ts bom-web/src/lib/notify.test.ts
git commit -m "feat(web): add sonner-backed notify helper for app-wide toasts"
```

---

## Task 2: Mount `<Toaster />` in App.tsx

**Files:**
- Modify: `bom-web/src/App.tsx`

- [ ] **Step 1: Read the current App.tsx structure**

Open `bom-web/src/App.tsx`. The component body today is:

```tsx
export default function App() {
  return <RouterProvider router={router} />;
}
```

- [ ] **Step 2: Add import + mount Toaster**

Add this import at the top of the file, grouped with the other top-level imports:

```ts
import { Toaster } from "sonner";
```

Replace the `App` function body with:

```tsx
export default function App() {
  return (
    <>
      <RouterProvider router={router} />
      <Toaster position="top-right" richColors closeButton />
    </>
  );
}
```

- [ ] **Step 3: Run full frontend suite — expect all pre-existing tests to still pass**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass. Mounting `<Toaster />` has no effect on existing tests (no test currently invokes `notify.*`).

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/App.tsx
git commit -m "feat(web): mount sonner Toaster in App.tsx"
```

---

## Task 3: Migrate `NewRequisitionPage`

**Files:**
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.tsx`
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`

- [ ] **Step 1: Add `notify` mock at the top of the test file**

Open `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`. Near the other `vi.mock` calls at the top (just below the imports), add:

```ts
vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));
```

Also add this import to pick up the mock in assertions (group with other imports):

```ts
import { notify } from "@/lib/notify";
```

- [ ] **Step 2: Run the test file — expect it to still pass**

```bash
cd bom-web && npx vitest run src/features/requisitions/NewRequisitionPage.test.tsx
```

Expected: all existing tests pass (mock doesn't affect anything yet).

- [ ] **Step 3: Migrate the component**

In `bom-web/src/features/requisitions/NewRequisitionPage.tsx`:

**3a.** Add the import near the other `@/lib` imports:

```ts
import { notify } from "@/lib/notify";
```

**3b.** Remove the `useState` import usage for `serverError`. Delete the line:

```ts
import { useState } from "react";
```

**BUT** only remove `useState` from the imports if it's not used elsewhere in the file. Check first — if `useState` is still used by any other state hook in the file, leave the import intact. The current file only uses `useState` for `serverError` (confirmed by reading the file), so both can go. If unsure, leave the import.

**3c.** Delete the state var:

```ts
const [serverError, setServerError] = useState<string | null>(null);
```

**3d.** Remove the `extractApiError` import if it's only used in the now-deleted catch block. Check the rest of the file first — if `extractApiError` appears nowhere else, delete:

```ts
import { extractApiError } from "@/lib/apiError";
```

**3e.** In `onSubmit`, find the try/catch. Replace:

```ts
  const onSubmit = handleSubmit(async (values) => {
    setServerError(null);
    try {
      const created = await create.mutateAsync({
        customerId: values.customer!.id,
        items: values.items.map((row) => ({
          itemId: row.item!.id,
          expectedQty: row.expectedQty,
        })),
        currencyCode: values.currencyCode,
      });
      navigate(`/requisitions/${created.id}`, { replace: true });
    } catch (e) {
      setServerError(extractApiError(e, "Failed to create requisition"));
    }
  });
```

With:

```ts
  const onSubmit = handleSubmit(async (values) => {
    try {
      const created = await create.mutateAsync({
        customerId: values.customer!.id,
        items: values.items.map((row) => ({
          itemId: row.item!.id,
          expectedQty: row.expectedQty,
        })),
        currencyCode: values.currencyCode,
      });
      notify.success("Requisition created");
      navigate(`/requisitions/${created.id}`, { replace: true });
    } catch (e) {
      notify.fromApiError(e, "Failed to create requisition");
    }
  });
```

**3f.** Delete the inline error span in the JSX:

```tsx
              {serverError && (
                <p className="text-sm text-destructive">{serverError}</p>
              )}
```

- [ ] **Step 4: Run tests**

```bash
cd bom-web && npx vitest run src/features/requisitions/NewRequisitionPage.test.tsx
```

Expected: all tests pass.

- [ ] **Step 5: Run full frontend suite**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add bom-web/src/features/requisitions/NewRequisitionPage.tsx \
        bom-web/src/features/requisitions/NewRequisitionPage.test.tsx
git commit -m "refactor(web): migrate NewRequisitionPage to toast notifications"
```

---

## Task 4: Migrate `BomEntryPage`

**Files:**
- Modify: `bom-web/src/features/bom/BomEntryPage.tsx`
- Modify: `bom-web/src/features/bom/BomEntryPage.test.tsx`

- [ ] **Step 1: Add `notify` mock to the test file**

In `bom-web/src/features/bom/BomEntryPage.test.tsx`, add the mock at the top (same pattern as Task 3):

```ts
vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));
```

- [ ] **Step 2: Run tests — expect all to still pass**

```bash
cd bom-web && npx vitest run src/features/bom/BomEntryPage.test.tsx
```

- [ ] **Step 3: Migrate the component**

In `bom-web/src/features/bom/BomEntryPage.tsx`:

**3a.** Add import:

```ts
import { notify } from "@/lib/notify";
```

**3b.** Remove the `extractApiError` import (it was added in F8.A, and after migration this file no longer uses it directly). Check the file first to confirm `extractApiError` appears nowhere else before deleting.

**3c.** Delete the submitError state var:

```ts
const [submitError, setSubmitError] = useState<string | null>(null);
```

**3d.** In `handleSubmit`, replace:

```ts
  function handleSubmit() {
    setSubmitError(null);
    submitBom.mutate(requisitionId, {
      onSuccess: () => navigate(`/requisitions/${requisitionId}`),
      onError: (err) => setSubmitError(extractApiError(err)),
    });
  }
```

With:

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

**3e.** In the JSX, find the Submit-All block (around the bottom of the component, inside `{!isReadOnly && (...)}`). Replace the current block:

```tsx
          {!isReadOnly && (
            <div className="flex flex-col items-end gap-1">
              <Button
                onClick={handleSubmit}
                disabled={!allItemsReady || submitBom.isPending}
              >
                {submitBom.isPending ? "Submitting…" : "Submit All"}
              </Button>
              {submitError && <span className="text-xs text-destructive">{submitError}</span>}
            </div>
          )}
```

With:

```tsx
          {!isReadOnly && (
            <div className="flex justify-end">
              <Button
                onClick={handleSubmit}
                disabled={!allItemsReady || submitBom.isPending}
              >
                {submitBom.isPending ? "Submitting…" : "Submit All"}
              </Button>
            </div>
          )}
```

(Reverts the layout change made in F8.A since the error span is gone; the simpler `flex justify-end` is restored.)

- [ ] **Step 4: Run tests**

```bash
cd bom-web && npx vitest run src/features/bom/BomEntryPage.test.tsx
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/bom/BomEntryPage.tsx \
        bom-web/src/features/bom/BomEntryPage.test.tsx
git commit -m "refactor(web): migrate BomEntryPage to toast notifications"
```

---

## Task 5: Migrate `CostingEntryPage`

**Files:**
- Modify: `bom-web/src/features/costing/CostingEntryPage.tsx`
- Modify: `bom-web/src/features/costing/CostingEntryPage.test.tsx`

- [ ] **Step 1: Add `notify` mock to the test file**

In `bom-web/src/features/costing/CostingEntryPage.test.tsx`, add at the top:

```ts
vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));
```

- [ ] **Step 2: Run tests — expect all still pass**

```bash
cd bom-web && npx vitest run src/features/costing/CostingEntryPage.test.tsx
```

- [ ] **Step 3: Migrate the component**

In `bom-web/src/features/costing/CostingEntryPage.tsx`:

**3a.** Add import:

```ts
import { notify } from "@/lib/notify";
```

**3b.** Remove the `extractApiError` import (it was used only in the `onError` block we're rewriting). Verify no other usage first.

**3c.** Delete the submitError state var:

```ts
const [submitError, setSubmitError] = useState<string | null>(null);
```

**3d.** In `handleSubmitItem`, find the block that sets submitError. Replace:

```ts
  function handleSubmitItem() {
    if (!selectedItemId) return;
    setSubmitError(null);
    submitCostingItem.mutate(
      {
        requisitionId,
        requisitionItemId: selectedItemId,
        payload: {
          rawMaterialCosts: lines.map((l) => ({
            bomLineId: l.bomLineId,
            costPerKg: l.costPerKg,
            currencyCode: l.currencyCode,
          })),
          landedCostType,
          landedCostValue,
          fohAmount,
        },
      },
      {
        onSuccess: () => {
          refetchCosting();
          const remaining = costingReview?.items.filter(
            (i) => i.requisitionItemId !== selectedItemId && i.costStatus !== "Submitted",
          );
          if (!remaining || remaining.length === 0) {
            navigate(`/requisitions/${requisitionId}`);
          }
        },
        onError: (err: unknown) => {
          setSubmitError(extractApiError(err, "Failed to submit costing."));
        },
      },
    );
  }
```

With:

```ts
  function handleSubmitItem() {
    if (!selectedItemId) return;
    submitCostingItem.mutate(
      {
        requisitionId,
        requisitionItemId: selectedItemId,
        payload: {
          rawMaterialCosts: lines.map((l) => ({
            bomLineId: l.bomLineId,
            costPerKg: l.costPerKg,
            currencyCode: l.currencyCode,
          })),
          landedCostType,
          landedCostValue,
          fohAmount,
        },
      },
      {
        onSuccess: () => {
          refetchCosting();
          notify.success("Costing submitted");
          const remaining = costingReview?.items.filter(
            (i) => i.requisitionItemId !== selectedItemId && i.costStatus !== "Submitted",
          );
          if (!remaining || remaining.length === 0) {
            navigate(`/requisitions/${requisitionId}`);
          }
        },
        onError: (err: unknown) => notify.fromApiError(err, "Failed to submit costing."),
      },
    );
  }
```

**3e.** In the JSX near the Submit button, find and delete the error span:

```tsx
                        {submitError && <span className="text-xs text-destructive">{submitError}</span>}
```

- [ ] **Step 4: Run tests**

```bash
cd bom-web && npx vitest run src/features/costing/CostingEntryPage.test.tsx
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/costing/CostingEntryPage.tsx \
        bom-web/src/features/costing/CostingEntryPage.test.tsx
git commit -m "refactor(web): migrate CostingEntryPage to toast notifications"
```

---

## Task 6: Migrate `MdReviewPage` (biggest — 2 handlers, 2 pre-submit validations, 2 success toasts)

**Files:**
- Modify: `bom-web/src/features/approvals/MdReviewPage.tsx`
- Modify: `bom-web/src/features/approvals/MdReviewPage.test.tsx`

- [ ] **Step 1: Update the test file**

In `bom-web/src/features/approvals/MdReviewPage.test.tsx`, add the `notify` mock at the top:

```ts
vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));
```

Also add:

```ts
import { notify } from "@/lib/notify";
```

Find the existing test that asserts on "Notes are required when rejecting" (around line 155). Replace:

```ts
    expect(
      screen.getByText(/Notes are required when rejecting/i),
    ).toBeInTheDocument();
```

With:

```ts
    expect(notify.error).toHaveBeenCalledWith(
      expect.stringContaining("Notes are required"),
    );
```

- [ ] **Step 2: Run tests — expect the rejection test to FAIL (mock assertion without implementation)**

```bash
cd bom-web && npx vitest run src/features/approvals/MdReviewPage.test.tsx
```

Expected: the rejection-without-notes test fails because the component still sets `validationError` state, not calling `notify.error`. Other tests should pass (the negative-margin badge tests from 8.C don't use `notify`).

- [ ] **Step 3: Migrate the component**

In `bom-web/src/features/approvals/MdReviewPage.tsx`:

**3a.** Add the import:

```ts
import { notify } from "@/lib/notify";
```

**3b.** Remove the `extractApiError` import (only used in the catches we're rewriting).

**3c.** Delete the state var:

```ts
const [validationError, setValidationError] = useState<string | null>(null);
```

**3d.** Replace `handleApprove`:

```ts
  async function handleApprove() {
    setValidationError(null);
    const items = data!.items.map((item) => {
      const price = Number(salesPrices[item.requisitionItemId] ?? "");
      return { requisitionItemId: item.requisitionItemId, salesPricePerKgAed: price };
    });
    if (items.some((i) => !Number.isFinite(i.salesPricePerKgAed) || i.salesPricePerKgAed <= 0)) {
      setValidationError("Enter a valid sales price for all items.");
      return;
    }
    try {
      await approve.mutateAsync({
        requisitionId,
        payload: { items, notes: notes || undefined },
      });
      setPageState({ kind: "approved" });
    } catch (e) {
      setValidationError(extractApiError(e, "Failed to approve."));
    }
  }
```

With:

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
      await approve.mutateAsync({
        requisitionId,
        payload: { items, notes: notes || undefined },
      });
      notify.success("Quotation approved");
      setPageState({ kind: "approved" });
    } catch (e) {
      notify.fromApiError(e, "Failed to approve.");
    }
  }
```

**3e.** Replace `handleReject`:

```ts
  async function handleReject() {
    setValidationError(null);
    if (notes.trim().length === 0) {
      setValidationError("Notes are required when rejecting.");
      return;
    }
    try {
      await reject.mutateAsync({
        requisitionId,
        payload: { notes: notes.trim() },
      });
      navigate(`/requisitions/${requisitionId}`);
    } catch (e) {
      setValidationError(extractApiError(e, "Failed to reject."));
    }
  }
```

With:

```ts
  async function handleReject() {
    if (notes.trim().length === 0) {
      notify.error("Notes are required when rejecting.");
      return;
    }
    try {
      await reject.mutateAsync({
        requisitionId,
        payload: { notes: notes.trim() },
      });
      notify.success("Quotation rejected");
      navigate(`/requisitions/${requisitionId}`);
    } catch (e) {
      notify.fromApiError(e, "Failed to reject.");
    }
  }
```

**3f.** Delete the inline error paragraph in the JSX (around the decision-panel section):

```tsx
              {validationError && (
                <p className="text-sm text-destructive">{validationError}</p>
              )}
```

- [ ] **Step 4: Run tests — expect PASS**

```bash
cd bom-web && npx vitest run src/features/approvals/MdReviewPage.test.tsx
```

Expected: all tests pass, including the updated rejection-without-notes assertion.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/approvals/MdReviewPage.tsx \
        bom-web/src/features/approvals/MdReviewPage.test.tsx
git commit -m "refactor(web): migrate MdReviewPage to toast notifications"
```

---

## Task 7: Wire SignalR → toast in `notificationsStore`

**Files:**
- Modify: `bom-web/src/store/notificationsStore.ts`
- Create: `bom-web/src/store/notificationsStore.test.ts`

- [ ] **Step 1: Write the failing tests**

Create `bom-web/src/store/notificationsStore.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from "vitest";
import { notificationsStore } from "./notificationsStore";
import { notify } from "@/lib/notify";
import type { Notification } from "@/types/api";

vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));

beforeEach(() => {
  vi.clearAllMocks();
  // Reset store state between tests
  notificationsStore.setState({
    notifications: [],
    unreadCount: 0,
    connected: false,
    _connection: null,
  });
});

describe("notificationsStore", () => {
  it("prependNotification adds and increments unreadCount", () => {
    const n: Notification = {
      id: 1,
      message: "Test",
      referenceId: 42,
      referenceType: "QuotationRequest",
      isRead: false,
      createdAt: "2026-04-16T12:00:00Z",
    };
    notificationsStore.getState().prependNotification(n);

    const state = notificationsStore.getState();
    expect(state.notifications).toHaveLength(1);
    expect(state.notifications[0].id).toBe(1);
    expect(state.unreadCount).toBe(1);
  });

  it("showToastForNotification fires notify.info with clickable View action for QuotationRequest", () => {
    const n: Notification = {
      id: 10,
      message: "Your quotation is ready",
      referenceId: 7,
      referenceType: "QuotationRequest",
      isRead: false,
      createdAt: "2026-04-16T12:00:00Z",
    };

    notificationsStore.getState().showToastForNotification(n);

    expect(notify.info).toHaveBeenCalledWith(
      "Your quotation is ready",
      expect.objectContaining({
        action: expect.objectContaining({
          label: "View",
          onClick: expect.any(Function),
        }),
      }),
    );
  });

  it("showToastForNotification fires notify.info WITHOUT action for unknown referenceType", () => {
    const n: Notification = {
      id: 20,
      message: "Unknown event",
      referenceId: 99,
      referenceType: "FutureTypeNotMapped",
      isRead: false,
      createdAt: "2026-04-16T12:00:00Z",
    };

    notificationsStore.getState().showToastForNotification(n);

    expect(notify.info).toHaveBeenCalledWith(
      "Unknown event",
      expect.objectContaining({
        action: undefined,
      }),
    );
  });
});
```

**Design note:** the test calls `showToastForNotification(n)` directly (exposed on the store). Extracting this as a store method (rather than an inline lambda inside `connect`) makes it testable in isolation without mocking SignalR.

- [ ] **Step 2: Run — expect FAIL (method not defined)**

```bash
cd bom-web && npx vitest run src/store/notificationsStore.test.ts
```

Expected: 2 tests fail (showToastForNotification doesn't exist). `prependNotification` test should pass since that method already exists.

- [ ] **Step 3: Extend the store**

In `bom-web/src/store/notificationsStore.ts`:

**3a.** Add the import near the top:

```ts
import { notify } from "@/lib/notify";
```

**3b.** Add a module-level helper function above the store definition (below the imports, above `interface NotificationsState`):

```ts
function pathForNotification(n: Notification): string | null {
  switch (n.referenceType) {
    case "QuotationRequest":
      return `/requisitions/${n.referenceId}`;
    default:
      return null;
  }
}
```

**3c.** Add `showToastForNotification` to the `NotificationsState` interface (alongside the existing methods):

```ts
interface NotificationsState {
  notifications: Notification[];
  unreadCount: number;
  connected: boolean;
  _connection: signalR.HubConnection | null;
  connect: (token: string) => Promise<void>;
  disconnect: () => Promise<void>;
  setNotifications: (ns: Notification[]) => void;
  prependNotification: (n: Notification) => void;
  showToastForNotification: (n: Notification) => void;
  markRead: (id: number) => void;
  markAllRead: () => void;
}
```

**3d.** In the store body, add the implementation after `prependNotification`:

```ts
  showToastForNotification: (n: Notification) => {
    const path = pathForNotification(n);
    notify.info(n.message, {
      action: path
        ? {
            label: "View",
            onClick: () => window.location.assign(path),
          }
        : undefined,
    });
  },
```

**3e.** Update the `ReceiveNotification` handler inside `connect` to also fire the toast. Find:

```ts
    connection.on("ReceiveNotification", (n: Notification) => {
      get().prependNotification(n);
    });
```

Replace with:

```ts
    connection.on("ReceiveNotification", (n: Notification) => {
      get().prependNotification(n);
      get().showToastForNotification(n);
    });
```

- [ ] **Step 4: Run tests — expect PASS**

```bash
cd bom-web && npx vitest run src/store/notificationsStore.test.ts
```

Expected: 3/3 pass.

- [ ] **Step 5: Run full frontend suite**

```bash
cd bom-web && npx vitest run
```

Expected: all tests pass (137+ plus the 5 new `notify` tests plus the 3 new `notificationsStore` tests = ~145 total).

- [ ] **Step 6: Commit**

```bash
git add bom-web/src/store/notificationsStore.ts \
        bom-web/src/store/notificationsStore.test.ts
git commit -m "feat(web): fire toast on SignalR ReceiveNotification with clickable View action"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|---|---|
| Install sonner | 1 |
| `notify.error/success/info/fromApiError` wrapper | 1 |
| `<Toaster />` in App.tsx with top-right / richColors / closeButton | 2 |
| NewRequisitionPage: remove inline; use notify.fromApiError + success | 3 |
| BomEntryPage: remove inline; use notify.fromApiError + success | 4 |
| CostingEntryPage: remove inline; use notify.fromApiError + success | 5 |
| MdReviewPage: remove inline; notify.error for pre-submit; success | 6 |
| MdReviewPage: Zod/page-load inline stays | 6 (nothing touched outside the 2 handlers) |
| SignalR ReceiveNotification → notify.info with action | 7 |
| pathForNotification handles QuotationRequest; default null | 7 |
| Unit tests for notify wrapper | 1 |
| Page test files add notify mock | 3, 4, 5, 6 |
| notificationsStore tests for toast + unknown-type safety | 7 |

**No placeholders.** Every step shows the exact code to write or replace.

**Type consistency:**
- `notify.fromApiError(err, fallback?): void` — defined in Task 1, called by name in Tasks 3, 4, 5, 6 (the fallback string varies per page).
- `showToastForNotification(n: Notification): void` — interface method added in Task 7; only referenced inside Task 7.
- `pathForNotification(n: Notification): string | null` — file-local helper in Task 7; signature consistent with its sole call site.

**Scope:** the plan touches exactly what the spec calls for. No drift into adjacent files. `RequisitionDetailPage`, modal components, and lookup helpers are deliberately untouched.

**Dependency order:** 1 → 2 (Toaster needs the lib). 2 → 3, 4, 5, 6 (pages need `<Toaster />` visible at runtime, though tests will pass without it since `notify` is mocked). 1 also unblocks 7. Tasks 3/4/5/6/7 can run in parallel once 1 and 2 are done — but subagent-driven execution keeps them sequential to avoid review interleaving.
