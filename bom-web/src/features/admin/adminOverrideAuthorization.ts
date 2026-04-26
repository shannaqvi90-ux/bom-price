import type { RequisitionStatus } from "@/types/api";

const rollbackWhitelist: Partial<Record<RequisitionStatus, RequisitionStatus>> = {
  Approved: "MdReview",
  MdReview: "CostingPending",
  CostingInProgress: "CostingPending",
  CostingPending: "BomInProgress",
  BomInProgress: "BomPending",
};

export function rollbackTarget(from: RequisitionStatus): RequisitionStatus | null {
  return rollbackWhitelist[from] ?? null;
}

export function canRollback(from: RequisitionStatus): boolean {
  return rollbackTarget(from) !== null;
}

export function canUnlockBom(current: RequisitionStatus): boolean {
  return (["CostingPending", "CostingInProgress", "MdReview"] as RequisitionStatus[]).includes(current);
}

export function canUnlockCosting(current: RequisitionStatus): boolean {
  return current === "MdReview";
}

export function canDelete(): boolean {
  return true;
}

export function canReassignSp(): boolean {
  return true;
}
