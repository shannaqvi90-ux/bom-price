# V3 Phase B — Frontend Rewrite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the web frontend half of the V3 simplified workflow — replace the V2.3 sales+BOM split with one combined screen, add the 2-stage MD approval flow (margin → customer-confirm → final-sign-and-lock), wire MD signature upload, and adapt existing pages to the V3 state machine. Backend Phase A is already merged at master `ea1a904`; Phase C cutover SQL ships separately.

**Architecture:** React 19 + Vite + TanStack Query + Zustand + Tailwind. Feature-slice folder layout (`src/features/<slice>/`). API axios client per resource (`src/api/<resource>.ts`); query hooks colocated. Type-safe via `src/types/api.ts` (single source for backend response shapes). PWA service worker via Workbox `injectManifest` mode (`src/sw.ts`). Tests: Vitest + Testing Library; component-level for pages, integration via mocked axios.

**Tech Stack:** React 19 · Vite 8 · TanStack Query 5 · Zustand · Tailwind CSS · Sonner (toasts) · Axios · TypeScript 5 · Vitest + @testing-library/react · vite-plugin-pwa (Workbox)

**Spec reference:** [`docs/superpowers/specs/2026-04-29-v3-simplified-workflow-design.md`](../specs/2026-04-29-v3-simplified-workflow-design.md) — read sections 5 (state machine), 6 (endpoints), 8 (frontend), 9 (notifications), 11 (admin overrides).

---

## File Structure

### NEW files

| Path | Purpose |
|---|---|
| `bom-web/src/api/requisitions.ts` | V3 axios client: list, get, create, submit, cancel, getImplicitItems, etc. (some routes already exist; consolidate + add V3 surface) |
| `bom-web/src/api/approvals.ts` | V3 axios client: setMargin, acceptCustomer, rejectCustomer, finalSign |
| `bom-web/src/api/profile.ts` | Signature upload + GET own |
| `bom-web/src/api/customers.ts` | Consolidated customer client (list, get, create with auto-Code response, getImplicitItems) |
| `bom-web/src/api/items.ts` | Consolidated item client (list, create with auto-Code response, status PATCH) |
| `bom-web/src/api/queries/requisitions.ts` | Query/mutation hooks for V3 reqs |
| `bom-web/src/api/queries/approvals.ts` | Hooks: `useSetMargin`, `useAcceptCustomer`, `useRejectCustomer`, `useFinalSign` |
| `bom-web/src/api/queries/profile.ts` | `useUploadSignature`, `useOwnSignature` |
| `bom-web/src/api/queries/customers.ts` | `useCustomers`, `useCustomer`, `useCreateCustomer`, `useCustomerImplicitItems` |
| `bom-web/src/api/queries/items.ts` | `useItems`, `useCreateItem`, `useToggleItemActive` |
| `bom-web/src/components/v3/BomEditorTable.tsx` | Shared inline BOM editor used in `NewRequisitionPage` and (read-only) on `RequisitionDetailPage` / `CustomerConfirmPage` |
| `bom-web/src/components/v3/CreateCustomerModal.tsx` | Inline customer create modal (Name, Email, Phone, Address — Code preview only) |
| `bom-web/src/components/v3/CreateFinishedGoodModal.tsx` | Inline FG item create modal (Description; Code preview; Branch=Alain implicit) |
| `bom-web/src/components/v3/CreateRawMaterialModal.tsx` | Inline RM item create modal (Description; Code preview; Branch=Alain implicit) |
| `bom-web/src/components/v3/SignaturePreview.tsx` | Reusable signature image preview (used by `ProfileSignaturePage` and `MdFinalSignPage`) |
| `bom-web/src/components/v3/V3StatusBadge.tsx` | Status pill with color mapping for new V3 statuses |
| `bom-web/src/features/requisitions/CustomerConfirmPage.tsx` | NEW page — customer confirm/reject (sales-only, on `CustomerConfirm` status) |
| `bom-web/src/features/approvals/MdFinalSignPage.tsx` | NEW page — MD final sign with type-to-confirm SIGN modal (replaces parts of MdReviewPage) |
| `bom-web/src/features/approvals/MdMarginPage.tsx` | NEW page — MD Stage 1 margin entry (replaces V2.3 single Approve action; previous MdReviewPage deletes/repurposes) |
| `bom-web/src/features/profile/ProfileSignaturePage.tsx` | NEW page — MD signature upload + replace flow |
| `bom-web/src/features/requisitions/CustomerConfirmPage.test.tsx` | Page test |
| `bom-web/src/features/approvals/MdFinalSignPage.test.tsx` | Page test |
| `bom-web/src/features/approvals/MdMarginPage.test.tsx` | Page test |
| `bom-web/src/features/profile/ProfileSignaturePage.test.tsx` | Page test |
| `bom-web/src/components/v3/BomEditorTable.test.tsx` | Component test |
| `bom-web/src/components/v3/CreateCustomerModal.test.tsx` | Component test |
| `bom-web/src/components/v3/CreateFinishedGoodModal.test.tsx` | Component test |
| `bom-web/src/components/v3/CreateRawMaterialModal.test.tsx` | Component test |
| `bom-web/src/components/v3/V3StatusBadge.test.tsx` | Component test |

### MODIFIED files

| Path | Change |
|---|---|
| `bom-web/src/types/api.ts` | Add V3 status union, `ApprovalStage`, V3 `Requisition` shape (with `finishedGoods[]`), `BomLine`, `BomCost`, `Approval` (with `Stage` + per-FG margin), `SignatureUploadResponse`, `ImplicitItemResponse`. Mark `Customer.code` and `Item.code` as auto-generated (`code: string` always present, not user-editable). |
| `bom-web/src/api/axios.ts` | (Likely no change; if `Content-Type` for multipart is wrong, fix in signature upload) |
| `bom-web/src/features/requisitions/NewRequisitionPage.tsx` | **Major rewrite.** Customer + currency picker + N FG cards each with `<BomEditorTable>` inline + Printing checkbox + Notes textarea + Save Draft / Submit |
| `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx` | Rewrite for V3 page |
| `bom-web/src/features/requisitions/RequisitionDetailPage.tsx` | New action buttons per V3 status (Submit/Cancel for sales on Draft; Edit BOM for accountant on Costing; etc.); inline BOM section (no separate tab); diff badges if accountant edited BOM |
| `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx` | V3 status behaviors |
| `bom-web/src/features/customers/CustomerListPage.tsx` | `code` column read-only; on inline create show preview-only (auto from server) |
| `bom-web/src/features/items/ItemListPage.tsx` | Same: `code` read-only; preview-only on create |
| `bom-web/src/features/dashboard/SalesDashboard.tsx` | Drop BomPending/BomInProgress; add CustomerConfirm queue + Drafts tab |
| `bom-web/src/features/dashboard/AccountantDashboard.tsx` | Drop CostingPending/CostingInProgress widgets; show V3 Costing queue; awaitingMd→MdPricing terminology |
| `bom-web/src/features/dashboard/MdDashboard.tsx` | Drop MdReview; add MdPricing queue + MdFinalSign queue |
| `bom-web/src/features/dashboard/BomDashboard.tsx` | DELETE (BomCreator role going away in V3) |
| `bom-web/src/features/dashboard/DashboardRouter.tsx` | Drop `BomCreator` case; route accordingly |
| `bom-web/src/features/admin/audit-log/AuditLogPage.tsx` | Add new V3 ActionType filter values (`RollbackToCosting`, `V3CutoverMigration`); add new Status filter values (`Costing`, `MdPricing`, `CustomerConfirm`, `MdFinalSign`, `Signed`, `Cancelled`) |
| `bom-web/src/sw.ts` | Bump cache name suffix (e.g., `bom-api-list-cache-v3`) so previous V2.3 PWA users get a fresh fetch on first V3 load |
| `bom-web/src/App.tsx` (or wherever routes live) | Add new routes: `/requisitions/{id}/customer-confirm`, `/approvals/{id}/margin`, `/approvals/{id}/final`, `/profile/signature` |

### DELETED files

| Path | Reason |
|---|---|
| `bom-web/src/features/bom/BomEntryPage.tsx` | V3 has no BOM stage |
| `bom-web/src/features/bom/BomEntryPage.test.tsx` | Same |
| `bom-web/src/features/dashboard/BomDashboard.tsx` | V3 drops BomCreator role |
| `bom-web/src/features/approvals/MdReviewPage.tsx` | V2.3 page combining margin+sign — replaced by `MdMarginPage` (Stage 1) + `MdFinalSignPage` (Stage 2) |
| `bom-web/src/features/approvals/MdReviewPage.test.tsx` | Same |
| `bom-web/src/features/requisitions/EditRequisitionPage.tsx` | V3 reqs are immutable post-Draft; edits while Draft happen via re-Save on `NewRequisitionPage`; this page can disappear |
| `bom-web/src/features/requisitions/EditRequisitionPage.test.tsx` | Same |
| `bom-web/src/features/requisitions/BranchSwapModal.tsx` | V3 is Alain-only; per-req branch reassignment is moot |
| `bom-web/src/features/requisitions/BranchSwapModal.test.tsx` | Same |
| `bom-web/src/features/requisitions/BranchChangeHistoryModal.tsx` | Same — branch never changes in V3 |
| `bom-web/src/features/requisitions/ChangeCustomerModal.tsx` | Customer is set at Create and never edited (admin override C8 hard-deletes; sales never changes) |
| `bom-web/src/features/requisitions/CustomerHistoryModal.tsx` | Same |

### Notes

- Keep `BranchPicker.tsx` for Admin pages (still needed in `CustomersPage`/`ItemsPage` admin context). V3 hides non-Alain in lists post-cutover but admin can still view/edit.
- `OwnedByBadge.tsx` stays — V3 still has sales groups (post-cutover semantics may evolve in Phase D).
- `<Dialog>` and `<Button>` UI primitives stay.

---

## Worktree

Implementation should run in a dedicated worktree off `master` (NOT off `docs/v3-simplified-workflow-design`):

```bash
# From repo root, on master with clean working tree
git worktree add .claude/worktrees/v3-phase-b feat/v3-phase-b-frontend
cd .claude/worktrees/v3-phase-b/bom-web
npm install
```

All commits land on `feat/v3-phase-b-frontend`. PR opens against `master` after Task 26 (final smoke).

---

## Task 1: Set up worktree + branch + verify baseline

**Files:** None modified (setup only)

- [ ] **Step 1: Verify clean working tree on master**

```bash
git checkout master
git pull origin master
git status
```

Expected: `On branch master / Your branch is up to date with 'origin/master'. / nothing to commit, working tree clean`

- [ ] **Step 2: Create dedicated worktree + install deps**

```bash
git worktree add .claude/worktrees/v3-phase-b feat/v3-phase-b-frontend
cd .claude/worktrees/v3-phase-b/bom-web
npm install
```

Expected: `Preparing worktree (new branch 'feat/v3-phase-b-frontend')` then `npm install` completes (may print warnings about peer deps for vite-plugin-pwa — fine, `.npmrc` legacy-peer-deps handles it).

- [ ] **Step 3: Verify baseline build + tests pass before any changes**

```bash
cd .claude/worktrees/v3-phase-b/bom-web
npx tsc --noEmit
npm run build
npm test -- --run
```

Expected: tsc clean (0 errors). Vite build succeeds. Vitest passes ~263 tests (matches CLAUDE.md most-recent count).

If build or tests fail BEFORE any changes, STOP. The failure is in master and must be fixed first.

- [ ] **Step 4: No commit yet — proceed to Task 2**

---

## Task 2: Add V3 types to types/api.ts

**Files:**
- Modify: `bom-web/src/types/api.ts`

- [ ] **Step 1: Add V3 status + approval types**

Open `bom-web/src/types/api.ts`. Append at end:

