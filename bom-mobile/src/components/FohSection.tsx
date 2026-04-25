import { Text, TextInput, View } from "react-native";

interface Props {
  amount: number;
  fieldError?: string;
  onChange: (v: number) => void;
  onBlur: () => void;
}

export function FohSection({ amount, fieldError, onChange, onBlur }: Props) {
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
      <Text style={{ fontSize: 14, fontWeight: "600", marginBottom: 8 }}>FOH per kg (AED)</Text>
      <TextInput
        value={String(amount ?? 0)}
        onChangeText={(t) => onChange(Number(t) || 0)}
        onBlur={onBlur}
        keyboardType="decimal-pad"
        style={{
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
