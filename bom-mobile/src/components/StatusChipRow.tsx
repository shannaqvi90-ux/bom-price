import { Pressable, ScrollView, Text } from "react-native";
import * as Haptics from "expo-haptics";
import type { RequisitionStatus } from "@/types/api";

export type ChipLabel = "All" | "BOM" | "Costing" | "MD review" | "Approved" | "Rejected";

export const CHIPS: ChipLabel[] = ["All", "BOM", "Costing", "MD review", "Approved", "Rejected"];

export const CHIP_TO_STATUSES: Record<ChipLabel, RequisitionStatus[]> = {
  "All":       [],
  "BOM":       ["BomPending", "BomInProgress"],
  "Costing":   ["CostingPending", "CostingInProgress"],
  "MD review": ["MdReview"],
  "Approved":  ["Approved"],
  "Rejected":  ["Rejected"],
};

interface Props {
  active: ChipLabel;
  onChange: (label: ChipLabel) => void;
}

export function StatusChipRow({ active, onChange }: Props) {
  return (
    <ScrollView
      horizontal
      showsHorizontalScrollIndicator={false}
      contentContainerStyle={{ paddingHorizontal: 14, paddingVertical: 8, gap: 6 }}
      style={{ backgroundColor: "#f8fafc", borderBottomWidth: 1, borderBottomColor: "#e2e8f0" }}
    >
      {CHIPS.map((label) => {
        const isActive = label === active;
        return (
          <Pressable
            key={label}
            onPress={() => {
              Haptics.selectionAsync();
              onChange(label);
            }}
            style={{
              paddingHorizontal: 12,
              paddingVertical: 6,
              borderRadius: 999,
              borderWidth: 1,
              backgroundColor: isActive ? "#1e40af" : "#ffffff",
              borderColor: isActive ? "#1e40af" : "#cbd5e1",
            }}
          >
            <Text style={{
              color: isActive ? "#ffffff" : "#334155",
              fontSize: 12,
              fontWeight: "600",
            }}>
              {label}
            </Text>
          </Pressable>
        );
      })}
    </ScrollView>
  );
}
