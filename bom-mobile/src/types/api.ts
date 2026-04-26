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
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface ApiError {
  message: string;
}

// ─── Plan 2: Requisitions & lookups ──────────────────────────────────────────

export type RequisitionStatus =
  | "Draft"
  | "BomPending"
  | "BomInProgress"
  | "CostingPending"
  | "CostingInProgress"
  | "MdReview"
  | "Approved"
  | "Rejected";

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

export interface ApprovalItemPrice {
  requisitionItemId: number;
  pricePerKg: number;
  pricePerKgForeign: number | null;
}

export interface ApprovalSummary {
  isApproved: boolean;
  notes: string | null;
  approvedAt: string;
  items: ApprovalItemPrice[] | null;
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
  branchId: number;
  customerId: number;
  items: RequisitionItemInput[];
  currencyCode: string;
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

// Request body shapes (used by Accountant V2.1 mobile hooks).
export interface RawMaterialCostInput {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
}

export interface SaveCostingDraftRequest {
  lines: RawMaterialCostInput[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
}

export interface SubmitCostingRequest {
  rawMaterialCosts: RawMaterialCostInput[];
  landedCostType: LandedCostType;
  landedCostValue: number;
  fohAmount: number;
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

export interface AccountantDashboardStats {
  pendingCosting: number;
  inProgress: number;
  submittedThisMonth: number;
  awaitingMd: number;
}

// ─── Customer change (Feature X) ─────────────────────────────────────────────

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
  changedAt: string; // ISO datetime
  reason: string | null;
}
