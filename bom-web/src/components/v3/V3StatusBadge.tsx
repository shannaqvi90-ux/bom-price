import type { RequisitionStatus } from "@/types/api";

interface Props {
  status: RequisitionStatus;
}

const STATUS_STYLES: Record<RequisitionStatus, string> = {
  // V3
  Draft: "bg-muted text-foreground",
  Costing: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  MdPricing: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  CustomerConfirm: "bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300",
  MdFinalSign: "bg-orange-100 text-orange-700 dark:bg-orange-900/40 dark:text-orange-300",
  Signed: "bg-green-100 text-green-700 dark:bg-emerald-900/40 dark:text-emerald-300",
  Cancelled: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",
  Rejected: "bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300",

  // Legacy V2
  BomPending: "bg-muted text-foreground",
  BomInProgress: "bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300",
  CostingPending: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  CostingInProgress: "bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300",
  MdReview: "bg-orange-100 text-orange-700 dark:bg-orange-900/40 dark:text-orange-300",
  Approved: "bg-green-100 text-green-700 dark:bg-emerald-900/40 dark:text-emerald-300",
};

export function V3StatusBadge({ status }: Props) {
  const styles = STATUS_STYLES[status] ?? "bg-muted text-foreground";
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${styles}`}>
      {status}
    </span>
  );
}