```typescript
// ─────────────────────────────────────────────────────────────────────────────
// V3 SIMPLIFIED WORKFLOW TYPES (post-2026-04-29)
// Backend Phase A merged at master ea1a904.
// ─────────────────────────────────────────────────────────────────────────────

export type V3RequisitionStatus =
  | "Draft"
  | "Costing"
  | "MdPricing"
  | "CustomerConfirm"
  | "MdFinalSign"
  | "Signed"
  | "Cancelled"
  | "Rejected";

// Legacy V2.3 statuses also exist on historical reqs (Approved, BomPending, etc.)
// Use the union type below for tolerant code paths.
export type RequisitionStatus = V3RequisitionStatus | LegacyV2RequisitionStatus;

export type LegacyV2RequisitionStatus =
  | "BomPending"
  | "BomInProgress"
  | "CostingPending"
  | "CostingInProgress"
  | "MdReview"
  | "Approved";

export type ApprovalStage = "InitialPricing" | "FinalSign";

export interface V3BomLine {
  id: number;
  qtyPerKg: number;
  micron: string | null;
  item: { id: number; code: string; description: string };
  lastModifiedByUserId?: number | null;
  lastModifiedAt?: string | null;
}

export interface V3BomCost {
  printingCostPerKg: number | null;
  printingCostCurrency: string | null;
  fohPerKg: number;
  transportPerKg: number;
  commissionPerKg: number;
  lines: Array<{
    bomLineId: number;
    wastagePercent: number;
    purchaseValuePerKg: number | null;
    purchaseCurrency: string | null;
  }>;
}

export interface V3FinishedGood {
  id: number;
  expectedQty: number;
  hasPrinting: boolean;
  item: { id: number; code: string; description: string };
  bomLines: V3BomLine[] | null;
  costs: V3BomCost | null;
}

export interface V3Requisition {
  id: number;
  refNo: string;
  status: V3RequisitionStatus;
  currencyCode: string;
  notes: string | null;
  customer: { id: number; name: string; code: string };
  salesPerson: { id: number; name: string };
  finishedGoods: V3FinishedGood[];
}

export interface V3ApprovalItem {
  requisitionItemId: number;
  marginPerKg: number;
}

export interface V3Approval {
  id: number;
  requisitionId: number;
  stage: ApprovalStage;
  isApproved: boolean;
  isSuperseded: boolean;
  approvedAt: string;
  rateSnapshot: number | null;
  costFxSnapshot: number | null;
  notes: string | null;
  items: V3ApprovalItem[];
}

export interface V3CreateRequisitionPayload {
  customerId: number;
  quotationCurrency: string;
  referenceNumber?: string;
  notes?: string;
  finishedGoods: Array<{
    itemId: number;
    expectedQtyKg: number;
    printing: boolean;
    bomLines: Array<{
      itemId: number;
      qtyPerKg: number;
      micron: string | null;
      processId: number;
    }>;
  }>;
}

export interface V3SetMarginPayload {
  notes?: string | null;
  items: Array<{ requisitionItemId: number; marginPerKg: number }>;
}

export interface V3FinalSignPayload {
  confirmationToken: string; // must equal "SIGN"
  notes?: string | null;
}

export interface SignatureUploadResponse {
  path: string;
  uploadedAt: string;
}

export interface ImplicitItemResponse {
  id: number;
  code: string;
  description: string;
}
```

- [ ] **Step 2: Run tsc**

```bash
npx tsc --noEmit
```

Expected: 0 errors. Existing code unaffected (purely additive types).

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/types/api.ts
git commit -m "feat(v3-web): add V3 type definitions to types/api.ts

V3RequisitionStatus union, ApprovalStage, V3Requisition with finishedGoods[],
V3BomLine, V3BomCost, V3Approval, payload types for Create/SetMargin/FinalSign,
SignatureUploadResponse, ImplicitItemResponse. Legacy V2 status names preserved
for historical req display.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3: V3 API axios clients

**Files:**
- Create: `bom-web/src/api/requisitions.ts`
- Create: `bom-web/src/api/approvals.ts`
- Create: `bom-web/src/api/profile.ts`
- Create: `bom-web/src/api/customers.ts`
- Create: `bom-web/src/api/items.ts`

(Some of these may already partially exist; consolidate and extend.)

- [ ] **Step 1: Create `requisitions.ts`**

```typescript
// bom-web/src/api/requisitions.ts
import { axios } from "./axios";
import type {
  V3Requisition,
  V3CreateRequisitionPayload,
  RequisitionStatus,
} from "../types/api";

export interface RequisitionListItem {
  id: number;
  refNo: string;
  status: RequisitionStatus;
  branchId: number;
  customerName: string;
  salesPersonName: string;
  updatedAt: string;
}

export const requisitionsApi = {
  list: (params?: { status?: string; from?: string; to?: string }) =>
    axios.get<RequisitionListItem[]>("/api/requisitions", { params }).then(r => r.data),

  get: (id: number) =>
    axios.get<V3Requisition>(`/api/requisitions/${id}`).then(r => r.data),

  create: (payload: V3CreateRequisitionPayload) =>
    axios.post<{ id: number; refNo: string; status: string }>("/api/requisitions", payload).then(r => r.data),

  submit: (id: number) =>
    axios.post<{ id: number; status: string }>(`/api/requisitions/${id}/submit`).then(r => r.data),

  cancel: (id: number, reason: string) =>
    axios.post<{ id: number; status: string }>(`/api/requisitions/${id}/cancel`, { reason }).then(r => r.data),
};
```

- [ ] **Step 2: Create `approvals.ts`**

```typescript
// bom-web/src/api/approvals.ts
import { axios } from "./axios";
import type { V3SetMarginPayload, V3FinalSignPayload, V3Approval } from "../types/api";

export const approvalsApi = {
  setMargin: (requisitionId: number, payload: V3SetMarginPayload) =>
    axios
      .post<{ id: number; status: string; approvalId: number }>(
        `/api/approvals/${requisitionId}/set-margin`,
        payload
      )
      .then(r => r.data),

  acceptCustomer: (requisitionId: number, customerFeedback?: string) =>
    axios
      .post<{ id: number; status: string }>(
        `/api/approvals/${requisitionId}/accept-customer`,
        { customerFeedback }
      )
      .then(r => r.data),

  rejectCustomer: (requisitionId: number, reason: string) =>
    axios
      .post<{ id: number; status: string }>(
        `/api/approvals/${requisitionId}/reject-customer`,
        { reason }
      )
      .then(r => r.data),

  finalSign: (requisitionId: number, payload: V3FinalSignPayload) =>
    axios
      .post<{ id: number; status: string; approvalId: number; pdfDownloadUrl: string }>(
        `/api/approvals/${requisitionId}/final-sign`,
        payload
      )
      .then(r => r.data),

  getCurrent: (requisitionId: number) =>
    axios.get<V3Approval>(`/api/approvals/${requisitionId}/current`).then(r => r.data),
};
```

- [ ] **Step 3: Create `profile.ts`**

```typescript
// bom-web/src/api/profile.ts
import { axios } from "./axios";
import type { SignatureUploadResponse } from "../types/api";

export const profileApi = {
  uploadSignature: async (file: File): Promise<SignatureUploadResponse> => {
    const formData = new FormData();
    formData.append("file", file);
    const r = await axios.post<SignatureUploadResponse>("/api/profile/signature", formData, {
      headers: { "Content-Type": "multipart/form-data" },
    });
    return r.data;
  },

  getOwnSignatureUrl: (): string => "/api/profile/signature",
};
```

- [ ] **Step 4: Create `customers.ts`**

```typescript
// bom-web/src/api/customers.ts
import { axios } from "./axios";
import type { ImplicitItemResponse } from "../types/api";

export interface CustomerListItem {
  id: number;
  code: string;
  name: string;
  email: string | null;
  phoneNumber: string | null;
  address: string | null;
  salesPersonId: number;
  isDeleted: boolean;
}

export interface CreateCustomerPayload {
  name: string;
  email: string;
  phoneNumber: string;
  address: string;
}

export const customersApi = {
  list: () => axios.get<CustomerListItem[]>("/api/customers").then(r => r.data),

  get: (id: number) => axios.get<CustomerListItem>(`/api/customers/${id}`).then(r => r.data),

  create: (payload: CreateCustomerPayload) =>
    axios.post<CustomerListItem>("/api/customers", payload).then(r => r.data),

  getImplicitItems: (customerId: number) =>
    axios.get<ImplicitItemResponse[]>(`/api/customers/${customerId}/items`).then(r => r.data),
};
```

- [ ] **Step 5: Create `items.ts`**

```typescript
// bom-web/src/api/items.ts
import { axios } from "./axios";

export interface ItemListItem {
  id: number;
  code: string;
  description: string;
  type: "FinishedGood" | "RawMaterial";
  branchId: number;
  isActive: boolean;
}

export interface CreateItemPayload {
  description: string;
  type: "FinishedGood" | "RawMaterial";
  branchId?: number; // admin only; SP/Accountant uses JWT branch
}

export const itemsApi = {
  list: (params?: { branchId?: number; type?: "FinishedGood" | "RawMaterial" }) =>
    axios.get<ItemListItem[]>("/api/items", { params }).then(r => r.data),

  create: (payload: CreateItemPayload) =>
    axios.post<ItemListItem>("/api/items", payload).then(r => r.data),

  toggleActive: (id: number, isActive: boolean) =>
    axios.patch<{ id: number; isActive: boolean }>(`/api/items/${id}/status`, { isActive }).then(r => r.data),
};
```

- [ ] **Step 6: Verify tsc + commit**

```bash
npx tsc --noEmit
git add bom-web/src/api/
git commit -m "feat(v3-web): add V3 API axios clients (requisitions, approvals, profile, customers, items)

Consolidated per-resource clients with full V3 endpoint coverage.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4: V3 query hooks

**Files:**
- Create: `bom-web/src/api/queries/requisitions.ts`
- Create: `bom-web/src/api/queries/approvals.ts`
- Create: `bom-web/src/api/queries/profile.ts`
- Create: `bom-web/src/api/queries/customers.ts`
- Create: `bom-web/src/api/queries/items.ts`

- [ ] **Step 1: Create requisitions hooks**

```typescript
// bom-web/src/api/queries/requisitions.ts
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { requisitionsApi } from "../requisitions";
import type { V3CreateRequisitionPayload } from "../../types/api";

export function useRequisitions(params?: { status?: string; from?: string; to?: string }) {
  return useQuery({
    queryKey: ["requisitions", params],
    queryFn: () => requisitionsApi.list(params),
  });
}

export function useRequisition(id: number) {
  return useQuery({
    queryKey: ["requisition", id],
    queryFn: () => requisitionsApi.get(id),
    enabled: !!id,
  });
}

export function useCreateRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: V3CreateRequisitionPayload) => requisitionsApi.create(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["requisitions"] }),
  });
}

export function useSubmitRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => requisitionsApi.submit(id),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: ["requisition", id] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}

export function useCancelRequisition() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) =>
      requisitionsApi.cancel(id, reason),
    onSuccess: (_d, { id }) => {
      qc.invalidateQueries({ queryKey: ["requisition", id] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}
```

- [ ] **Step 2: Create approvals hooks**

```typescript
// bom-web/src/api/queries/approvals.ts
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { approvalsApi } from "../approvals";
import type { V3SetMarginPayload, V3FinalSignPayload } from "../../types/api";

export function useSetMargin() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ requisitionId, payload }: { requisitionId: number; payload: V3SetMarginPayload }) =>
      approvalsApi.setMargin(requisitionId, payload),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}

export function useAcceptCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ requisitionId, customerFeedback }: { requisitionId: number; customerFeedback?: string }) =>
      approvalsApi.acceptCustomer(requisitionId, customerFeedback),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}

export function useRejectCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ requisitionId, reason }: { requisitionId: number; reason: string }) =>
      approvalsApi.rejectCustomer(requisitionId, reason),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}

export function useFinalSign() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ requisitionId, payload }: { requisitionId: number; payload: V3FinalSignPayload }) =>
      approvalsApi.finalSign(requisitionId, payload),
    onSuccess: (_d, { requisitionId }) => {
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId] });
      qc.invalidateQueries({ queryKey: ["requisitions"] });
    },
  });
}
```

- [ ] **Step 3: Create profile hooks**

```typescript
// bom-web/src/api/queries/profile.ts
import { useMutation } from "@tanstack/react-query";
import { profileApi } from "../profile";

export function useUploadSignature() {
  return useMutation({
    mutationFn: (file: File) => profileApi.uploadSignature(file),
  });
}
```

- [ ] **Step 4: Create customers hooks**

```typescript
// bom-web/src/api/queries/customers.ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { customersApi, type CreateCustomerPayload } from "../customers";

export function useCustomers() {
  return useQuery({
    queryKey: ["customers"],
    queryFn: customersApi.list,
  });
}

export function useCustomer(id: number) {
  return useQuery({
    queryKey: ["customer", id],
    queryFn: () => customersApi.get(id),
    enabled: !!id,
  });
}

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateCustomerPayload) => customersApi.create(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["customers"] }),
  });
}

export function useCustomerImplicitItems(customerId: number | null) {
  return useQuery({
    queryKey: ["customer-implicit-items", customerId],
    queryFn: () => (customerId ? customersApi.getImplicitItems(customerId) : Promise.resolve([])),
    enabled: !!customerId,
  });
}
```

- [ ] **Step 5: Create items hooks**

```typescript
// bom-web/src/api/queries/items.ts
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { itemsApi, type CreateItemPayload } from "../items";

