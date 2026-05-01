export type V3Status =
  | "Draft" | "Costing" | "MdPricing" | "CustomerConfirm"
  | "MdFinalSign" | "Signed" | "Rejected" | "Cancelled";

// ===== GET response shapes (match backend V3RequisitionDetail / V3FinishedGoodDto / etc.) =====

export interface V3ItemSummary {
  id: number;
  code: string;
  description: string;
}

export interface V3CustomerSummary {
  id: number;
  name: string;
  code: string;
}

export interface V3SalesPersonSummary {
  id: number;
  name: string;
}

export interface V3BomLineDto {
  id: number;
  qtyPerKg: number;
  micron?: string | null;
  item: V3ItemSummary;
  lastModifiedByUserId?: number | null;
  lastModifiedAt?: string | null;
}

export interface V3BomCostLineDto {
  bomLineId: number;
  wastagePercent: number;
  purchaseValuePerKg?: number | null;
  purchaseCurrency?: string | null;
}

export interface V3BomCostDto {
  totalCostPerKg: number;
  printingCostPerKg?: number | null;
  printingCostCurrency?: string | null;
  fohPerKg: number;
  transportPerKg: number;
  commissionPerKg: number;
  lines: V3BomCostLineDto[];
}

export interface V3FinishedGoodDto {
  id: number;
  expectedQty: number;
  hasPrinting: boolean;
  item: V3ItemSummary;
  bomLines?: V3BomLineDto[] | null;
  costs?: V3BomCostDto | null;
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

export interface V3Requisition {
  id: number;
  refNo: string;
  status: V3Status;
  currencyCode: string;
  notes?: string | null;
  customer: V3CustomerSummary;
  salesPerson: V3SalesPersonSummary;
  finishedGoods: V3FinishedGoodDto[];
  cancelReason?: string | null;
  cancelledAt?: string | null;
  cancelledByUserId?: number | null;
  // V3 D-3 (post-PR #54): finalPrice now ships from backend on V3RequisitionDetail.
  // FinalPriceCard renders only when present (omitted/null until MdPricing locks margins).
  finalPrice?: V3FinalPrice | null;
}

export interface V3RequisitionListItem {
  id: number;
  refNo: string;
  status: V3Status;
  itemCount: number;          // backend sends ItemCount (was fgCount — renamed to match wire format)
  customerName: string;
  currencyCode: string;
  branchId: number;
  branchName: string;
  salesPersonId: number;
  salesPersonName: string;
  createdAt: string;
}

// ===== CREATE-flow editing state (mobile-side draft model) =====

export interface V3BomLineDraft {
  // Optional id present when editing an existing draft (Task 24); absent when newly added
  id?: number;
  processId: number;
  processName?: string;          // display-only, populated from ProcessPickerSheet
  rawMaterialItemId: number;
  rawMaterialDescription?: string; // display-only, populated from RmPickerSheet
  qtyPerKg: number;
  wastagePct: number;            // collected by UI; backend hard-codes to 0 on create per V3 design
}

export interface V3FinishedGoodDraft {
  id?: number;
  itemId: number;
  code?: string;                 // display-only from FgPickerSheet
  description?: string;          // display-only from FgPickerSheet
  expectedQty: number;
  bomLines: V3BomLineDraft[];
}

// ===== PUT /api/costing/{id}/cost-data INPUT types (request body shapes) =====

export interface V3RawMaterialCostInput {
  bomLineId: number;
  costPerKg: number;
  currencyCode: string;
}

export interface V3FgCostInput {
  requisitionItemId: number;
  rawMaterialCosts: V3RawMaterialCostInput[];
  printingCostPerKg: number | null;
  printingCostCurrency: string | null;
  fohPerKg: number;
  transportPerKg: number;
  commissionPerKg: number;
}

export interface SaveV3CostDataPayload {
  finishedGoods: V3FgCostInput[];
}

// ===== Stats endpoint shapes =====

export interface AccountantDashboardV3Stats {
  costing: number;
  awaitingMd: number;
  awaitingCustomer: number;
  submittedThisMonth: number;
}
