import { Text, View } from "react-native";
import type { RequisitionStatus } from "@/types/api";

const STAGE_ORDER: RequisitionStatus[] = [
  "Draft",
  "BomPending",
  "BomInProgress",
  "CostingPending",
  "CostingInProgress",
  "MdReview",
  "Approved",
];

function indexOf(status: RequisitionStatus): number {
  const i = STAGE_ORDER.indexOf(status);
  return i < 0 ? 0 : i;
}

function isAtLeast(status: RequisitionStatus, target: RequisitionStatus): boolean {
  if (status === "Rejected") return false;
  return indexOf(status) >= indexOf(target);
}

interface Props {
  status: RequisitionStatus;
}

export function ItemStageBadge({ status }: Props) {
  const bomDone = isAtLeast(status, "CostingPending");
  const costingDone = isAtLeast(status, "MdReview");
  const priceSet = status === "Approved";

  return (
    <View className="flex-row flex-wrap mt-1">
      <Badge label="BOM" done={bomDone} />
      <Badge label="Costing" done={costingDone} />
      <Badge label="Price" done={priceSet} />
    </View>
  );
}

function Badge({ label, done }: { label: string; done: boolean }) {
  return (
    <View
      className={`px-2 py-0.5 mr-1 rounded ${
        done ? "bg-emerald-100" : "bg-slate-100"
      }`}
    >
      <Text
        className={`text-xs ${done ? "text-emerald-700" : "text-slate-500"}`}
      >
        {done ? "✓" : "○"} {label}
      </Text>
    </View>
  );
}
