export type UserRole =
  | "SalesPerson"
  | "BomCreator"
  | "Accountant"
  | "ManagingDirector"
  | "Admin";

export interface AuthUser {
  userId: number;
  name: string;
  role: UserRole;
  branchId: number | null;
  mustChangePassword: boolean;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  role: UserRole;
  userId: number;
  name: string;
  branchId: number | null;
  mustChangePassword: boolean;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface ApiError {
  message: string;
}

// ─── Plan 2: Requisitions & lookups ──────────────────────────────────────────

export interface RequisitionListItem {
  id: number;
  refNo: string;
  status: RequisitionStatus;
  itemCount: number;
  customerName: string;
  currencyCode: string;
  branchName: string;
  salesPersonId: number;
  salesPersonName: string;
  createdAt: string;
}

export interface RequisitionItemDto {
  id: number;
  itemId: number;
  itemDescription: string;
  expectedQty: number;
  sortOrder: number;
}

export interface ApprovalSummary {
  isApproved: boolean;
  notes: string | null;
  approvedAt: string;
}

export interface RequisitionDetail {
  id: number;
  refNo: string;
  status: RequisitionStatus;
  customerId: number;
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  customerAddress: string;
  currencyCode: string;
  exchangeRateSnapshot: number | null;
  branchId: number;
  branchName: string;
  salesPersonId: number;
  salesPersonName: string;
  createdAt: string;
  updatedAt: string;
  items: RequisitionItemDto[];
  approval: ApprovalSummary | null;
}

export interface RequisitionItemInput {
  itemId: number;
  expectedQty: number;
}

export interface CreateRequisitionRequest {
  customerId: number;
  items: RequisitionItemInput[];
  currencyCode: string;
  branchId?: number | null;
}

export interface AddRequisitionItemRequest {
  itemId: number;
  expectedQty: number;
}

export interface Customer {
  id: number;
  code: string;
  name: string;
  address: string;
  email: string;
  phoneNumber: string;
  salesPersonId: number | null;
  salesPersonName: string | null;
  createdByUserId: number;
}

export interface CreateCustomerRequest {
  code: string;
  name: string;
  address: string;
  email: string;
  phoneNumber: string;
}

export interface ImportResult {
  imported: number;
  skipped: number;
  errors: string[];
}

export type ItemKind = "FinishedGood" | "RawMaterial";

export interface Item {
  id: number;
  code: string;
  description: string;
  type: ItemKind;
  branchId: number;
  isActive: boolean;
  lastPurchasePrice: number | null;
}

export interface CreateItemRequest {
  code: string;
  description: string;
  type: ItemKind;
  lastPurchasePrice: number | null;
  branchId?: number | null;
}

export interface UpdateItemRequest {
  code: string;
  description: string;
  type: ItemKind;
  lastPurchasePrice: number | null;
}

export interface LedgerHeadersResponse {
  headers: string[];
}

export interface LedgerImportResult {
  updated: number;
  skipped: number;
  unmatchedCodes: string[];
}

export interface ExchangeRate {
  id: number;
  currencyCode: string;
  currencyName: string;
  rateToAed: number;
  effectiveDate: string;
  isActive: boolean;
  setByName: string;
}

export interface CreateExchangeRateRequest {
  currencyCode: string;
  currencyName: string;
  rateToAed: number;
  effectiveDate: string;
}

export interface UpdateExchangeRateRequest {
  rateToAed: number;
  effectiveDate: string;
  isActive: boolean;
}

// ─── BOM Entry ────────────────────────────────────────────────────────────────

export interface Process {
  id: number;
  name: string;
  displayOrder: number;
  isActive: boolean;
}

export interface BomLine {
  id: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  costPerKg: number | null;
  currencyCode: string | null;
  costPerKgInAed: number | null;
  contributionAed: number | null;
}

export interface BomItemResponse {
  requisitionItemId: number;
  itemId: number;
  itemDescription: string;
  expectedQty: number;
  sortOrder: number;
  bomHeaderId: number | null;
  bomStatus: "NotStarted" | "InProgress" | "Submitted";
  lines: BomLine[];
  totalCostPerKg: number;
  submittedAt: string | null;
}

export interface BomReviewResponse {
  requisitionId: number;
  refNo: string;
  requisitionStatus: string;
  items: BomItemResponse[];
}

// ─── Costing Entry ────────────────────────────────────────────────────────────

export interface LastCostInfo {
  costPerKg: number;
  currencyCode: string;
  updatedAt: string;
}

export interface CostingBomLine {
  bomLineId: number;
  processId: number;
  processName: string;
  rawMaterialItemId: number;
  rawMaterialDescription: string;
  qtyPerKg: number;
  wastagePct: number;
  lastCost: LastCostInfo | null;
}

export type LandedCostType = "Percentage" | "FixedValue";

export interface CostingDraftLine {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
}

export interface CostingDraft {
  lines: CostingDraftLine[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
}

export interface CostingSummary {
  id: number;
  rawMaterialCostTotal: number;
  landedCostType: string;
  landedCostValue: number;
  fohAmount: number;
  totalCostPerKg: number;
  submittedAt: string | null;
}

export interface CostingItemResponse {
  requisitionItemId: number;
  itemId: number;
  itemDescription: string;
  expectedQty: number;
  bomHeaderId: number | null;
  costStatus: "NotStarted" | "InProgress" | "Submitted";
  cost: CostingSummary | null;
  bomLines: CostingBomLine[];
  draft: CostingDraft | null;
}

export interface CostingReviewResponse {
  requisitionId: number;
  items: CostingItemResponse[];
}

// ─── MD Review ───────────────────────────────────────────────────────────────

export interface MdReviewItemCost {
  rawMaterialCostPerKg: number;
  landedCostPerKg: number;
  fohPerKg: number;
  totalCostPerKg: number;
  materialCostPct: number;
  landedCostPct: number;
  fohPct: number;
}

export interface MdReviewItemDetail {
  requisitionItemId: number;
  itemDescription: string;
  expectedQty: number;
  costStatus: "NotStarted" | "InProgress" | "Submitted";
  cost: MdReviewItemCost | null;
}

export interface MdReviewDetail {
  refNo: string;
  customerName: string;
  currencyCode: string;
  exchangeRate: number | null;
  readyForReview: boolean;
  items: MdReviewItemDetail[];
}

// ─── Notifications ────────────────────────────────────────────────────────────────

export interface Notification {
  id: number;
  message: string;
  referenceId: number;
  referenceType: string;
  isRead: boolean;
  createdAt: string;
}

// ─── Users ───────────────────────────────────────────────────────────────────

export interface User {
  id: number;
  name: string;
  email: string;
  role: UserRole;
  branchId: number | null;
  branchName: string | null;
  isActive: boolean;
}

export interface CreateUserRequest {
  name: string;
  email: string;
  password: string;
  role: UserRole;
  branchId: number | null;
}

export interface UpdateUserRequest {
  name: string;
  email: string;
  role: UserRole;
  branchId: number | null;
  isActive: boolean;
}

// ─── Customer change history (Phase 2/3 of customer-creation feature) ────────

export interface ChangeCustomerRequest {
  customerId: number;
  reason?: string | null;
}

export interface CustomerChangeHistoryEntry {
  id: number;
  oldCustomerId: number;
  oldCustomerName: string;
  newCustomerId: number;
  newCustomerName: string;
  changedByUserId: number;
  changedByUserName: string;
  changedAt: string;
  reason: string | null;
}

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
  totalCostPerKg: number;
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

export interface V3FinalPriceItem {
  requisitionItemId: number;
  itemId: number;
  description: string;
  expectedQty: number;
  costPerKg: number;
  marginPerKg: number;
  salePerKg: number;
  salePerKgAed: number;
  totalAed: number;
}

export interface V3FinalPrice {
  totalAed: number;
  currencyCode: string;
  rateSnapshot: number | null;
  perFg: V3FinalPriceItem[];
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
  // Populated once MdPricing locks margins (PR #54). Null/omitted before that.
  finalPrice?: V3FinalPrice | null;
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
