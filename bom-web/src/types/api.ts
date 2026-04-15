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
  itemDescription: string;
  customerName: string;
  expectedQty: number;
  currencyCode: string;
  branchName: string;
  salesPersonName: string;
  createdAt: string;
}

export interface BomSummary {
  id: number;
  totalCostPerKg: number;
  hasCost: boolean;
}

export interface ApprovalSummary {
  salesPriceAed: number;
  salesPriceForeign: number | null;
  profitMarginPct: number;
  isApproved: boolean;
}

export interface RequisitionDetail {
  id: number;
  refNo: string;
  status: RequisitionStatus;
  itemId: number;
  itemDescription: string;
  customerId: number;
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  customerAddress: string;
  expectedQty: number;
  currencyCode: string;
  exchangeRateSnapshot: number | null;
  branchId: number;
  branchName: string;
  salesPersonId: number;
  salesPersonName: string;
  createdAt: string;
  updatedAt: string;
  bom: BomSummary | null;
  approval: ApprovalSummary | null;
}

export interface CreateRequisitionRequest {
  customerId: number;
  itemId: number;
  expectedQty: number;
  currencyCode: string;
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

export interface BomDetail {
  id: number;
  quotationRequestId: number;
  refNo: string;
  itemDescription: string;
  lines: BomLine[];
  totalCostPerKg: number;
  submittedAt: string | null;
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

export interface CostingDetail {
  id: number;
  rawMaterialCostTotal: number;
  landedCostType: string;
  landedCostValue: number;
  fohAmount: number;
  totalCostPerKg: number;
  submittedAt: string | null;
  bomLines: CostingBomLine[];
  draft: CostingDraft | null;
}

// ─── MD Review ───────────────────────────────────────────────────────────────

export interface MdReviewDetail {
  refNo: string;
  itemDescription: string;
  customerName: string;
  expectedQty: number;
  currencyCode: string;
  exchangeRate: number | null;
  rawMaterialCostPerKg: number;
  landedCostPerKg: number;
  fohPerKg: number;
  totalCostPerKg: number;
  materialCostPct: number;
  landedCostPct: number;
  fohPct: number;
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
  branchId: null;
}

export interface UpdateUserRequest {
  name: string;
  email: string;
  role: UserRole;
  branchId: null;
  isActive: boolean;
}
