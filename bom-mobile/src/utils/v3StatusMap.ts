import type { V3Status } from "../types/v3";

export type ListTab = "active" | "done" | "closed";

export const STATUS_TO_TAB: Record<V3Status, ListTab> = {
  Draft: "active", Costing: "active", MdPricing: "active",
  CustomerConfirm: "active", MdFinalSign: "active",
  Signed: "done",
  Rejected: "closed", Cancelled: "closed",
};

export const STATUSES_BY_TAB: Record<ListTab, V3Status[]> = {
  active: ["Draft", "Costing", "MdPricing", "CustomerConfirm", "MdFinalSign"],
  done: ["Signed"],
  closed: ["Rejected", "Cancelled"],
};

export const STATUS_COLOR: Record<V3Status, string> = {
  Draft: "#6b7280", Costing: "#f59e0b", MdPricing: "#3b82f6",
  CustomerConfirm: "#6366f1", MdFinalSign: "#8b5cf6",
  Signed: "#10b981", Rejected: "#ef4444", Cancelled: "#475569",
};

export const STATUS_LABEL: Record<V3Status, string> = {
  Draft: "Draft", Costing: "Costing", MdPricing: "MD Pricing",
  CustomerConfirm: "Customer Confirm", MdFinalSign: "MD Final Sign",
  Signed: "Signed", Rejected: "Rejected", Cancelled: "Cancelled",
};
