import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import type { V3FinishedGoodDto } from "../../../types/v3";
import type { FgReadiness } from "../state/fgReadiness";

interface Props {
  fgIdx: number;
  fg: V3FinishedGoodDto;
  readiness: FgReadiness;
  onPress: () => void;
}

export function FgCostingCard({ fgIdx, fg, readiness, onPress }: Props) {
  const dot = readiness === "ready" ? "🟢" : readiness === "in_progress" ? "🟡" : "⚪";
  const ringColor = readiness === "ready" ? "#10b981" : readiness === "in_progress" ? "#f59e0b" : "#cbd5e1";

  return (
    <View style={{ marginHorizontal: 12, marginVertical: 6 }}>
      <Pressable
        onPress={() => { Haptics.selectionAsync(); onPress(); }}
        style={({ pressed }) => ({
          padding: 14, borderRadius: 12,
          backgroundColor: "#ffffff",
          borderWidth: 1.5, borderColor: ringColor,
          opacity: pressed ? 0.85 : 1,
        })}
      >
        <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
          <Text style={{ fontSize: 12, color: "#64748b", fontWeight: "700", letterSpacing: 0.5 }}>
            FG {fgIdx + 1}
          </Text>
          <Text style={{ fontSize: 13, fontWeight: "700" }}>{dot}</Text>
        </View>
        <Text style={{ fontSize: 15, fontWeight: "600", color: "#0f172a", marginTop: 4 }} numberOfLines={2}>
          {fg.item.description}
        </Text>
        <Text style={{ fontSize: 12, color: "#94a3b8", marginTop: 2 }}>
          {fg.item.code}
        </Text>
        <Text style={{ fontSize: 13, color: "#64748b", marginTop: 4 }}>
          {fg.expectedQty.toLocaleString()} KG · {(fg.bomLines ?? []).length} BOM line{(fg.bomLines ?? []).length === 1 ? "" : "s"}
        </Text>
        <Text style={{ marginTop: 8, color: "#1e40af", fontSize: 13, fontWeight: "600" }}>
          Edit costs ▸
        </Text>
      </Pressable>
    </View>
  );
}
