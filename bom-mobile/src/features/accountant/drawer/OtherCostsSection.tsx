import { Text, TextInput, View } from "react-native";

interface Props {
  fohPerKg: string;
  transportPerKg: string;
  commissionPerKg: string;
  onChange: (partial: {
    fohPerKg?: string;
    transportPerKg?: string;
    commissionPerKg?: string;
  }) => void;
}

function NumField({
  label,
  value,
  onChange,
  suffix = "AED/KG",
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  suffix?: string;
}) {
  return (
    <View style={{ flex: 1 }}>
      <Text style={{ fontSize: 11, color: "#64748b", marginBottom: 4 }}>
        {label} <Text style={{ color: "#94a3b8" }}>({suffix})</Text>
      </Text>
      <TextInput
        value={value}
        onChangeText={onChange}
        keyboardType="decimal-pad"
        placeholder="0.00"
        style={{
          borderWidth: 1,
          borderColor: "#cbd5e1",
          borderRadius: 8,
          paddingHorizontal: 10,
          paddingVertical: 8,
          fontSize: 15,
          color: "#0f172a",
          backgroundColor: "#ffffff",
        }}
      />
    </View>
  );
}

export function OtherCostsSection({
  fohPerKg,
  transportPerKg,
  commissionPerKg,
  onChange,
}: Props) {
  return (
    <View style={{ padding: 12, marginTop: 8 }}>
      <Text style={{ fontSize: 13, fontWeight: "700", color: "#0f172a", marginBottom: 8 }}>
        Other costs
      </Text>
      <View style={{ flexDirection: "row", gap: 8 }}>
        <NumField
          label="FOH"
          value={fohPerKg}
          onChange={(v) => onChange({ fohPerKg: v })}
        />
        <NumField
          label="Transport"
          value={transportPerKg}
          onChange={(v) => onChange({ transportPerKg: v })}
        />
        <NumField
          label="Commission"
          value={commissionPerKg}
          onChange={(v) => onChange({ commissionPerKg: v })}
        />
      </View>
    </View>
  );
}
