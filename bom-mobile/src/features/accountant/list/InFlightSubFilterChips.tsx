import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";

export type InFlightSubFilter = "all" | "md" | "customer";

const CHIP_LABELS: Record<InFlightSubFilter, string> = {
  all: "All",
  md: "MD",
  customer: "Customer",
};

interface Props {
  active: InFlightSubFilter;
  onChange: (chip: InFlightSubFilter) => void;
}

export function InFlightSubFilterChips({ active, onChange }: Props) {
  return (
    <View style={{ flexDirection: "row", gap: 8, paddingHorizontal: 12, paddingBottom: 8 }}>
      {(Object.keys(CHIP_LABELS) as InFlightSubFilter[]).map((c) => {
        const isActive = active === c;
        return (
          <Pressable
            key={c}
            onPress={() => {
              Haptics.selectionAsync();
              onChange(c);
            }}
            style={{
              paddingHorizontal: 12,
              paddingVertical: 6,
              borderRadius: 999,
              borderWidth: 1,
              borderColor: isActive ? "#1e40af" : "#cbd5e1",
              backgroundColor: isActive ? "#1e40af" : "#ffffff",
            }}
          >
            <Text style={{
              fontSize: 12,
              fontWeight: "600",
              color: isActive ? "#ffffff" : "#64748b",
            }}>
              {CHIP_LABELS[c]}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}