export function useItems(params?: { branchId?: number; type?: "FinishedGood" | "RawMaterial" }) {
  return useQuery({
    queryKey: ["items", params],
    queryFn: () => itemsApi.list(params),
  });
}

export function useCreateItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateItemPayload) => itemsApi.create(payload),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["items"] }),
  });
}

export function useToggleItemActive() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, isActive }: { id: number; isActive: boolean }) =>
      itemsApi.toggleActive(id, isActive),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["items"] }),
  });
}
```

- [ ] **Step 6: Build + commit**

```bash
npx tsc --noEmit
git add bom-web/src/api/queries/
git commit -m "feat(v3-web): add V3 TanStack Query hooks

useRequisitions/Submit/Cancel/Create, useSetMargin/Accept/Reject/FinalSign,
useUploadSignature, useCustomers/Customer/CreateCustomer/CustomerImplicitItems,
useItems/CreateItem/ToggleItemActive.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5: V3StatusBadge component

**Files:**
- Create: `bom-web/src/components/v3/V3StatusBadge.tsx`
- Create: `bom-web/src/components/v3/V3StatusBadge.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// bom-web/src/components/v3/V3StatusBadge.test.tsx
import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { V3StatusBadge } from "./V3StatusBadge";

describe("V3StatusBadge", () => {
  it("renders Draft status with neutral styling", () => {
    render(<V3StatusBadge status="Draft" />);
    const badge = screen.getByText("Draft");
    expect(badge).toBeInTheDocument();
    expect(badge.className).toMatch(/bg-gray-/);
  });

  it("renders Signed status with success styling", () => {
    render(<V3StatusBadge status="Signed" />);
    const badge = screen.getByText("Signed");
    expect(badge.className).toMatch(/bg-green-/);
  });

  it("renders Cancelled status with red styling", () => {
    render(<V3StatusBadge status="Cancelled" />);
    expect(screen.getByText("Cancelled").className).toMatch(/bg-red-/);
  });

  it("renders legacy V2 Approved status with success styling", () => {
    render(<V3StatusBadge status="Approved" />);
    expect(screen.getByText("Approved").className).toMatch(/bg-green-/);
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
npm test -- --run V3StatusBadge
```

Expected: FAIL — module `./V3StatusBadge` does not exist.

- [ ] **Step 3: Implement the component**

```tsx
// bom-web/src/components/v3/V3StatusBadge.tsx
import type { RequisitionStatus } from "../../types/api";

interface Props {
  status: RequisitionStatus;
}

const STATUS_STYLES: Record<RequisitionStatus, string> = {
  // V3
  Draft: "bg-gray-100 text-gray-700",
  Costing: "bg-blue-100 text-blue-700",
  MdPricing: "bg-amber-100 text-amber-700",
  CustomerConfirm: "bg-purple-100 text-purple-700",
  MdFinalSign: "bg-orange-100 text-orange-700",
  Signed: "bg-green-100 text-green-700",
  Cancelled: "bg-red-100 text-red-700",
  Rejected: "bg-red-100 text-red-700",

  // Legacy V2
  BomPending: "bg-gray-100 text-gray-700",
  BomInProgress: "bg-blue-100 text-blue-700",
  CostingPending: "bg-amber-100 text-amber-700",
  CostingInProgress: "bg-amber-100 text-amber-700",
  MdReview: "bg-orange-100 text-orange-700",
  Approved: "bg-green-100 text-green-700",
};

export function V3StatusBadge({ status }: Props) {
  const styles = STATUS_STYLES[status] ?? "bg-gray-100 text-gray-700";
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${styles}`}>
      {status}
    </span>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
npm test -- --run V3StatusBadge
```

Expected: PASS — all 4 cases.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/components/v3/V3StatusBadge.tsx bom-web/src/components/v3/V3StatusBadge.test.tsx
git commit -m "feat(v3-web): add V3StatusBadge component

Color-coded pill for V3 statuses + legacy V2 statuses (for historical reqs).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6: CreateCustomerModal

**Files:**
- Create: `bom-web/src/components/v3/CreateCustomerModal.tsx`
- Create: `bom-web/src/components/v3/CreateCustomerModal.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// bom-web/src/components/v3/CreateCustomerModal.test.tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { CreateCustomerModal } from "./CreateCustomerModal";
import { axios } from "../../api/axios";

vi.mock("../../api/axios", () => ({
  axios: { post: vi.fn(), get: vi.fn() },
}));

