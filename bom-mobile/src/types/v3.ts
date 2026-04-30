export type V3Status =
  | "Draft" | "Costing" | "MdPricing" | "CustomerConfirm"
  | "MdFinalSign" | "Signed" | "Rejected" | "Cancelled";

export interface V3Customer {
  id: number; code: string; name: string;
  email?: string | null; phone?: string | null; address?: string | null;
}

export interface V3SalesPerson {
  id: number; name: string; email: string;
}

export interface V3BomLine {
  id?: number; processId: number; processName?: string;
  rawMaterialItemId: number; rawMaterialDescription?: string;
  qtyPerKg: number; wastagePct: number;
}

export interface V3FinishedGood {
  id?: number; itemId: number; code?: string; description?: string;
  expectedQty: number;
  bomLines: V3BomLine[];
  costs?: { foh?: number; transport?: number; commission?: number } | null;
}

export interface V3Requisition {
  id: number; refNo: string; status: V3Status; statusInt: number;
  branchId: number; branchName?: string;
  currencyCode: string; referenceNumber?: string | null; notes?: string | null;
  customer: V3Customer;
  salesPerson: V3SalesPerson;
  finishedGoods: V3FinishedGood[];
  createdAt: string; updatedAt: string;
  cancelReason?: string | null;
  cancelledAt?: string | null;
  cancelledByUserId?: number | null;
  finalPrice?: { totalAed: number; perFg: { itemId: number; priceAed: number }[] } | null;
}

export interface V3RequisitionListItem {
  id: number; refNo: string; status: V3Status; statusInt: number;
  customerName: string; currencyCode: string;
  branchId: number; branchName: string;
  salesPersonId: number; salesPersonName: string;
  createdAt: string;
  fgCount: number;
}
