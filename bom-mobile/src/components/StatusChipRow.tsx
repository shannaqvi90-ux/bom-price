import { Pressable, ScrollView, Text, View } from "react-native";
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
    <View
      style={{
        height: 50,
        backgroundColor: "#f8fafc",
        borderBottomWidth: 1,
        borderBottomColor: "#e2e8f0",
      }}
    >
      <ScrollView
        horizontal
        showsHorizontalScrollIndicator={false}
        contentContainerStyle={{
          paddingHorizontal: 14,
          alignItems: "center",
        }}
      >
        {CHIPS.map((label, idx) => {
          const isActive = label === active;
          return (
            <Pressable
              key={label}
              onPress={() => {
                Haptics.selectionAsync();
                onChange(label);
              }}
              style={{ marginRight: idx < CHIPS.length - 1 ? 8 : 0 }}
            >
              <View
                style={{
                  paddingHorizontal: 12,
                  paddingVertical: 6,
                  borderRadius: 999,
                  borderWidth: 1.5,
                  backgroundColor: isActive ? "#1e40af" : "#ffffff",
                  borderColor: isActive ? "#1e40af" : "#94a3b8",
                }}
              >
                <Text style={{
                  color: isActive ? "#ffffff" : "#1e293b",
                  fontSize: 13,
                  fontWeight: "600",
                }}>
                  {label}
                </Text>
              </View>
            </Pressable>
          );
        })}
      </ScrollView>
    </View>
  );
}
