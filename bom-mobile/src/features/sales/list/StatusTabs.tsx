import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { ListTab } from "../utils/statusMap";
import { theme } from "../../../theme";

interface Props {
  current: ListTab;
  counts: Record<ListTab, number>;
  onChange: (tab: ListTab) => void;
}

const TABS: { key: ListTab; label: string }[] = [
  { key: "active", label: "Active" },
  { key: "done", label: "Done" },
  { key: "closed", label: "Closed" },
];

export function StatusTabs({ current, counts, onChange }: Props) {
  return (
    <View style={{ flexDirection: "row", padding: 8, gap: 6 }}>
      {TABS.map(({ key, label }) => {
        const active = current === key;
        return (
          <Pressable
            key={key}
            onPress={() => {
              Haptics.selectionAsync();
              onChange(key);
            }}
            style={{
              flex: 1, paddingVertical: 10, borderRadius: 10,
              backgroundColor: active ? theme.colors.primary : "#f1f5f9",
              alignItems: "center",
            }}
          >
            <Text style={{ color: active ? "white" : "#0f172a", fontWeight: "600" }}>
              {label} {counts[key] > 0 ? `(${counts[key]})` : ""}
            </Text>
          </Pressable>
        );
      })}
    </View>
  );
}
