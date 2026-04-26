import type { RequisitionStatus } from "@/types/api";

interface Props {
  requisition: { id: number; refNo: string; status: RequisitionStatus };
  onClose: () => void;
}

export function ReassignSpModal(_props: Props) {
  return null;
}
