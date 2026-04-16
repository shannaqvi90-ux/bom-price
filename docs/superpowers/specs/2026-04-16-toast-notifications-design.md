# Toast Notifications — Design Spec

**Date:** 2026-04-16
**Status:** Approved (design); pending implementation plan
**Scope:** App-wide notification channel for transient feedback (errors, success, info)

---

## Goal

Introduce a single app-wide notification channel using [sonner](https://sonner.emilkowal.ski/) so that transient user feedback — backend API errors, action confirmations, and real-time SignalR events — appears as a toast in a consistent location, replacing the four per-page inline error displays that exist today.

## Non-Goals

- No structured per-field 400 response schema. Toasts carry a single string message.
- No persistent notification pane redesign (`NotificationsPage` is unchanged).
- No changes to Zod field-level validation — inline per-field errors remain on forms.
- No toasts for page-load failures (GET errors). Those stay inline because they're not transient.
- No dark-mode theme tuning beyond what sonner auto-provides.

---

## Design Principles

- **Single entry point.** All callers import `notify` from `@/lib/notify`. No direct `sonner` imports outside that file. This keeps the library swappable and centralizes defaults.
- **No React context.** Sonner uses an internal store; `toast.*` works from anywhere (components, Zustand actions, event handlers) as long as `<Toaster />` is mounted once.
- **Layer separation.** Zod validation → inline per-field. Backend 400 → toast. SignalR events → toast. Page-load failures → inline. Auto-save indicators → inline.

---

## Policy Decisions

| # | Decision | Choice |
|---|---|---|
| 1 | Feedback scope | All feedback: errors, success, info |
| 2 | Library | sonner (~3kb, shadcn-compatible, React 19 ready) |
| 3 | SignalR notifications | Show as `notify.info` toast with clickable "View" action → navigates to related entity |
| 4 | Migration | Full: remove inline error spans on the four pages; Zod inline + page-load inline preserved |
| 5 | Architecture | Thin wrapper `notify` over sonner; avoid direct library calls in feature code |

---

## Architecture

```
sonner (npm, ~3kb)
  └─ exports <Toaster /> component + imperative toast.*() API
     ▲
bom-web/src/lib/notify.ts (thin wrapper, ~30 lines)
  • notify.error(msg, opts?)    → toast.error, default 6s
  • notify.success(msg, opts?)  → toast.success, default 3s
  • notify.info(msg, opts?)     → toast, default 4s, optional action slot
  • notify.fromApiError(err, fallback?) → extractApiError + notify.error
     ▲ called by
Feature callsites (4 pages) + notificationsStore (SignalR)
     ▲ rendered by
App.tsx: <Toaster position="top-right" richColors closeButton />
```

Sonner's `<Toaster />` is mounted once in `App.tsx` as a sibling of `<RouterProvider />`. It does not need routing or query-client context; no rerenders on navigation.

---

## Components

### `bom-web/src/lib/notify.ts` (new)

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

**Design choices:**
- **Error 6s** — users need time to read backend errors.
- **Success 3s** — ephemeral confirmations.
- **Info 4s** — SignalR events; the `action` slot makes them clickable-to-navigate.
- **`fromApiError`** composes with the existing `extractApiError`; mutation `onError` collapses to a one-liner.
- **No custom `warning` variant yet** — YAGNI. Add later if needed.

### `App.tsx` integration

```tsx
import { Toaster } from "sonner";

export default function App() {
  return (
    <>
      <RouterProvider router={router} />
      <Toaster position="top-right" richColors closeButton />
    </>
  );
}
```

- `position="top-right"` — out of the way of the left sidebar layout.
- `richColors` — sonner's variant colors align with the existing Tailwind destructive/success tokens.
- `closeButton` — manual dismiss for long error messages.
- Theme auto-follows system/app.

### Dependency

```bash
cd bom-web && npm install sonner
```

Sonner has no peer deps beyond React ≥18; works with React 19.

---

## SignalR Integration

`bom-web/src/store/notificationsStore.ts` currently registers a silent handler:

```ts
connection.on("ReceiveNotification", (n: Notification) => {
  get().prependNotification(n);
});
```

**Change:** also fire an `info` toast with a "View" action:

```ts
connection.on("ReceiveNotification", (n: Notification) => {
  get().prependNotification(n);
  notify.info(n.message, {
    action: {
      label: "View",
      onClick: () => {
        const path = pathForNotification(n);
        if (path) window.location.assign(path);
      },
    },
  });
});
```

**Helper** (same file or co-located):

```ts
function pathForNotification(n: Notification): string | null {
  switch (n.referenceType) {
    case "QuotationRequest":
      return `/requisitions/${n.referenceId}`;
    default:
      return null; // unknown type → action slot is a no-op, no broken link
  }
}
```

The `Notification` type is `{ id, message, referenceId, referenceType, isRead, createdAt }`. Today the backend only emits `referenceType = "QuotationRequest"`, but the switch defaults to `null` so new types added later don't create broken "View" links.

**Why `window.location.assign` not `useNavigate()`:** the store is outside React's tree; there's no hook access. Full-page reload on click is acceptable for a low-frequency action (handful of toasts per session). Upgrade path: a module-level router ref if click-reload becomes annoying. YAGNI.

---

## Per-Page Migration

For each of the four pages: delete the inline error state + span, swap mutation `onError` to `notify.fromApiError`, add `notify.success` on the success path where the current UX is a silent navigate or "Saved ✓" text.

### `NewRequisitionPage.tsx`
- Remove: `serverError` state, `<p>{serverError}</p>`.
- Replace `catch (e) { … }` in `onSubmit` with `notify.fromApiError(e, "Failed to create requisition")`.
- On success: `notify.success("Requisition created")` just before `navigate(...)`.

### `BomEntryPage.tsx`
- Remove: `submitError` state + `<span>{submitError}</span>`.
- Mutation `onError`: `notify.fromApiError(err, "Failed to submit BOM")`.
- `onSuccess`: `notify.success("BOM submitted for costing")` before `navigate`.
- **Keep** the "Saved ✓ / Saving… / Save failed" indicator for per-line auto-save. Per-line changes are high-frequency; a toast per change would spam.

### `CostingEntryPage.tsx`
- Remove: `submitError` state + error span.
- Mutation `onError`: `notify.fromApiError(err, "Failed to submit costing")`.
- `onSuccess`: `notify.success("Costing submitted")`.
- **Keep** the auto-save "Saved ✓" indicator (same reason).

### `MdReviewPage.tsx`
- Remove: `validationError` state + `<p>{validationError}</p>`.
- `handleApprove` catch: `notify.fromApiError(e, "Failed to approve")`.
- `handleReject` catch: `notify.fromApiError(e, "Failed to reject")`.
- **Also** move the two client-side pre-submit validations (`"Enter a valid sales price for all items."`, `"Notes are required when rejecting."`) to `notify.error(...)`. Consistent with the full-migration decision; no inline error state survives on this page.
- On success: `notify.success("Quotation approved")` before `setPageState({ kind: "approved" })`, and `notify.success("Quotation rejected")` before `navigate(...)`.

### Not migrated (intentional)
- `RequisitionDetailPage` 403/404/error cards — page-load failures, non-transient, stay inline.
- `MdReviewPage` 403/404/error cards at the top of the component — same.
- Zod field-level errors on any form (e.g. the `"Qty must be greater than zero"` inline text under an input) — they belong near the field that failed.
- Auto-save "Saved ✓ / Saving… / Save failed" indicators on BOM and Costing — high-frequency, inline is the correct surface.

---

## Testing

### Unit — `bom-web/src/lib/notify.test.ts` (new)

Mock `sonner.toast` and assert the wrapper forwards correctly:

- `error` → `toast.error(msg, { duration: 6000 })`
- `success` → `toast.success(msg, { duration: 3000 })`
- `info` → `toast(msg, { duration: 4000, action })`
- `fromApiError` with `{ response: { data: { message } } }` → `toast.error(message, { duration: 6000 })`
- `fromApiError` with bad shape + fallback → `toast.error(fallback, ...)`

Five tests.

### Existing page tests — mechanical update

Each of the four page test files currently asserts on inline error text (e.g., `expect(screen.getByText("Failed to create requisition")).toBeInTheDocument()`). After migration these assertions break because the `<p>` is gone.

**Replacement pattern** in each file:

```ts
vi.mock("@/lib/notify", () => ({
  notify: { error: vi.fn(), success: vi.fn(), info: vi.fn(), fromApiError: vi.fn() },
}));
```

Then swap text assertions for:

```ts
expect(notify.fromApiError).toHaveBeenCalled();
// or
expect(notify.error).toHaveBeenCalledWith(expect.stringContaining("..."));
```

Test count stays roughly flat; ≤ 5 assertion swaps per file.

### SignalR integration

If `notificationsStore` has an existing test file, extend it with a case that mocks `@/lib/notify` and verifies `ReceiveNotification` triggers `notify.info` with the correct `action` shape. If there's no existing test for the SignalR path, add a minimal one. Verify before implementing.

### Not directly tested

- `<Toaster />` rendered in `App.tsx` — trusting sonner's own coverage. Smoke-tested by the page tests running end-to-end.
- Sonner's own toast rendering, dismissal, animation, stacking.

---

## Files Changed (Summary)

### New
- `bom-web/src/lib/notify.ts`
- `bom-web/src/lib/notify.test.ts`

### Modified
- `bom-web/package.json` + `package-lock.json` (sonner dep)
- `bom-web/src/App.tsx` (mount `<Toaster />`)
- `bom-web/src/store/notificationsStore.ts` (SignalR → toast)
- `bom-web/src/features/requisitions/NewRequisitionPage.tsx`
- `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`
- `bom-web/src/features/bom/BomEntryPage.tsx`
- `bom-web/src/features/bom/BomEntryPage.test.tsx`
- `bom-web/src/features/costing/CostingEntryPage.tsx`
- `bom-web/src/features/costing/CostingEntryPage.test.tsx`
- `bom-web/src/features/approvals/MdReviewPage.tsx`
- `bom-web/src/features/approvals/MdReviewPage.test.tsx`
- (Optional) `bom-web/src/store/notificationsStore.test.ts` (if present)

Net diff estimate: ~80 new lines (notify.ts + tests), ~100 line reductions across the 4 pages (state vars + spans removed), ~60 line test assertion swaps. Approximately +40 lines total.

---

## Risks & Open Questions

1. **Double notification for SignalR events.** Users land on a page, click a toast, and the notification is now read in the store — but the toast doesn't auto-mark it read. Post-implementation UX check: if users complain, add a `markRead(n.id)` call in the action `onClick`.

2. **Toast spam on network flake.** A burst of failed retries could stack up. Sonner's default stacks to 3 visible; beyond that they queue. Acceptable for now; monitor.

3. **`window.location.assign` reloads.** Full page reload per toast-click is jarring compared to SPA navigation. Upgrade path documented (module-level router ref) but not taken yet.

4. **No warning variant yet.** If a future use case needs yellow/orange "warning" semantics (e.g., stale exchange rate), add `notify.warning` to the wrapper at that time.
