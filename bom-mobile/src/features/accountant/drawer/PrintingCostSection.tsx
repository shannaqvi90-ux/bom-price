import { Text, TextInput, View } from "react-native";
import { CurrencyPickerSheet } from "../../../components/CurrencyPickerSheet";

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "PKR", "INR", "CNY"];

interface Props {
  costPerKg: string;
  currency: string;
  onChange: (partial: { costPerKg?: string; currency?: string }) => void;
}

export function PrintingCostSection({ costPerKg, currency, onChange }: Props) {
  const invalid = costPerKg !== "" && parseFloat(costPerKg) <= 0;

  return (
    <View
      style={{
        padding: 12,
        backgroundColor: "#fef9c3",
        marginVertical: 8,
        borderRadius: 10,
      }}
    >
      <Text style={{ fontSize: 13, fontWeight: "700", color: "#854d0e", marginBottom: 8 }}>
        Printing cost (this FG has printing)
      </Text>
      <View style={{ flexDirection: "row", gap: 8 }}>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 11, color: "#854d0e", marginBottom: 4 }}>Cost/KG</Text>
          <TextInput
            value={costPerKg}
            onChangeText={(v) => onChange({ costPerKg: v })}
            keyboardType="decimal-pad"
            placeholder="0.00"
            style={{
              borderWidth: 1,
              borderColor: invalid ? "#ef4444" : "#fde68a",
              borderRadius: 8,
              paddingHorizontal: 10,
              paddingVertical: 8,
              fontSize: 15,
              color: "#0f172a",
              backgroundColor: "#ffffff",
            }}
          />
          {invalid ? (
            <Text style={{ fontSize: 11, color: "#ef4444", marginTop: 4 }}>Must be &gt; 0</Text>
          ) : null}
        </View>
        <View style={{ width: 110 }}>
          <Text style={{ fontSize: 11, color: "#854d0e", marginBottom: 4 }}>Currency</Text>
          <CurrencyPickerSheet
            value={currency}
            options={CURRENCIES}
            onChange={(code) => onChange({ currency: code })}
            placeholder="Set…"
          />
        </View>
      </View>
    </View>
  );
}
