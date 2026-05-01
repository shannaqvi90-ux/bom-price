// bom-mobile/src/features/sales/detail/FgReadCard.tsx
import { useState } from "react";
import { View, Text, Pressable } from "react-native";
import * as Haptics from "expo-haptics";
import type { V3FinishedGoodDto } from "../../../types/v3";

export function FgReadCard({ fg, index }: { fg: V3FinishedGoodDto; index: number }) {
  const [expanded, setExpanded] = useState(false);
  const lines = fg.bomLines ?? [];

  return (
    <View style={{
      marginHorizontal: 12, marginVertical: 6,
      backgroundColor: "white", borderRadius: 12,
      borderWidth: 1, borderColor: "#e5e7eb",
    }}>
      <Pressable
        onPress={() => {
          Haptics.selectionAsync();
          setExpanded((s) => !s);
        }}
        style={{ padding: 14 }}
      >
        <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
          <View style={{ flex: 1 }}>
            <Text style={{ fontWeight: "600", fontSize: 14, color: "#0f172a" }}>
              FG #{index + 1} · {fg.item.code}
            </Text>
            <Text style={{ fontSize: 12, color: "#64748b", marginTop: 2 }}>
              {fg.item.description} · {fg.expectedQty}kg · {lines.length} BOM lines
            </Text>
          </View>
          <Text style={{ color: "#3b82f6", fontSize: 18 }}>{expanded ? "▾" : "▸"}</Text>
        </View>
      </Pressable>
      {expanded && (
        <View style={{ paddingHorizontal: 14, paddingBottom: 14, borderTopWidth: 1, borderColor: "#f1f5f9" }}>
          {lines.map((line, i) => (
            <View key={line.id ?? i} style={{
              marginTop: 8, padding: 10, backgroundColor: "#f8fafc", borderRadius: 8,
            }}>
              <Text style={{ fontSize: 13, fontWeight: "500", color: "#0f172a" }}>
                {line.item.code} · {line.item.description}
              </Text>
              <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
                {line.qtyPerKg.toFixed(3)} kg/kg{line.micron ? ` · ${line.micron}µ` : ""}
              </Text>
            </View>
          ))}
          {lines.length === 0 && (
            <Text style={{ marginTop: 8, color: "#94a3b8", fontStyle: "italic" }}>No BOM lines</Text>
          )}
        </View>
      )}
    </View>
  );
}
