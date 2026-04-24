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
    <View style={{ flexDirection: "row", flexWrap: "wrap", marginTop: 4 }}>
      <Badge label="BOM" done={bomDone} />
      <Badge label="Costing" done={costingDone} />
      <Badge label="Price" done={priceSet} />
    </View>
  );
}

function Badge({ label, done }: { label: string; done: boolean }) {
  return (
    <View
      style={{
        paddingHorizontal: 8,
        paddingVertical: 2,
        marginRight: 4,
        borderRadius: 4,
        backgroundColor: done ? "#d1fae5" : "#f1f5f9",
      }}
    >
      <Text
        style={{
          fontSize: 12,
          color: done ? "#047857" : "#64748b",
        }}
      >
        {done ? "✓" : "○"} {label}
      </Text>
    </View>
  );
}
