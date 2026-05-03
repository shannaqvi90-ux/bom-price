import { cn } from "@/lib/cn";
import type { RequisitionStatus } from "@/types/api";

const LABELS: Record<RequisitionStatus, string> = {
  // V3 statuses (current workflow):
  Draft: "Draft",
  Costing: "Costing",
  MdPricing: "MD Pricing",
  CustomerConfirm: "Customer Confirm",
  MdFinalSign: "MD Sign",
  Signed: "Signed",
  Cancelled: "Cancelled",
  Rejected: "Rejected",
  // Legacy V2.3 statuses (historical reqs only):
  BomPending: "BOM Pending",
  BomInProgress: "BOM In Progress",
  CostingPending: "Costing Pending",
  CostingInProgress: "Costing In Progress",
  MdReview: "MD Review",
  Approved: "Approved",
};

const COLOURS: Record<RequisitionStatus, string> = {
  // V3 statuses:
  Draft: "bg-slate-500/10 text-slate-600 dark:text-slate-300 ring-slate-500/20",
  Costing: "bg-amber-500/10 text-amber-600 dark:text-amber-400 ring-amber-500/20",
  MdPricing: "bg-blue-500/10 text-blue-600 dark:text-blue-400 ring-blue-500/20",
  CustomerConfirm: "bg-cyan-500/10 text-cyan-600 dark:text-cyan-400 ring-cyan-500/20",
  MdFinalSign: "bg-indigo-500/10 text-indigo-600 dark:text-indigo-400 ring-indigo-500/20",
  Signed: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 ring-emerald-500/20",
  Cancelled: "bg-slate-500/10 text-slate-500 dark:text-slate-400 ring-slate-500/20",
  Rejected: "bg-rose-500/10 text-rose-600 dark:text-rose-400 ring-rose-500/20",
  // Legacy V2.3 statuses:
  BomPending: "bg-amber-500/10 text-amber-600 dark:text-amber-400 ring-amber-500/20",
  CostingPending: "bg-amber-500/10 text-amber-600 dark:text-amber-400 ring-amber-500/20",
  MdReview: "bg-amber-500/10 text-amber-600 dark:text-amber-400 ring-amber-500/20",
  BomInProgress: "bg-blue-500/10 text-blue-600 dark:text-blue-400 ring-blue-500/20",
  CostingInProgress: "bg-blue-500/10 text-blue-600 dark:text-blue-400 ring-blue-500/20",
  Approved: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 ring-emerald-500/20",
};

interface Props {
  status: RequisitionStatus;
  className?: string;
}

export function StatusBadge({ status, className }: Props) {
  return (
    <span
      className={cn(
        "inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ring-1 ring-inset",
        COLOURS[status],
        className,
      )}
    >
      {LABELS[status]}
    </span>
  );
}
