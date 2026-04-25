import { Pressable, Text, TextInput, View } from "react-native";
import type { LandedCostType } from "@/types/api";

interface Props {
  type: LandedCostType;
  value: number;
  fieldError?: string;
  onChange: (v: { type: LandedCostType; value: number }) => void;
  onBlur: () => void;
}

export function LandedCostSection({ type, value, fieldError, onChange, onBlur }: Props) {
  const SegButton = ({ label, val }: { label: string; val: LandedCostType }) => (
    <Pressable
      onPress={() => onChange({ type: val, value })}
      style={({ pressed }) => ({ flex: 1, opacity: pressed ? 0.7 : 1 })}
    >
      <View
        style={{
          paddingVertical: 8,
          alignItems: "center",
          backgroundColor: type === val ? "#1e40af" : "#f1f5f9",
          borderRadius: 8,
        }}
      >
        <Text style={{ color: type === val ? "#fff" : "#475569", fontWeight: "600" }}>{label}</Text>
      </View>
    </Pressable>
  );

  return (
    <View
      style={{
        backgroundColor: "#fff",
        borderRadius: 10,
        padding: 12,
        marginBottom: 10,
        borderWidth: 1,
        borderColor: fieldError ? "#fca5a5" : "#e2e8f0",
      }}
    >
      <Text style={{ fontSize: 14, fontWeight: "600", marginBottom: 8 }}>Landed Cost</Text>
      <View style={{ flexDirection: "row", gap: 8 }}>
        <SegButton label="Percentage" val="Percentage" />
        <SegButton label="Fixed AED" val="FixedValue" />
      </View>
      <TextInput
        value={String(value ?? 0)}
        onChangeText={(t) => onChange({ type, value: Number(t) || 0 })}
        onBlur={onBlur}
        keyboardType="decimal-pad"
        placeholder={type === "Percentage" ? "% of raw material" : "AED amount"}
        style={{
          marginTop: 10,
          borderWidth: 1,
          borderColor: "#cbd5e1",
          borderRadius: 8,
          padding: 10,
          fontVariant: ["tabular-nums"],
          backgroundColor: "#fff",
        }}
      />
      {fieldError ? (
        <Text style={{ marginTop: 6, color: "#b91c1c", fontSize: 12 }}>{fieldError}</Text>
      ) : null}
    </View>
  );
}
