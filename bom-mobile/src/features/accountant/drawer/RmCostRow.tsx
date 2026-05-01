import { Text, TextInput, View } from "react-native";
import { CurrencyPickerSheet } from "../../../components/CurrencyPickerSheet";
import type { V3BomLineDto } from "../../../types/v3";
import type { RawMaterialCostState } from "../state/fgReadiness";

const CURRENCIES = ["AED", "USD", "EUR", "GBP", "PKR", "INR", "CNY"];

interface Props {
  bom: V3BomLineDto;
  cost: RawMaterialCostState;
  onChange: (partial: Partial<RawMaterialCostState>) => void;
}

export function RmCostRow({ bom, cost, onChange }: Props) {
  const costInvalid =
    cost.costPerKg !== "" &&
    (parseFloat(cost.costPerKg) <= 0 || !Number.isFinite(parseFloat(cost.costPerKg)));

  return (
    <View
      style={{
        paddingVertical: 10,
        paddingHorizontal: 12,
        borderBottomWidth: 1,
        borderBottomColor: "#f1f5f9",
      }}
    >
      <View
        style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}
      >
        <View style={{ flex: 1 }}>
          <Text
            style={{ fontSize: 14, fontWeight: "600", color: "#0f172a" }}
            numberOfLines={1}
          >
            {bom.item.description}
          </Text>
          <Text style={{ fontSize: 12, color: "#64748b", marginTop: 2 }}>
            {bom.item.code} · Qty/KG: {bom.qtyPerKg.toFixed(2)} · Micron: {bom.micron ?? "—"}
          </Text>
        </View>
      </View>

      <View style={{ flexDirection: "row", alignItems: "center", marginTop: 8, gap: 8 }}>
        <View style={{ flex: 1 }}>
          <Text style={{ fontSize: 11, color: "#64748b", marginBottom: 4 }}>Cost/KG</Text>
          <TextInput
            value={cost.costPerKg}
            onChangeText={(v) => onChange({ costPerKg: v })}
            keyboardType="decimal-pad"
            placeholder="0.00"
            style={{
              borderWidth: 1,
              borderColor: costInvalid ? "#ef4444" : "#cbd5e1",
              borderRadius: 8,
              paddingHorizontal: 10,
              paddingVertical: 8,
              fontSize: 15,
              color: "#0f172a",
              backgroundColor: "#ffffff",
            }}
          />
          {costInvalid ? (
            <Text style={{ fontSize: 11, color: "#ef4444", marginTop: 4 }}>
              Must be greater than 0
            </Text>
          ) : null}
        </View>

        <View style={{ width: 110 }}>
          <Text style={{ fontSize: 11, color: "#64748b", marginBottom: 4 }}>Currency</Text>
          <CurrencyPickerSheet
            value={cost.currencyCode || "AED"}
            options={CURRENCIES}
            onChange={(code) => onChange({ currencyCode: code })}
          />
        </View>
      </View>
    </View>
  );
}
