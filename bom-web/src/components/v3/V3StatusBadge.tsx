import type { RequisitionStatus } from "@/types/api";

interface Props {
  status: RequisitionStatus;
}

const STATUS_STYLES: Record<RequisitionStatus, string> = {
  // V3
  Draft: "bg-gray-100 text-gray-700",
  Costing: "bg-blue-100 text-blue-700",
  MdPricing: "bg-amber-100 text-amber-700",
  CustomerConfirm: "bg-purple-100 text-purple-700",
  MdFinalSign: "bg-orange-100 text-orange-700",
  Signed: "bg-green-100 text-green-700",
  Cancelled: "bg-red-100 text-red-700",
  Rejected: "bg-red-100 text-red-700",

  // Legacy V2
  BomPending: "bg-gray-100 text-gray-700",
  BomInProgress: "bg-blue-100 text-blue-700",
  CostingPending: "bg-amber-100 text-amber-700",
  CostingInProgress: "bg-amber-100 text-amber-700",
  MdReview: "bg-orange-100 text-orange-700",
  Approved: "bg-green-100 text-green-700",
};

export function V3StatusBadge({ status }: Props) {
  const styles = STATUS_STYLES[status] ?? "bg-gray-100 text-gray-700";
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${styles}`}>
      {status}
    </span>
  );
}
