import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { MdTab } from "./tabFilters";

const TAB_LABELS: Record<MdTab, string> = {
  queue: "Queue",
  "in-flight": "In Flight",
  done: "Done",
  closed: "Closed",
};

interface Props {
  active: MdTab;
  onChange: (tab: MdTab) => void;
}

export function MdTabs({ active, onChange }: Props) {
  return (
    <View style={{
      flexDirection: "row",
      backgroundColor: "#f1f5f9",
      borderRadius: 10,
      padding: 4,
      margin: 12,
    }}>
      {(Object.keys(TAB_LABELS) as MdTab[]).map((t) => {
        const isActive = active === t;
        return (
          <Pressable
            key={t}
            onPress={() => {
              Haptics.selectionAsync();
              onChange(t);
            }}
            style={{
              flex: 1,
              paddingVertical: 8,
              borderRadius: 8,
              backgroundColor: isActive ? "#ffffff" : "transparent",
              shadowColor: isActive ? "#0f172a" : "transparent",
              shadowOffset: { width: 0, height: 1 },
              shadowOpacity: 0.06,
              shadowRadius: 2,
              elevation: isActive ? 1 : 0,
            }}
          >
            <Text style={{
              textAlign: "center",
              fontSize: 13,
              fontWeight: isActive ? "700" : "500",
              color: isActive ? "#0f172a" : "#64748b",
            }}>
              {TAB_LABELS[t]}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}
