// bom-mobile/src/features/sales/create/BomLineRow.tsx
import { View, Text, TextInput, Pressable } from "react-native";
import type { V3BomLine } from "../../../types/v3";

interface Props {
  line: V3BomLine; idx: number;
  onChange: (line: V3BomLine) => void;
  onPickRm: () => void;
  onPickProcess: () => void;
  onRemove: () => void;
}

export function BomLineRow({ line, idx, onChange, onPickRm, onPickProcess, onRemove }: Props) {
  return (
    <View style={{ marginTop: 10, padding: 10, backgroundColor: "#f8fafc", borderRadius: 8 }}>
      <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
        <Text style={{ fontSize: 11, fontWeight: "600", color: "#64748b" }}>Line {idx + 1}</Text>
        <Pressable onPress={onRemove}><Text style={{ color: "#dc2626", fontSize: 12 }}>Remove</Text></Pressable>
      </View>
      <Pressable onPress={onPickProcess} style={{ marginTop: 6 }}>
        <Text style={{ fontSize: 10, color: "#94a3b8" }}>Process</Text>
        <Text style={{ fontWeight: "500", color: line.processName ? "#0f172a" : "#94a3b8" }}>
          {line.processName ?? "Tap to pick…"}
        </Text>
      </Pressable>
      <Pressable onPress={onPickRm} style={{ marginTop: 6 }}>
        <Text style={{ fontSize: 10, color: "#94a3b8" }}>Raw material</Text>
        <Text style={{ fontWeight: "500", color: line.rawMaterialDescription ? "#0f172a" : "#94a3b8" }}>
          {line.rawMaterialDescription ?? "Tap to pick…"}
        </Text>
      </Pressable>
      <View style={{ flexDirection: "row", gap: 8, marginTop: 6 }}>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 10, color: "#94a3b8" }}>Qty/kg</Text>
          <TextInput
            value={String(line.qtyPerKg)}
            keyboardType="decimal-pad"
            onChangeText={(t) => onChange({ ...line, qtyPerKg: parseFloat(t) || 0 })}
            style={{ padding: 6, backgroundColor: "white", borderRadius: 6, fontSize: 14 }}
          />
        </View>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 10, color: "#94a3b8" }}>Wastage %</Text>
          <TextInput
            value={String(line.wastagePct)}
            keyboardType="decimal-pad"
            onChangeText={(t) => onChange({ ...line, wastagePct: parseFloat(t) || 0 })}
            style={{ padding: 6, backgroundColor: "white", borderRadius: 6, fontSize: 14 }}
          />
        </View>
      </View>
    </View>
  );
}
