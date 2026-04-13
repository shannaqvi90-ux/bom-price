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
  name: string;
  address: string;
  email: string;
  phoneNumber: string;
  branchId: number;
  createdByUserId: number;
}

export type ItemKind = "FinishedGood" | "RawMaterial";

export interface Item {
  id: number;
  code: string;
  description: string;
  type: ItemKind;
  branchId: number;
  isActive: boolean;
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