function renderWithProviders(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("CreateCustomerModal", () => {
  beforeEach(() => {
    vi.mocked(axios.post).mockReset();
  });

  it("posts new customer and calls onCreated with returned customer", async () => {
    vi.mocked(axios.post).mockResolvedValue({
      data: { id: 99, code: "CUST-0099", name: "Acme", email: "a@b.com", phoneNumber: "+1", address: "x", salesPersonId: 1, isDeleted: false },
    });

    const onCreated = vi.fn();
    const onClose = vi.fn();
    renderWithProviders(<CreateCustomerModal open={true} onClose={onClose} onCreated={onCreated} />);

    await userEvent.type(screen.getByLabelText(/name/i), "Acme");
    await userEvent.type(screen.getByLabelText(/email/i), "a@b.com");
    await userEvent.type(screen.getByLabelText(/phone/i), "+1234");
    await userEvent.type(screen.getByLabelText(/address/i), "Test Address");
    await userEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(expect.objectContaining({ id: 99, code: "CUST-0099" })));
    expect(onClose).toHaveBeenCalled();
  });

  it("shows validation error if name is empty", async () => {
    renderWithProviders(<CreateCustomerModal open={true} onClose={vi.fn()} onCreated={vi.fn()} />);
    await userEvent.click(screen.getByRole("button", { name: /create/i }));
    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(axios.post).not.toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
npm test -- --run CreateCustomerModal
```

Expected: FAIL — module not found.

- [ ] **Step 3: Implement the component**

```tsx
// bom-web/src/components/v3/CreateCustomerModal.tsx
import { useState } from "react";
import { useCreateCustomer } from "../../api/queries/customers";
import type { CustomerListItem } from "../../api/customers";

interface Props {
  open: boolean;
  onClose: () => void;
  onCreated: (customer: CustomerListItem) => void;
}

export function CreateCustomerModal({ open, onClose, onCreated }: Props) {
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [address, setAddress] = useState("");
  const [error, setError] = useState<string | null>(null);

  const createCustomer = useCreateCustomer();

  if (!open) return null;

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!name.trim()) {
      setError("Name is required");
      return;
    }
    try {
      const created = await createCustomer.mutateAsync({ name, email, phoneNumber, address });
      onCreated(created);
      onClose();
    } catch (err: any) {
      setError(err?.response?.data?.error ?? "Failed to create customer");
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
        <h2 className="text-lg font-semibold text-gray-900">Create Customer</h2>
        <p className="mt-1 text-xs text-gray-500">Code is auto-generated as CUST-XXXX on save.</p>

        <form onSubmit={onSubmit} className="mt-4 space-y-3">
          <label className="block">
            <span className="text-sm font-medium text-gray-700">Name</span>
            <input value={name} onChange={e => setName(e.target.value)}
              className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-blue-500"
              aria-label="name" autoFocus />
          </label>
          <label className="block">
            <span className="text-sm font-medium text-gray-700">Email</span>
            <input type="email" value={email} onChange={e => setEmail(e.target.value)}
              className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-blue-500"
              aria-label="email" />
          </label>
          <label className="block">
            <span className="text-sm font-medium text-gray-700">Phone</span>
            <input value={phoneNumber} onChange={e => setPhoneNumber(e.target.value)}
              className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-blue-500"
              aria-label="phone" />
          </label>
          <label className="block">
            <span className="text-sm font-medium text-gray-700">Address</span>
            <input value={address} onChange={e => setAddress(e.target.value)}
              className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-blue-500"
              aria-label="address" />
          </label>

          {error && <p className="text-sm text-red-600">{error}</p>}

          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={onClose}
              className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50">
              Cancel
            </button>
            <button type="submit" disabled={createCustomer.isPending}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {createCustomer.isPending ? "Creating…" : "Create"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

```bash
npm test -- --run CreateCustomerModal
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/components/v3/CreateCustomerModal.tsx bom-web/src/components/v3/CreateCustomerModal.test.tsx
git commit -m "feat(v3-web): add CreateCustomerModal

Inline modal with Name/Email/Phone/Address. Code auto-generated server-side.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7: CreateFinishedGoodModal

**Files:**
- Create: `bom-web/src/components/v3/CreateFinishedGoodModal.tsx`
- Create: `bom-web/src/components/v3/CreateFinishedGoodModal.test.tsx`

- [ ] **Step 1: Write the failing test**

```tsx
// bom-web/src/components/v3/CreateFinishedGoodModal.test.tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { CreateFinishedGoodModal } from "./CreateFinishedGoodModal";
import { axios } from "../../api/axios";

vi.mock("../../api/axios", () => ({ axios: { post: vi.fn() } }));

function renderWith(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("CreateFinishedGoodModal", () => {
  beforeEach(() => vi.mocked(axios.post).mockReset());

  it("creates a FinishedGood item and calls onCreated", async () => {
    vi.mocked(axios.post).mockResolvedValue({
      data: { id: 87, code: "FG-0087", description: "Test FG", type: "FinishedGood", branchId: 2, isActive: true },
    });

    const onCreated = vi.fn();
    renderWith(<CreateFinishedGoodModal open={true} onClose={vi.fn()} onCreated={onCreated} />);

    await userEvent.type(screen.getByLabelText(/description/i), "Test FG");
    await userEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() => expect(onCreated).toHaveBeenCalledWith(expect.objectContaining({ id: 87, code: "FG-0087", type: "FinishedGood" })));
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
npm test -- --run CreateFinishedGoodModal
```

Expected: FAIL.

- [ ] **Step 3: Implement**

```tsx
// bom-web/src/components/v3/CreateFinishedGoodModal.tsx
import { useState } from "react";
import { useCreateItem } from "../../api/queries/items";
import type { ItemListItem } from "../../api/items";

interface Props {
  open: boolean;
  onClose: () => void;
  onCreated: (item: ItemListItem) => void;
}

export function CreateFinishedGoodModal({ open, onClose, onCreated }: Props) {
  const [description, setDescription] = useState("");
  const [error, setError] = useState<string | null>(null);
  const createItem = useCreateItem();

  if (!open) return null;

  const onSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!description.trim()) {
      setError("Description is required");
      return;
    }
    try {
      const item = await createItem.mutateAsync({ description, type: "FinishedGood" });
      onCreated(item);
      onClose();
    } catch (err: any) {
      setError(err?.response?.data?.error ?? "Failed to create item");
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-md rounded-xl bg-white p-6 shadow-xl">
        <h2 className="text-lg font-semibold text-gray-900">Create Finished Good</h2>
        <p className="mt-1 text-xs text-gray-500">Code auto-generated (FG-XXXX). Branch: Alain.</p>
        <form onSubmit={onSubmit} className="mt-4 space-y-3">
          <label className="block">
            <span className="text-sm font-medium text-gray-700">Description</span>
            <input value={description} onChange={e => setDescription(e.target.value)} aria-label="description" autoFocus
              className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:ring-blue-500" />
          </label>
          {error && <p className="text-sm text-red-600">{error}</p>}
          <div className="flex justify-end gap-2 pt-2">
            <button type="button" onClick={onClose}
              className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50">Cancel</button>
            <button type="submit" disabled={createItem.isPending}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
              {createItem.isPending ? "Creating…" : "Create"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Verify + commit**

```bash
npm test -- --run CreateFinishedGoodModal
git add bom-web/src/components/v3/CreateFinishedGoodModal.tsx bom-web/src/components/v3/CreateFinishedGoodModal.test.tsx
git commit -m "feat(v3-web): add CreateFinishedGoodModal

Inline modal for FG item creation. Code auto-generated FG-XXXX server-side.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8: CreateRawMaterialModal

**Files:**
- Create: `bom-web/src/components/v3/CreateRawMaterialModal.tsx`
- Create: `bom-web/src/components/v3/CreateRawMaterialModal.test.tsx`

- [ ] **Step 1: Mirror Task 7 structure** — copy `CreateFinishedGoodModal.tsx` and adapt:
  - Component name: `CreateRawMaterialModal`
  - Heading: "Create Raw Material"
  - Description preview text: "Code auto-generated (RM-XXXX). Branch: Alain."
  - Mutation payload: `type: "RawMaterial"`

- [ ] **Step 2: Mirror test structure** — same shape, expect `code: "RM-0001"`-style + `type: "RawMaterial"`.

- [ ] **Step 3: Verify + commit**

```bash
npm test -- --run CreateRawMaterialModal
git add bom-web/src/components/v3/CreateRawMaterialModal.tsx bom-web/src/components/v3/CreateRawMaterialModal.test.tsx
git commit -m "feat(v3-web): add CreateRawMaterialModal

Inline modal for RM item creation. Code auto-generated RM-XXXX server-side.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9: BomEditorTable shared component

**Files:**
- Create: `bom-web/src/components/v3/BomEditorTable.tsx`
- Create: `bom-web/src/components/v3/BomEditorTable.test.tsx`

The BomEditorTable is the inline-BOM editor used inside `NewRequisitionPage` (per FG card) AND read-only on `RequisitionDetailPage`/`CustomerConfirmPage` for status display.

- [ ] **Step 1: Write the failing test**

```tsx
// bom-web/src/components/v3/BomEditorTable.test.tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it, expect, vi } from "vitest";
import { BomEditorTable, type BomLineRow } from "./BomEditorTable";
import { axios } from "../../api/axios";

vi.mock("../../api/axios", () => ({ axios: { get: vi.fn(), post: vi.fn() } }));

function renderWith(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("BomEditorTable", () => {
  it("renders existing lines + supports adding new line", async () => {
    vi.mocked(axios.get).mockResolvedValue({
      data: [
        { id: 12, code: "RM-0012", description: "BOPP", type: "RawMaterial", branchId: 2, isActive: true },
        { id: 34, code: "RM-0034", description: "INK", type: "RawMaterial", branchId: 2, isActive: true },
      ],
    });

    const lines: BomLineRow[] = [
      { itemId: 12, qtyPerKg: 0.44, micron: "20", processId: 1 },
    ];

    const onChange = vi.fn();
    renderWith(<BomEditorTable lines={lines} onChange={onChange} />);

    await waitFor(() => expect(screen.getByDisplayValue("0.44")).toBeInTheDocument());
    expect(screen.getByDisplayValue("20")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /add raw material/i }));

    expect(onChange).toHaveBeenCalledWith(
      expect.arrayContaining([
        expect.objectContaining({ itemId: 12, qtyPerKg: 0.44, micron: "20" }),
        expect.objectContaining({ itemId: 0, qtyPerKg: 0, micron: "" }),
      ])
    );
  });

  it("renders read-only mode without inputs", () => {
    const lines: BomLineRow[] = [
      { itemId: 12, qtyPerKg: 0.44, micron: "20", processId: 1 },
    ];
    renderWith(<BomEditorTable lines={lines} readOnly={true} />);
    expect(screen.queryByDisplayValue("0.44")).not.toBeInTheDocument();
    expect(screen.getByText("0.44")).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
npm test -- --run BomEditorTable
```

Expected: FAIL.

- [ ] **Step 3: Implement**

```tsx
// bom-web/src/components/v3/BomEditorTable.tsx
import { useState } from "react";
import { useItems } from "../../api/queries/items";
import { CreateRawMaterialModal } from "./CreateRawMaterialModal";

export interface BomLineRow {
  itemId: number;
  qtyPerKg: number;
  micron: string | null;
  processId: number;
}

interface Props {
  lines: BomLineRow[];
  onChange?: (lines: BomLineRow[]) => void;
  readOnly?: boolean;
}

const DEFAULT_PROCESS_ID = 1; // "Extrusion" — V3 cutover-day default; product can extend later

export function BomEditorTable({ lines, onChange, readOnly = false }: Props) {
  const [createOpen, setCreateOpen] = useState(false);
  const items = useItems({ type: "RawMaterial" });

  const itemMap = new Map((items.data ?? []).map(i => [i.id, i]));

  const updateLine = (idx: number, patch: Partial<BomLineRow>) => {
    if (!onChange) return;
    onChange(lines.map((l, i) => (i === idx ? { ...l, ...patch } : l)));
  };
  const removeLine = (idx: number) => {
    if (!onChange) return;
    onChange(lines.filter((_, i) => i !== idx));
  };
  const addLine = () => {
    if (!onChange) return;
    onChange([...lines, { itemId: 0, qtyPerKg: 0, micron: "", processId: DEFAULT_PROCESS_ID }]);
  };

  return (
    <div className="space-y-2">
      <table className="w-full text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-2 py-1 text-left font-medium text-gray-700">Item</th>
            <th className="px-2 py-1 text-left font-medium text-gray-700">Qty/KG</th>
            <th className="px-2 py-1 text-left font-medium text-gray-700">Micron</th>
            {!readOnly && <th />}
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {lines.map((line, idx) => {
            const item = itemMap.get(line.itemId);
            return (
              <tr key={idx}>
                <td className="px-2 py-1">
                  {readOnly ? (
                    <span>{item?.description ?? "—"}</span>
                  ) : (
                    <select value={line.itemId} onChange={e => updateLine(idx, { itemId: parseInt(e.target.value) })}
                      className="w-full rounded border-gray-300 text-sm">
                      <option value={0}>— select —</option>
                      {(items.data ?? []).map(i => (
                        <option key={i.id} value={i.id}>{i.code} · {i.description}</option>
                      ))}
                    </select>
                  )}
                </td>
                <td className="px-2 py-1">
                  {readOnly ? <span>{line.qtyPerKg}</span> : (
                    <input type="number" step="0.001" value={line.qtyPerKg}
                      onChange={e => updateLine(idx, { qtyPerKg: parseFloat(e.target.value) || 0 })}
                      className="w-24 rounded border-gray-300 text-sm" />
                  )}
                </td>
                <td className="px-2 py-1">
                  {readOnly ? <span>{line.micron ?? "—"}</span> : (
                    <input type="text" value={line.micron ?? ""}
                      onChange={e => updateLine(idx, { micron: e.target.value })}
                      className="w-20 rounded border-gray-300 text-sm" />
                  )}
                </td>
                {!readOnly && (
                  <td className="px-2 py-1 text-right">
                    <button type="button" onClick={() => removeLine(idx)}
                      className="text-xs text-red-600 hover:text-red-700">Remove</button>
                  </td>
                )}
              </tr>
            );
          })}
        </tbody>
      </table>

      {!readOnly && (
        <div className="flex gap-2">
          <button type="button" onClick={addLine}
            className="rounded-md border border-gray-300 bg-white px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50">
            + Add Raw Material
          </button>
          <button type="button" onClick={() => setCreateOpen(true)}
            className="rounded-md border border-blue-300 bg-blue-50 px-3 py-1.5 text-xs font-medium text-blue-700 hover:bg-blue-100">
            + Create new RM
          </button>
        </div>
      )}

      <CreateRawMaterialModal
        open={createOpen}
        onClose={() => setCreateOpen(false)}
        onCreated={(item) => {
          // Auto-add the just-created RM as a new BOM line
          if (onChange) {
            onChange([...lines, { itemId: item.id, qtyPerKg: 0, micron: "", processId: DEFAULT_PROCESS_ID }]);
          }
        }}
      />
    </div>
  );
}
```

- [ ] **Step 4: Verify + commit**

```bash
npm test -- --run BomEditorTable
git add bom-web/src/components/v3/BomEditorTable.tsx bom-web/src/components/v3/BomEditorTable.test.tsx
git commit -m "feat(v3-web): add BomEditorTable shared component

Inline BOM editor used in NewRequisitionPage (per-FG card) and read-only
on detail/confirm pages. Inline 'create new RM' via CreateRawMaterialModal.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 10: NewRequisitionPage rewrite

**Files:**
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.tsx`
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.test.tsx`

This is the **largest single page change** in Phase B. Rewrite combines sales+BOM into one screen.

- [ ] **Step 1: Rewrite the test**

```tsx
// bom-web/src/features/requisitions/NewRequisitionPage.test.tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { NewRequisitionPage } from "./NewRequisitionPage";
import { axios } from "../../api/axios";

vi.mock("../../api/axios", () => ({ axios: { get: vi.fn(), post: vi.fn(), patch: vi.fn() } }));

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <NewRequisitionPage />
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe("NewRequisitionPage (V3)", () => {
  beforeEach(() => {
    vi.mocked(axios.get).mockReset();
    vi.mocked(axios.post).mockReset();
  });

  it("loads customers and currency picker, renders empty state", async () => {
    vi.mocked(axios.get).mockImplementation((url: string) => {
      if (url === "/api/customers") return Promise.resolve({ data: [{ id: 1, code: "CUST-0001", name: "Acme", isDeleted: false }] });
      return Promise.resolve({ data: [] });
    });
    renderPage();
    await waitFor(() => expect(screen.getByText(/new requisition/i)).toBeInTheDocument());
    expect(screen.getByLabelText(/customer/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/currency/i)).toBeInTheDocument();
    expect(screen.getByText(/no finished goods added/i)).toBeInTheDocument();
  });

  it("submits a V3 payload with finishedGoods array", async () => {
    vi.mocked(axios.get).mockImplementation((url: string) => {
      if (url === "/api/customers") return Promise.resolve({ data: [{ id: 1, code: "CUST-0001", name: "Acme", isDeleted: false }] });
      if (url.startsWith("/api/customers/1/items")) return Promise.resolve({ data: [{ id: 87, code: "FG-0087", description: "Test FG" }] });
      if (url === "/api/items") return Promise.resolve({ data: [
        { id: 87, code: "FG-0087", description: "Test FG", type: "FinishedGood", branchId: 2, isActive: true },
        { id: 12, code: "RM-0012", description: "BOPP", type: "RawMaterial", branchId: 2, isActive: true },
      ] });
      return Promise.resolve({ data: [] });
    });
    vi.mocked(axios.post).mockResolvedValue({ data: { id: 100, refNo: "REQ-0100", status: "Draft" } });

    renderPage();

    await userEvent.selectOptions(await screen.findByLabelText(/customer/i), "1");
    await userEvent.selectOptions(screen.getByLabelText(/currency/i), "USD");
    await userEvent.click(screen.getByRole("button", { name: /add finished good/i }));
    // Fill the FG card
    await userEvent.selectOptions(screen.getByLabelText(/fg item/i), "87");
    await userEvent.type(screen.getByLabelText(/quantity/i), "5000");

    // Submit
    await userEvent.click(screen.getByRole("button", { name: /^submit$/i }));

    await waitFor(() => expect(axios.post).toHaveBeenCalledWith(
      "/api/requisitions",
      expect.objectContaining({
        customerId: 1,
        quotationCurrency: "USD",
        finishedGoods: expect.arrayContaining([
          expect.objectContaining({ itemId: 87, expectedQtyKg: 5000 }),
        ]),
      })
    ));
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
npm test -- --run NewRequisitionPage
```

Expected: FAIL — V3 page doesn't exist yet (V2.3 page is there but the test expects V3 shape).

- [ ] **Step 3: Rewrite the page**

```tsx
// bom-web/src/features/requisitions/NewRequisitionPage.tsx
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { useCustomers, useCustomerImplicitItems } from "../../api/queries/customers";
import { useItems } from "../../api/queries/items";
import { useCreateRequisition, useSubmitRequisition } from "../../api/queries/requisitions";
import { BomEditorTable, type BomLineRow } from "../../components/v3/BomEditorTable";
import { CreateCustomerModal } from "../../components/v3/CreateCustomerModal";
import { CreateFinishedGoodModal } from "../../components/v3/CreateFinishedGoodModal";

interface FgCardState {
  itemId: number;
  expectedQtyKg: number;
  printing: boolean;
  bomLines: BomLineRow[];
}

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "JPY"];

export function NewRequisitionPage() {
  const navigate = useNavigate();
  const [customerId, setCustomerId] = useState<number>(0);
  const [currency, setCurrency] = useState("AED");
  const [referenceNumber, setReferenceNumber] = useState("");
  const [notes, setNotes] = useState("");
  const [fgs, setFgs] = useState<FgCardState[]>([]);

  const [createCustomerOpen, setCreateCustomerOpen] = useState(false);
  const [createFgOpen, setCreateFgOpen] = useState(false);

  const customers = useCustomers();
  const allItems = useItems();
  const customerFgs = useCustomerImplicitItems(customerId || null);

  const createReq = useCreateRequisition();
  const submitReq = useSubmitRequisition();

  const fgItemPool = customerId
    ? (customerFgs.data ?? [])
    : (allItems.data ?? []).filter(i => i.type === "FinishedGood");

  const updateFg = (idx: number, patch: Partial<FgCardState>) =>
    setFgs(s => s.map((fg, i) => (i === idx ? { ...fg, ...patch } : fg)));
  const removeFg = (idx: number) => setFgs(s => s.filter((_, i) => i !== idx));
  const addFg = () => setFgs(s => [...s, { itemId: 0, expectedQtyKg: 0, printing: false, bomLines: [] }]);

  const onSave = async (submit: boolean) => {
    if (!customerId) { toast.error("Pick a customer first"); return; }
    if (fgs.length === 0) { toast.error("Add at least one finished good"); return; }
    for (const fg of fgs) {
      if (fg.itemId === 0) { toast.error("Each FG must have an item selected"); return; }
      if (fg.bomLines.length === 0) { toast.error("Each FG must have at least one BOM line"); return; }
    }

    try {
      const created = await createReq.mutateAsync({
        customerId,
        quotationCurrency: currency,
        referenceNumber: referenceNumber || undefined,
        notes: notes || undefined,
        finishedGoods: fgs.map(fg => ({
          itemId: fg.itemId,
          expectedQtyKg: fg.expectedQtyKg,
          printing: fg.printing,
          bomLines: fg.bomLines.map(b => ({ itemId: b.itemId, qtyPerKg: b.qtyPerKg, micron: b.micron, processId: b.processId })),
        })),
      });

      if (submit) {
        await submitReq.mutateAsync(created.id);
        toast.success(`Submitted: ${created.refNo}`);
      } else {
        toast.success(`Saved as draft: ${created.refNo}`);
      }
      navigate(`/requisitions/${created.id}`);
    } catch (err: any) {
      toast.error(err?.response?.data?.error ?? "Failed to save requisition");
    }
  };

  return (
    <div className="mx-auto max-w-5xl p-6">
      <h1 className="text-2xl font-semibold text-gray-900">New Requisition</h1>

      <div className="mt-6 grid grid-cols-2 gap-4">
        <label className="block">
          <span className="text-sm font-medium text-gray-700">Customer</span>
          <div className="mt-1 flex gap-2">
            <select aria-label="customer" value={customerId} onChange={e => setCustomerId(parseInt(e.target.value))}
              className="flex-1 rounded-md border-gray-300 px-3 py-2 text-sm">
              <option value={0}>— select —</option>
              {(customers.data ?? []).filter(c => !c.isDeleted).map(c => (
                <option key={c.id} value={c.id}>{c.code} · {c.name}</option>
              ))}
            </select>
            <button type="button" onClick={() => setCreateCustomerOpen(true)}
              className="rounded-md border border-blue-300 bg-blue-50 px-3 py-2 text-xs font-medium text-blue-700 hover:bg-blue-100">+ New</button>
          </div>
        </label>

        <label className="block">
          <span className="text-sm font-medium text-gray-700">Currency</span>
          <select aria-label="currency" value={currency} onChange={e => setCurrency(e.target.value)}
            className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm">
            {CURRENCIES.map(c => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>

        <label className="block col-span-2">
          <span className="text-sm font-medium text-gray-700">Reference (optional)</span>
          <input value={referenceNumber} onChange={e => setReferenceNumber(e.target.value)}
            placeholder="PO-9941"
            className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm" />
        </label>
      </div>

      <h2 className="mt-8 text-lg font-semibold text-gray-900">Finished Goods</h2>
      {fgs.length === 0 && (
        <p className="mt-2 text-sm text-gray-500">No finished goods added yet.</p>
      )}
      <div className="mt-3 space-y-4">
        {fgs.map((fg, idx) => (
          <div key={idx} className="rounded-lg border border-gray-200 p-4">
            <div className="flex justify-between">
              <h3 className="font-medium text-gray-900">FG #{idx + 1}</h3>
              <button type="button" onClick={() => removeFg(idx)} className="text-xs text-red-600">Remove FG</button>
            </div>

            <div className="mt-3 grid grid-cols-3 gap-3">
              <label className="block col-span-2">
                <span className="text-sm font-medium text-gray-700">FG Item</span>
                <div className="mt-1 flex gap-2">
                  <select aria-label="fg item" value={fg.itemId}
                    onChange={e => updateFg(idx, { itemId: parseInt(e.target.value) })}
                    className="flex-1 rounded-md border-gray-300 px-3 py-2 text-sm">
                    <option value={0}>— select —</option>
                    {fgItemPool.map(i => (
                      <option key={i.id} value={i.id}>{i.code} · {i.description}</option>
                    ))}
                  </select>
                  <button type="button" onClick={() => setCreateFgOpen(true)}
                    className="rounded-md border border-blue-300 bg-blue-50 px-3 py-2 text-xs font-medium text-blue-700 hover:bg-blue-100">+ New FG</button>
                </div>
              </label>

              <label className="block">
                <span className="text-sm font-medium text-gray-700">Quantity (KG)</span>
                <input type="number" aria-label="quantity" value={fg.expectedQtyKg}
                  onChange={e => updateFg(idx, { expectedQtyKg: parseFloat(e.target.value) || 0 })}
                  className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm" />
              </label>
            </div>

            <label className="mt-3 inline-flex items-center gap-2">
              <input type="checkbox" checked={fg.printing}
                onChange={e => updateFg(idx, { printing: e.target.checked })} />
              <span className="text-sm text-gray-700">Printing required</span>
            </label>

            <h4 className="mt-4 text-sm font-medium text-gray-700">BOM Recipe</h4>
            <div className="mt-2">
              <BomEditorTable
                lines={fg.bomLines}
                onChange={lines => updateFg(idx, { bomLines: lines })}
              />
            </div>
          </div>
        ))}
        <button type="button" onClick={addFg}
          className="rounded-md border border-blue-300 bg-blue-50 px-4 py-2 text-sm font-medium text-blue-700 hover:bg-blue-100">
          + Add Finished Good
        </button>
      </div>

      <h2 className="mt-8 text-lg font-semibold text-gray-900">Notes</h2>
      <textarea value={notes} onChange={e => setNotes(e.target.value)} rows={3}
        className="mt-2 w-full rounded-md border-gray-300 px-3 py-2 text-sm" />

      <div className="mt-8 flex justify-end gap-3">
        <button type="button" onClick={() => navigate(-1)}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50">Cancel</button>
        <button type="button" onClick={() => onSave(false)}
          disabled={createReq.isPending}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50 disabled:opacity-50">
          Save Draft
        </button>
        <button type="button" onClick={() => onSave(true)}
          disabled={createReq.isPending || submitReq.isPending}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
          Submit
        </button>
      </div>

      <CreateCustomerModal open={createCustomerOpen} onClose={() => setCreateCustomerOpen(false)}
        onCreated={cust => setCustomerId(cust.id)} />
      <CreateFinishedGoodModal open={createFgOpen} onClose={() => setCreateFgOpen(false)}
        onCreated={() => { /* refetch via mutation invalidation */ }} />
    </div>
  );
}
```

- [ ] **Step 4: Run tests**

```bash
npm test -- --run NewRequisitionPage
```

Expected: PASS — both tests.

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/requisitions/NewRequisitionPage.tsx bom-web/src/features/requisitions/NewRequisitionPage.test.tsx
git commit -m "feat(v3-web): rewrite NewRequisitionPage for V3 combined sales+BOM flow

Customer + currency + N FG cards (each with inline BOM editor + Printing
checkbox + quantity) + notes. Save Draft / Submit buttons. Customer's
implicit FG list auto-loads from past reqs (D20).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 11: CustomerConfirmPage

**Files:**
- Create: `bom-web/src/features/requisitions/CustomerConfirmPage.tsx`
- Create: `bom-web/src/features/requisitions/CustomerConfirmPage.test.tsx`

Sales-only page where SP shares the MD-priced quotation with the customer (offline) and reports back the result.

- [ ] **Step 1: Write the test**

```tsx
// bom-web/src/features/requisitions/CustomerConfirmPage.test.tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { CustomerConfirmPage } from "./CustomerConfirmPage";
import { axios } from "../../api/axios";

vi.mock("../../api/axios", () => ({ axios: { get: vi.fn(), post: vi.fn() } }));

function renderAt(path: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/requisitions/:id/customer-confirm" element={<CustomerConfirmPage />} />
          <Route path="/requisitions/:id" element={<div>req-detail</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe("CustomerConfirmPage", () => {
  beforeEach(() => {
    vi.mocked(axios.get).mockReset();
    vi.mocked(axios.post).mockReset();
  });

  it("renders MD-priced quotation + accept/reject buttons", async () => {
    vi.mocked(axios.get).mockResolvedValue({
      data: {
        id: 100, refNo: "REQ-0100", status: "CustomerConfirm", currencyCode: "USD", notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [{ id: 50, expectedQty: 5000, hasPrinting: false, item: { id: 87, code: "FG-0087", description: "Test FG" }, bomLines: [], costs: null }],
      },
    });

    renderAt("/requisitions/100/customer-confirm");
    await waitFor(() => expect(screen.getByText("REQ-0100")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /customer accepted/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /request md to re-price/i })).toBeInTheDocument();
  });

  it("calls accept-customer endpoint on Accept click", async () => {
    vi.mocked(axios.get).mockResolvedValue({
      data: {
        id: 100, refNo: "REQ-0100", status: "CustomerConfirm", currencyCode: "USD", notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [],
      },
    });
    vi.mocked(axios.post).mockResolvedValue({ data: { id: 100, status: "MdFinalSign" } });

    renderAt("/requisitions/100/customer-confirm");
    await screen.findByRole("button", { name: /customer accepted/i });
    await userEvent.click(screen.getByRole("button", { name: /customer accepted/i }));

    await waitFor(() => expect(axios.post).toHaveBeenCalledWith(
      "/api/approvals/100/accept-customer",
      expect.any(Object)
    ));
  });
});
```

- [ ] **Step 2: Run test to verify it fails**

```bash
npm test -- --run CustomerConfirmPage
```

Expected: FAIL.

- [ ] **Step 3: Implement**

```tsx
// bom-web/src/features/requisitions/CustomerConfirmPage.tsx
import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useRequisition } from "../../api/queries/requisitions";
import { useAcceptCustomer, useRejectCustomer } from "../../api/queries/approvals";
import { V3StatusBadge } from "../../components/v3/V3StatusBadge";

export function CustomerConfirmPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const reqId = id ? parseInt(id) : 0;
  const { data: req, isLoading } = useRequisition(reqId);
  const accept = useAcceptCustomer();
  const reject = useRejectCustomer();

  const [feedback, setFeedback] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  const [showRejectInput, setShowRejectInput] = useState(false);

  if (isLoading || !req) return <div className="p-6">Loading…</div>;

  const onAccept = async () => {
    try {
      await accept.mutateAsync({ requisitionId: reqId, customerFeedback: feedback || undefined });
      toast.success("Confirmed — sent for MD final sign");
      navigate(`/requisitions/${reqId}`);
    } catch (err: any) {
      toast.error(err?.response?.data?.error ?? "Failed");
    }
  };

  const onReject = async () => {
    if (rejectReason.trim().length < 5) {
      toast.error("Reason must be ≥5 chars");
      return;
    }
    try {
      await reject.mutateAsync({ requisitionId: reqId, reason: rejectReason });
      toast.success("Sent back to MD for re-pricing");
      navigate(`/requisitions/${reqId}`);
    } catch (err: any) {
      toast.error(err?.response?.data?.error ?? "Failed");
    }
  };

  return (
    <div className="mx-auto max-w-4xl p-6">
      <div className="flex items-center gap-3">
        <h1 className="text-2xl font-semibold text-gray-900">{req.refNo}</h1>
        <V3StatusBadge status={req.status} />
      </div>
      <p className="mt-1 text-sm text-gray-500">Customer: {req.customer.name} · Currency: {req.currencyCode}</p>

      <h2 className="mt-6 text-lg font-semibold text-gray-900">MD-Priced Quotation</h2>
      <table className="mt-2 w-full text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-3 py-2 text-left font-medium text-gray-700">Finished Good</th>
            <th className="px-3 py-2 text-right font-medium text-gray-700">Qty (KG)</th>
            <th className="px-3 py-2 text-right font-medium text-gray-700">Price/KG ({req.currencyCode})</th>
            <th className="px-3 py-2 text-right font-medium text-gray-700">Total</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {req.finishedGoods.map(fg => (
            <tr key={fg.id}>
              <td className="px-3 py-2">{fg.item.description}</td>
              <td className="px-3 py-2 text-right">{fg.expectedQty.toLocaleString()}</td>
              <td className="px-3 py-2 text-right">—</td>
              <td className="px-3 py-2 text-right">—</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2 className="mt-8 text-lg font-semibold text-gray-900">Customer feedback</h2>
      <textarea value={feedback} onChange={e => setFeedback(e.target.value)} rows={3}
        placeholder="Optional — what did customer say?"
        className="mt-2 w-full rounded-md border-gray-300 px-3 py-2 text-sm" />

      <div className="mt-6 flex flex-wrap gap-3">
        <button onClick={onAccept} disabled={accept.isPending}
          className="rounded-md bg-green-600 px-4 py-2 text-sm font-medium text-white hover:bg-green-700 disabled:opacity-50">
          ✓ Customer Accepted
        </button>
        <button onClick={() => setShowRejectInput(true)}
          className="rounded-md bg-amber-500 px-4 py-2 text-sm font-medium text-white hover:bg-amber-600">
          ✕ Request MD to Re-price
        </button>
      </div>

      {showRejectInput && (
        <div className="mt-4 rounded-lg border border-amber-200 bg-amber-50 p-4">
          <label className="block">
            <span className="text-sm font-medium text-amber-900">Reason for re-price (≥5 chars)</span>
            <input value={rejectReason} onChange={e => setRejectReason(e.target.value)}
              className="mt-1 w-full rounded-md border-amber-300 px-3 py-2 text-sm" />
          </label>
          <button onClick={onReject} disabled={reject.isPending}
            className="mt-3 rounded-md bg-amber-600 px-4 py-2 text-sm font-medium text-white hover:bg-amber-700 disabled:opacity-50">
            Send Back to MD
          </button>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 4: Verify + commit**

```bash
npm test -- --run CustomerConfirmPage
git add bom-web/src/features/requisitions/CustomerConfirmPage.tsx bom-web/src/features/requisitions/CustomerConfirmPage.test.tsx
git commit -m "feat(v3-web): add CustomerConfirmPage

Sales-only page: shows MD-priced quotation, accept goes to MdFinalSign,
reject re-routes to MdPricing with reason (D8 — re-margin loop).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 12: MdMarginPage (Stage 1)

**Files:**
- Create: `bom-web/src/features/approvals/MdMarginPage.tsx`
- Create: `bom-web/src/features/approvals/MdMarginPage.test.tsx`

MD enters per-FG margin/KG (in quote currency) on a req that's in `MdPricing` status.

- [ ] **Step 1: Test**

```tsx
// bom-web/src/features/approvals/MdMarginPage.test.tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MdMarginPage } from "./MdMarginPage";
import { axios } from "../../api/axios";

vi.mock("../../api/axios", () => ({ axios: { get: vi.fn(), post: vi.fn() } }));

function renderAt(path: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/approvals/:id/margin" element={<MdMarginPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe("MdMarginPage", () => {
  beforeEach(() => {
    vi.mocked(axios.get).mockReset();
    vi.mocked(axios.post).mockReset();
  });

  it("submits per-FG margins to set-margin endpoint", async () => {
    vi.mocked(axios.get).mockResolvedValue({
      data: {
        id: 100, refNo: "REQ-0100", status: "MdPricing", currencyCode: "USD", notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [
          { id: 50, expectedQty: 5000, hasPrinting: false, item: { id: 87, code: "FG-0087", description: "FG-A" }, bomLines: [], costs: null },
          { id: 51, expectedQty: 2000, hasPrinting: true, item: { id: 88, code: "FG-0088", description: "FG-B" }, bomLines: [], costs: null },
        ],
      },
    });
    vi.mocked(axios.post).mockResolvedValue({ data: { id: 100, status: "CustomerConfirm", approvalId: 5 } });

    renderAt("/approvals/100/margin");
    await waitFor(() => expect(screen.getByText("REQ-0100")).toBeInTheDocument());

    const inputs = screen.getAllByRole("spinbutton") as HTMLInputElement[];
    await userEvent.type(inputs[0], "0.5");
    await userEvent.type(inputs[1], "0.7");
    await userEvent.click(screen.getByRole("button", { name: /submit/i }));

    await waitFor(() => expect(axios.post).toHaveBeenCalledWith(
      "/api/approvals/100/set-margin",
      expect.objectContaining({
        items: expect.arrayContaining([
          expect.objectContaining({ requisitionItemId: 50, marginPerKg: 0.5 }),
          expect.objectContaining({ requisitionItemId: 51, marginPerKg: 0.7 }),
        ]),
      })
    ));
  });
});
```

- [ ] **Step 2: Test fails**

```bash
npm test -- --run MdMarginPage
```

Expected: FAIL.

- [ ] **Step 3: Implement**

```tsx
// bom-web/src/features/approvals/MdMarginPage.tsx
import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useRequisition } from "../../api/queries/requisitions";
import { useSetMargin } from "../../api/queries/approvals";
import { V3StatusBadge } from "../../components/v3/V3StatusBadge";

export function MdMarginPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const reqId = id ? parseInt(id) : 0;
  const { data: req, isLoading } = useRequisition(reqId);
  const setMargin = useSetMargin();
  const [margins, setMargins] = useState<Record<number, number>>({});
  const [notes, setNotes] = useState("");

  if (isLoading || !req) return <div className="p-6">Loading…</div>;

  const onSubmit = async () => {
    const items = req.finishedGoods.map(fg => ({
      requisitionItemId: fg.id,
      marginPerKg: margins[fg.id] ?? 0,
    }));
    if (items.some(i => i.marginPerKg < 0)) { toast.error("Margin must be ≥ 0"); return; }

    try {
      await setMargin.mutateAsync({ requisitionId: reqId, payload: { notes: notes || undefined, items } });
      toast.success("Margin saved — now sales will confirm with customer");
      navigate(`/requisitions/${reqId}`);
    } catch (err: any) {
      toast.error(err?.response?.data?.error ?? "Failed");
    }
  };

  return (
    <div className="mx-auto max-w-4xl p-6">
      <div className="flex items-center gap-3">
        <h1 className="text-2xl font-semibold text-gray-900">{req.refNo}</h1>
        <V3StatusBadge status={req.status} />
      </div>
      <p className="mt-1 text-sm text-gray-500">Customer: {req.customer.name} · Currency: {req.currencyCode}</p>

      <h2 className="mt-6 text-lg font-semibold text-gray-900">Set Margin per FG ({req.currencyCode}/KG)</h2>
      <table className="mt-2 w-full text-sm">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-3 py-2 text-left font-medium text-gray-700">Finished Good</th>
            <th className="px-3 py-2 text-right font-medium text-gray-700">Qty (KG)</th>
            <th className="px-3 py-2 text-right font-medium text-gray-700">Margin/KG</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {req.finishedGoods.map(fg => (
            <tr key={fg.id}>
              <td className="px-3 py-2">{fg.item.description}</td>
              <td className="px-3 py-2 text-right">{fg.expectedQty.toLocaleString()}</td>
              <td className="px-3 py-2 text-right">
                <input type="number" step="0.01" value={margins[fg.id] ?? ""}
                  onChange={e => setMargins(m => ({ ...m, [fg.id]: parseFloat(e.target.value) || 0 }))}
                  className="w-28 rounded border-gray-300 px-2 py-1 text-right text-sm" />
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <label className="mt-6 block">
        <span className="text-sm font-medium text-gray-700">Notes (optional)</span>
        <textarea value={notes} onChange={e => setNotes(e.target.value)} rows={3}
          className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm" />
      </label>

      <div className="mt-6 flex justify-end gap-3">
        <button onClick={() => navigate(-1)} className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50">
          Cancel
        </button>
        <button onClick={onSubmit} disabled={setMargin.isPending}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
          Submit
        </button>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Verify + commit**

```bash
npm test -- --run MdMarginPage
git add bom-web/src/features/approvals/MdMarginPage.tsx bom-web/src/features/approvals/MdMarginPage.test.tsx
git commit -m "feat(v3-web): add MdMarginPage (Stage 1 — initial pricing)

MD enters per-FG margin/KG in quote currency. Submitting transitions
MdPricing -> CustomerConfirm. Replaces V2.3 single-shot Approve.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 13: MdFinalSignPage (Stage 2 — type-to-confirm)

**Files:**
- Create: `bom-web/src/features/approvals/MdFinalSignPage.tsx`
- Create: `bom-web/src/features/approvals/MdFinalSignPage.test.tsx`

MD reviews and signs off; type "SIGN" to confirm; PDF generated server-side.

- [ ] **Step 1: Test**

```tsx
// bom-web/src/features/approvals/MdFinalSignPage.test.tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MdFinalSignPage } from "./MdFinalSignPage";
import { axios } from "../../api/axios";

vi.mock("../../api/axios", () => ({ axios: { get: vi.fn(), post: vi.fn() } }));

function renderAt(path: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/approvals/:id/final" element={<MdFinalSignPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe("MdFinalSignPage", () => {
  beforeEach(() => {
    vi.mocked(axios.get).mockReset();
    vi.mocked(axios.post).mockReset();
  });

  it("requires SIGN token to enable submit", async () => {
    vi.mocked(axios.get).mockResolvedValue({
      data: {
        id: 100, refNo: "REQ-0100", status: "MdFinalSign", currencyCode: "USD", notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [],
      },
    });

    renderAt("/approvals/100/final");
    await screen.findByText("REQ-0100");
    const submitBtn = screen.getByRole("button", { name: /sign and lock/i }) as HTMLButtonElement;
    expect(submitBtn).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/type SIGN to confirm/i), "SIGN");
    expect(submitBtn).not.toBeDisabled();
  });

  it("posts final-sign on submit", async () => {
    vi.mocked(axios.get).mockResolvedValue({
      data: {
        id: 100, refNo: "REQ-0100", status: "MdFinalSign", currencyCode: "USD", notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [],
      },
    });
    vi.mocked(axios.post).mockResolvedValue({ data: { id: 100, status: "Signed", approvalId: 5, pdfDownloadUrl: "/api/approvals/100/pdf" } });

    renderAt("/approvals/100/final");
    await screen.findByText("REQ-0100");
    await userEvent.type(screen.getByLabelText(/type SIGN to confirm/i), "SIGN");
    await userEvent.click(screen.getByRole("button", { name: /sign and lock/i }));

    await waitFor(() => expect(axios.post).toHaveBeenCalledWith(
      "/api/approvals/100/final-sign",
      expect.objectContaining({ confirmationToken: "SIGN" })
    ));
  });
});
```

- [ ] **Step 2: Test fails**

```bash
npm test -- --run MdFinalSignPage
```

- [ ] **Step 3: Implement**

```tsx
// bom-web/src/features/approvals/MdFinalSignPage.tsx
import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { useRequisition } from "../../api/queries/requisitions";
import { useFinalSign } from "../../api/queries/approvals";
import { V3StatusBadge } from "../../components/v3/V3StatusBadge";

export function MdFinalSignPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const reqId = id ? parseInt(id) : 0;
  const { data: req, isLoading } = useRequisition(reqId);
  const sign = useFinalSign();
  const [token, setToken] = useState("");
  const [notes, setNotes] = useState("");

  if (isLoading || !req) return <div className="p-6">Loading…</div>;

  const onSubmit = async () => {
    try {
      const result = await sign.mutateAsync({
        requisitionId: reqId,
        payload: { confirmationToken: token, notes: notes || undefined },
      });
      toast.success("Signed and locked. PDF available.");
      navigate(`/requisitions/${reqId}`);
      void result; // pdfDownloadUrl returned but RequisitionDetailPage handles download
    } catch (err: any) {
      toast.error(err?.response?.data?.error ?? "Failed");
    }
  };

  return (
    <div className="mx-auto max-w-4xl p-6">
      <div className="flex items-center gap-3">
        <h1 className="text-2xl font-semibold text-gray-900">{req.refNo}</h1>
        <V3StatusBadge status={req.status} />
      </div>
      <p className="mt-1 text-sm text-gray-500">Customer: {req.customer.name}</p>

      <div className="mt-6 rounded-lg border border-orange-200 bg-orange-50 p-4">
        <h2 className="text-base font-semibold text-orange-900">Final sign locks this quotation</h2>
        <p className="mt-1 text-sm text-orange-800">
          After signing, no changes can be made. The PDF will be generated immediately and can
          be downloaded by the salesperson to share with the customer manually.
        </p>
      </div>

      <label className="mt-6 block">
        <span className="text-sm font-medium text-gray-700">Notes (optional)</span>
        <textarea value={notes} onChange={e => setNotes(e.target.value)} rows={3}
          className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm" />
      </label>

      <label className="mt-4 block">
        <span className="text-sm font-medium text-gray-700">Type SIGN to confirm</span>
        <input value={token} onChange={e => setToken(e.target.value)} aria-label="type SIGN to confirm"
          placeholder="SIGN"
          className="mt-1 w-full rounded-md border-gray-300 px-3 py-2 text-sm font-mono uppercase" />
      </label>

      <div className="mt-6 flex justify-end gap-3">
        <button onClick={() => navigate(-1)}
          className="rounded-md border border-gray-300 bg-white px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50">
          Cancel
        </button>
        <button onClick={onSubmit} disabled={token !== "SIGN" || sign.isPending}
          className="rounded-md bg-orange-600 px-4 py-2 text-sm font-medium text-white hover:bg-orange-700 disabled:opacity-50">
          Sign and Lock
        </button>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Verify + commit**

```bash
npm test -- --run MdFinalSignPage
git add bom-web/src/features/approvals/MdFinalSignPage.tsx bom-web/src/features/approvals/MdFinalSignPage.test.tsx
git commit -m "feat(v3-web): add MdFinalSignPage with type-to-confirm SIGN

MD Stage 2 final sign. Submit button disabled until 'SIGN' typed.
Successful submit transitions to Signed (terminal/locked).

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 14: ProfileSignaturePage

**Files:**
- Create: `bom-web/src/features/profile/ProfileSignaturePage.tsx`
- Create: `bom-web/src/features/profile/ProfileSignaturePage.test.tsx`

MD-only page to upload signature image (one-time + replace).

- [ ] **Step 1: Test**

```tsx
// bom-web/src/features/profile/ProfileSignaturePage.test.tsx
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { ProfileSignaturePage } from "./ProfileSignaturePage";
import { axios } from "../../api/axios";

vi.mock("../../api/axios", () => ({ axios: { get: vi.fn(), post: vi.fn() } }));

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ProfileSignaturePage />
      </MemoryRouter>
    </QueryClientProvider>
  );
}

describe("ProfileSignaturePage", () => {
  beforeEach(() => {
    vi.mocked(axios.post).mockReset();
  });

  it("uploads file via multipart and shows success", async () => {
    vi.mocked(axios.post).mockResolvedValue({
      data: { path: "/data/signatures/5.png", uploadedAt: "2026-04-29T00:00:00Z" },
    });

    renderPage();

    const file = new File(["png-bytes"], "sig.png", { type: "image/png" });
    const input = screen.getByLabelText(/upload signature/i) as HTMLInputElement;
    await userEvent.upload(input, file);
    await userEvent.click(screen.getByRole("button", { name: /upload/i }));

    await waitFor(() => expect(axios.post).toHaveBeenCalledWith(
      "/api/profile/signature",
      expect.any(FormData),
      expect.objectContaining({ headers: expect.objectContaining({ "Content-Type": "multipart/form-data" }) })
    ));
  });
});
```

- [ ] **Step 2: Test fails**

```bash
npm test -- --run ProfileSignaturePage
```

- [ ] **Step 3: Implement**

```tsx
// bom-web/src/features/profile/ProfileSignaturePage.tsx
import { useState } from "react";
import { toast } from "sonner";
import { useUploadSignature } from "../../api/queries/profile";

export function ProfileSignaturePage() {
  const [file, setFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const upload = useUploadSignature();

  const onFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const f = e.target.files?.[0] ?? null;
    setFile(f);
    if (f) setPreviewUrl(URL.createObjectURL(f));
  };

  const onUpload = async () => {
    if (!file) { toast.error("Pick a file first"); return; }
    if (file.size > 500 * 1024) { toast.error("File too large (max 500KB)"); return; }
    try {
      await upload.mutateAsync(file);
      toast.success("Signature uploaded");
    } catch (err: any) {
      toast.error(err?.response?.data?.error ?? "Upload failed");
    }
  };

  return (
    <div className="mx-auto max-w-2xl p-6">
      <h1 className="text-2xl font-semibold text-gray-900">Profile · Signature</h1>
      <p className="mt-1 text-sm text-gray-500">
        Uploaded signature appears on signed quotation PDFs. PNG/JPG ≤ 500KB. ~600×200px recommended.
      </p>

      <div className="mt-6 rounded-lg border border-gray-200 bg-white p-6">
        <h2 className="text-sm font-medium text-gray-700">Current signature</h2>
        <div className="mt-2 flex h-24 items-center justify-center rounded-md border border-dashed border-gray-300 bg-gray-50">
          <img src="/api/profile/signature" alt="Current signature" className="max-h-20"
            onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = "none"; }} />
          <span className="text-xs text-gray-400">[no signature uploaded]</span>
        </div>

        <h2 className="mt-6 text-sm font-medium text-gray-700">Upload new</h2>
        <input type="file" accept="image/png,image/jpeg" onChange={onFileChange} aria-label="upload signature"
          className="mt-2 block w-full rounded-md border-gray-300 px-3 py-2 text-sm" />

        {previewUrl && (
          <div className="mt-3">
            <p className="text-xs text-gray-500">Preview:</p>
            <img src={previewUrl} alt="Preview" className="mt-1 max-h-24 rounded border border-gray-200" />
          </div>
        )}

        <button onClick={onUpload} disabled={!file || upload.isPending}
          className="mt-4 rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
          {upload.isPending ? "Uploading…" : "Upload"}
        </button>
      </div>
    </div>
  );
}
```

- [ ] **Step 4: Verify + commit**

```bash
npm test -- --run ProfileSignaturePage
git add bom-web/src/features/profile/ProfileSignaturePage.tsx bom-web/src/features/profile/ProfileSignaturePage.test.tsx
git commit -m "feat(v3-web): add ProfileSignaturePage

MD-only signature upload + replace flow. Preview before save. Files
≤500KB; PNG/JPEG. Embedded into signed quotation PDFs by backend.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 15: Wire new routes into the app

**Files:**
- Modify: `bom-web/src/App.tsx` (or wherever routes are declared — verify path)

- [ ] **Step 1: Find the router root**

```bash
grep -rl "<Routes>" bom-web/src/
```

Expected: a single file (likely `App.tsx`) with the route table.

- [ ] **Step 2: Add the V3 routes**

Inside the `<Routes>` block (preserving existing routes):

```tsx
<Route path="/requisitions/:id/customer-confirm" element={<CustomerConfirmPage />} />
<Route path="/approvals/:id/margin" element={<MdMarginPage />} />
<Route path="/approvals/:id/final" element={<MdFinalSignPage />} />
<Route path="/profile/signature" element={<ProfileSignaturePage />} />
```

Imports:

```tsx
import { CustomerConfirmPage } from "./features/requisitions/CustomerConfirmPage";
import { MdMarginPage } from "./features/approvals/MdMarginPage";
import { MdFinalSignPage } from "./features/approvals/MdFinalSignPage";
import { ProfileSignaturePage } from "./features/profile/ProfileSignaturePage";
```

- [ ] **Step 3: Verify tsc**

```bash
npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/App.tsx
git commit -m "feat(v3-web): wire V3 routes (customer-confirm, margin, final, signature)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 16: RequisitionDetailPage updates

**Files:**
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx`

Adapt to V3: new state-machine action buttons, inline BOM (no separate tab), diff badges if accountant edited BOM.

- [ ] **Step 1: Update the test (add V3 cases)**

Add tests:
- "renders Submit + Cancel buttons when status=Draft"
- "renders Set Margin button when status=MdPricing and role=ManagingDirector"
- "renders Customer Confirm link when status=CustomerConfirm and role=SalesPerson"
- "renders Sign Final link when status=MdFinalSign and role=ManagingDirector"
- "renders Download PDF when status=Signed"

- [ ] **Step 2: Test fails**

```bash
npm test -- --run RequisitionDetailPage
```

- [ ] **Step 3: Update the page**

Key edits in `RequisitionDetailPage.tsx`:
- Use `V3StatusBadge` instead of inline status text
- Replace V2.3 action buttons with V3 set:
  - `Draft` (sales/admin own): Submit + Cancel + Edit (navigate to NewRequisitionPage with `?id=`)
  - `Costing` (accountant): Edit BOM + Submit Costing (navigate to existing `/costing/{id}` page)
  - `MdPricing` (MD): "Set Margin" → navigate `/approvals/{id}/margin`
  - `CustomerConfirm` (sales/admin own): "Confirm with Customer" → navigate `/requisitions/{id}/customer-confirm`
  - `MdFinalSign` (MD): "Sign Final" → navigate `/approvals/{id}/final`
  - `Signed`: Show "Download PDF" link to `/api/approvals/{id}/pdf`
- Inline BOM section: render `<BomEditorTable lines={...} readOnly />` per FG (always read-only on detail page)
- Diff badges: if `bomLine.lastModifiedByUserId !== null`, show "Edited by accountant" badge next to that line

(Detailed code omitted for brevity — implementer adapts existing 271-line page; expect ~150 lines of net additions/replacements. See spec §8.4 for full intent.)

- [ ] **Step 4: Verify**

```bash
npm test -- --run RequisitionDetailPage
```

- [ ] **Step 5: Commit**

```bash
git add bom-web/src/features/requisitions/RequisitionDetailPage.tsx bom-web/src/features/requisitions/RequisitionDetailPage.test.tsx
git commit -m "feat(v3-web): adapt RequisitionDetailPage for V3 state machine

V3 action buttons per status; inline BOM section (no separate tab);
'Edited by accountant' diff badges; Download PDF on Signed.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 17: CustomersPage / ItemsPage Code-readonly

**Files:**
- Modify: `bom-web/src/features/customers/CustomerListPage.tsx`
- Modify: `bom-web/src/features/items/ItemListPage.tsx`

The Code column is now server-generated and immutable.

- [ ] **Step 1: Customer list — remove Code from inline-edit columns; add preview-on-create**

Inline edits via existing modal/forms: replace the editable `code` field with:
- A read-only display in detail/list view
- A "Code: <auto-generated on save>" placeholder in create form

Create flow now uses `<CreateCustomerModal>` (from Task 6) which already handles this.

- [ ] **Step 2: Same edits to ItemListPage**

`<CreateFinishedGoodModal>` and `<CreateRawMaterialModal>` (Tasks 7-8) handle item creation.

- [ ] **Step 3: Verify tsc + tests**

```bash
npx tsc --noEmit
npm test -- --run "CustomerListPage|ItemListPage"
```

- [ ] **Step 4: Commit**

```bash
git add bom-web/src/features/customers/CustomerListPage.tsx bom-web/src/features/items/ItemListPage.tsx
git commit -m "feat(v3-web): mark Customer.code + Item.code read-only on list pages

Auto-generated server-side (CUST-XXXX / FG-XXXX / RM-XXXX). Inline create
modals show preview placeholder; users no longer type codes.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 18: Dashboard refactor

**Files:**
- Modify: `bom-web/src/features/dashboard/SalesDashboard.tsx`
- Modify: `bom-web/src/features/dashboard/AccountantDashboard.tsx`
- Modify: `bom-web/src/features/dashboard/MdDashboard.tsx`
- Modify: `bom-web/src/features/dashboard/DashboardRouter.tsx`
- Delete: `bom-web/src/features/dashboard/BomDashboard.tsx`

Drop V2.3 widgets; add V3 queues.

- [ ] **Step 1: SalesDashboard**

Replace V2.3 status filter widgets with V3 ones:
- "Drafts" tab (status=Draft)
- "In Costing" (status=Costing) — accountant working
- "Awaiting MD" (status=MdPricing,MdFinalSign)
- "Awaiting Customer Confirm" (status=CustomerConfirm) ← sales' actionable queue
- "Signed/Done" (status=Signed)
- "Cancelled/Rejected" (status=Cancelled,Rejected)

- [ ] **Step 2: AccountantDashboard**

V3 widgets:
- "Pending Costing" (status=Costing) ← actionable queue
- "Submitted (this month)" (status>=MdPricing && updatedAt>=startOfMonth)
- "Awaiting MD" (status=MdPricing,MdFinalSign)

- [ ] **Step 3: MdDashboard**

V3 widgets:
- "Awaiting Pricing" (status=MdPricing) ← actionable queue 1
- "Awaiting Final Sign" (status=MdFinalSign) ← actionable queue 2
- "Signed (this month)" (status=Signed && approvedAt>=startOfMonth)

- [ ] **Step 4: DashboardRouter — drop BomCreator case**

```tsx
// dashboard/DashboardRouter.tsx
const role = user?.role;
if (role === "SalesPerson") return <SalesDashboard />;
if (role === "Accountant") return <AccountantDashboard />;
if (role === "ManagingDirector") return <MdDashboard />;
if (role === "Admin") return <AdminDashboard />;
return null; // BomCreator role deprecated; nothing to render
```

- [ ] **Step 5: Delete BomDashboard.tsx**

```bash
git rm bom-web/src/features/dashboard/BomDashboard.tsx
```

- [ ] **Step 6: Verify + commit**

```bash
npx tsc --noEmit
npm test -- --run Dashboard
git add -A
git commit -m "feat(v3-web): refactor dashboards for V3 state machine

- Sales: drop BomPending/BomInProgress; add CustomerConfirm + Drafts queues
- Accountant: V3 Costing queue; submitted-this-month aggregates V3 statuses
- MD: split Awaiting Pricing + Awaiting Final Sign queues
- Delete BomDashboard (BomCreator role going away)
- Route table drops BomCreator branch

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 19: AdminAuditLogPage filter additions

**Files:**
- Modify: `bom-web/src/features/admin/audit-log/AuditLogPage.tsx`

Add V3 ActionType + Status filter values.

- [ ] **Step 1: Add filter values**

In the existing dropdowns:

ActionType options (additions): `RollbackToCosting`, `V3CutoverMigration`. Keep V2.3 values.

Status options (additions): `Draft`, `Costing`, `MdPricing`, `CustomerConfirm`, `MdFinalSign`, `Signed`, `Cancelled`. Keep V2.3 values for historical row filtering.

- [ ] **Step 2: Verify tsc**

```bash
npx tsc --noEmit
```

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/features/admin/audit-log/AuditLogPage.tsx
git commit -m "feat(v3-web): extend AuditLogPage filters with V3 ActionTypes + Statuses

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 20: Delete obsolete pages

**Files:**
- Delete: `bom-web/src/features/bom/BomEntryPage.tsx`
- Delete: `bom-web/src/features/bom/BomEntryPage.test.tsx` (if exists)
- Delete: `bom-web/src/features/approvals/MdReviewPage.tsx`
- Delete: `bom-web/src/features/approvals/MdReviewPage.test.tsx`
- Delete: `bom-web/src/features/requisitions/EditRequisitionPage.tsx`
- Delete: `bom-web/src/features/requisitions/EditRequisitionPage.test.tsx`
- Delete: `bom-web/src/features/requisitions/BranchSwapModal.tsx`
- Delete: `bom-web/src/features/requisitions/BranchSwapModal.test.tsx`
- Delete: `bom-web/src/features/requisitions/BranchChangeHistoryModal.tsx`
- Delete: `bom-web/src/features/requisitions/ChangeCustomerModal.tsx`
- Delete: `bom-web/src/features/requisitions/CustomerHistoryModal.tsx`

Also remove imports + routes referencing these pages from `App.tsx`.

- [ ] **Step 1: Remove files**

```bash
git rm bom-web/src/features/bom/BomEntryPage.tsx bom-web/src/features/bom/BomEntryPage.test.tsx
git rm bom-web/src/features/approvals/MdReviewPage.tsx bom-web/src/features/approvals/MdReviewPage.test.tsx
git rm bom-web/src/features/requisitions/EditRequisitionPage.tsx bom-web/src/features/requisitions/EditRequisitionPage.test.tsx
git rm bom-web/src/features/requisitions/BranchSwapModal.tsx bom-web/src/features/requisitions/BranchSwapModal.test.tsx
git rm bom-web/src/features/requisitions/BranchChangeHistoryModal.tsx
git rm bom-web/src/features/requisitions/ChangeCustomerModal.tsx
git rm bom-web/src/features/requisitions/CustomerHistoryModal.tsx
```

- [ ] **Step 2: Remove import statements + routes** in `App.tsx` for the deleted pages.

- [ ] **Step 3: Verify tsc clean**

```bash
npx tsc --noEmit
```

If any "module not found" errors in other files: trace the dependent + adapt or delete it (some V2.3 callers may also need cleanup).

- [ ] **Step 4: Run all tests**

```bash
npm test -- --run
```

Expected: previous tests for deleted components are gone; remaining tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(v3-web): delete V2.3 obsolete pages

- BomEntryPage (no BOM stage)
- MdReviewPage (split into MdMarginPage + MdFinalSignPage)
- EditRequisitionPage (V3 Drafts edit via NewRequisitionPage)
- BranchSwapModal, BranchChangeHistoryModal (V3 = Alain only)
- ChangeCustomerModal, CustomerHistoryModal (customer immutable post-Create)

App.tsx routes + imports cleaned up to match.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 21: PWA service worker cache bump

**Files:**
- Modify: `bom-web/src/sw.ts`

Force previous V2.3 PWA users to fetch fresh data after deploy.

- [ ] **Step 1: Bump cache name suffix**

Find each `cacheName` literal (e.g., `"bom-api-list-cache"`) and change suffix:

```typescript
const LIST_CACHE = "bom-api-list-cache-v3"; // was: bom-api-list-cache
const DETAIL_CACHE = "bom-api-detail-cache-v3"; // was: bom-api-detail-cache
```

- [ ] **Step 2: Verify build**

```bash
npm run build
```

Expected: clean. Check `dist/sw.js` for the new cache names.

- [ ] **Step 3: Commit**

```bash
git add bom-web/src/sw.ts
git commit -m "feat(v3-web): bump PWA cache name suffix to -v3

Forces previous V2.3 PWA users to invalidate cached API responses on
first V3 load. No new SW strategies needed.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 22: Run full test suite

**Files:** None modified.

- [ ] **Step 1: Build**

```bash
cd .claude/worktrees/v3-phase-b/bom-web
npx tsc --noEmit
npm run build
```

Expected: 0 errors.

- [ ] **Step 2: Run all tests**

```bash
npm test -- --run
```

Expected: all tests pass. Total may have changed (some V2.3 tests deleted in Task 20, V3 tests added in Tasks 5–14).

- [ ] **Step 3: Inspect dist for sanity**

```bash
ls -la dist/
ls -la dist/assets/ | head -10
```

Should see `index.html`, `sw.js`, `manifest.webmanifest`, `assets/index-*.js`, etc.

- [ ] **Step 4: No commit**

---

## Task 23: Manual smoke against local backend

**Files:** None modified.

This requires a running local backend (V3 backend from Phase A — master `ea1a904`) and a fresh local DB.

- [ ] **Step 1: Start backend in another terminal** (NOT in the worktree dir):

```bash
cd D:\shan\ projects\BOM_Price_Approval
dotnet ef database update --project BomPriceApproval.API
dotnet run --project BomPriceApproval.API
```

Wait for `Now listening on: http://localhost:7300`.

- [ ] **Step 2: Start frontend dev server**

```bash
cd .claude/worktrees/v3-phase-b/bom-web
npm run dev
```

Wait for `Local: http://localhost:5300/`.

- [ ] **Step 3: Walk the V3 happy path manually in browser**

1. Open http://localhost:5300, log in as `ali@test.com / Test@1234`
2. Click "+ New Requisition"
3. Pick existing customer OR click "+ New" → fill modal → save (verify CUST-XXXX code appears)
4. Pick currency USD
5. "+ Add Finished Good" → pick existing FG OR click "+ New FG" → fill modal → save (verify FG-XXXX)
6. Enter quantity 5000, check Printing
7. In BOM table: "+ Add Raw Material" → pick existing OR "+ Create new RM" → enter qty/KG=0.44, micron=20
8. Notes: "Smoke test"
9. Click "Submit"
10. Should land on `/requisitions/{id}` with status=Costing
11. Log out → log in as `sara@test.com / Test@1234`
12. Open the req from dashboard → status=Costing → "Edit BOM" + Costing entry (existing flow)
13. Submit costing → status=MdPricing
14. Log in as `md@test.com / Test@1234` → req appears in "Awaiting Pricing"
15. Open → "Set Margin" → enter 0.50 USD/KG → Submit → status=CustomerConfirm
16. Log in as `ali@test.com` → req in "Awaiting Customer Confirm"
17. Open → "Confirm with Customer" → click "Customer Accepted" → status=MdFinalSign
18. Log in as `md@test.com` → "Awaiting Final Sign" queue
19. Open → "Sign Final" → type "SIGN" → click "Sign and Lock" → status=Signed
20. Click "Download PDF" → verify PDF downloads with signature placeholder (or real sig if uploaded via /profile/signature)

- [ ] **Step 4: No commit (smoke is verification only)**

If any step fails, fix the underlying issue before opening PR.

---

## Task 24: Push branch + open PR

**Files:** None.

- [ ] **Step 1: Verify clean state**

```bash
cd .claude/worktrees/v3-phase-b/bom-web
git status
```

Expected: `nothing to commit, working tree clean`.

- [ ] **Step 2: Final test run + build**

```bash
npx tsc --noEmit
npm run build
npm test -- --run
```

All green.

- [ ] **Step 3: Push branch**

```bash
git push -u origin feat/v3-phase-b-frontend
```

- [ ] **Step 4: Open PR**

```bash
gh pr create --base master --head feat/v3-phase-b-frontend \
  --title "feat(v3-web): Phase B frontend rewrite — V3 simplified workflow" \
  --body "$(cat <<'EOF'
## Summary

Phase B of the V3 simplified workflow — frontend half. Backend (Phase A) merged at master \`ea1a904\`. Phase C cutover SQL ships separately.

## What ships

**New pages (4):**
- \`NewRequisitionPage\` — major rewrite for combined sales+BOM screen
- \`CustomerConfirmPage\` — sales accept/reject MD price
- \`MdMarginPage\` — Stage 1 (margin entry)
- \`MdFinalSignPage\` — Stage 2 (type-to-confirm SIGN)
- \`ProfileSignaturePage\` — MD signature upload

**New components (5):**
- \`<BomEditorTable>\` — shared inline BOM editor (editable + read-only modes)
- \`<CreateCustomerModal>\` / \`<CreateFinishedGoodModal>\` / \`<CreateRawMaterialModal>\`
- \`<V3StatusBadge>\` — color-coded status pill

**Updates:**
- \`RequisitionDetailPage\` — V3 state-machine actions, inline BOM, diff badges
- \`CustomersPage\` / \`ItemsPage\` — Code column read-only
- All dashboards — V3 queues
- \`AdminAuditLogPage\` — V3 ActionType + Status filters
- PWA service worker cache bumped to \`-v3\`

**Deletions:**
- \`BomEntryPage\` (no BOM stage)
- \`MdReviewPage\` (split)
- \`EditRequisitionPage\`, \`BranchSwapModal\`, \`BranchChangeHistoryModal\`, \`ChangeCustomerModal\`, \`CustomerHistoryModal\`
- \`BomDashboard\` (BomCreator role going away)

## Out of scope

- Phase C cutover SQL (separate PR)
- Phase D mobile (deferred)

## Test plan

- [x] \`npx tsc --noEmit\` — 0 errors
- [x] \`npm run build\` — clean
- [x] \`npm test\` — all green
- [x] Manual smoke walked Draft → Signed end-to-end against local backend

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

- [ ] **Step 5: Auto-merge (per CLAUDE.md auto-merge rule, post-PR-30)**

Wait for CI green:

```bash
until ! gh pr checks <PR-NUMBER> 2>/dev/null | awk -F'\t' '{print $2}' | grep -q pending; do sleep 25; done
gh pr checks <PR-NUMBER>
```

If all pass + no \`hold\` label:

```bash
gh pr merge <PR-NUMBER> --squash --delete-branch
```

- [ ] **Step 6: Cleanup worktree**

```bash
cd D:\shan\ projects\BOM_Price_Approval
git worktree remove .claude/worktrees/v3-phase-b
git worktree prune
git worktree list
```

Expected: only the main worktree remains.

---

## Definition of Done

- All 263+ existing web tests still green where applicable; obsolete tests deleted with clear deletion-commit reference
- 30+ new V3 web tests added (one per new page/component)
- \`npx tsc --noEmit\` clean
- \`npm run build\` clean (Vite dist emits sw.js, manifest, icons)
- Manual happy path Draft → Signed walked end-to-end against the V3 backend
- PR opened, CI green, auto-merged to master
- Worktree cleaned up

---

## Notes for the executor

- This plan assumes Phase A backend is the canonical contract. If a backend response shape differs from `types/api.ts` definitions in this plan, **trust the backend** (it's the merged truth) and adjust types accordingly.
- The `<BomEditorTable>` is reused in editable mode (Task 9 in NewRequisitionPage) and read-only mode (Task 16 in RequisitionDetailPage). Don't duplicate — the `readOnly` prop is the toggle.
- `RequisitionDetailPage` (Task 16) is the most subtle update: it's an EXISTING 271-line page that needs surgical edits, not a rewrite. Add V3 status actions, swap V2.3-only sections for inline BOM, leave the historical-data display paths intact for Approved (legacy V2.3) reqs.
- The PDF download URL is `/api/approvals/{id}/pdf` (existing V2.3 endpoint reused). The Signed status's "Download PDF" button is just a `<a href>` to that URL.
- `<a href="/api/approvals/...pdf" download>` should work (axios baseURL is set to API host so a relative `<a>` link from React Router page goes through the same proxy in dev).
- If the spec gap from Phase A's Tasks 23-25 (V3 cost-component fields) hits the UI (e.g., `costs.fohPerKg` showing as 0 because accountant input UI doesn't exist yet), document as a follow-up task — Phase B does NOT include extending the costing-input UI for the new V3 BomCost fields. That's a separate concern.
