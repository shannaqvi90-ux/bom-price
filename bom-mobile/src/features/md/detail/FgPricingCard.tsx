import { Text, TextInput, View } from "react-native";
import type { V3FinishedGoodDto } from "@/types/v3";

interface Props {
  fg: V3FinishedGoodDto;
  index: number;
  marginInput: string;
  onMarginChange: (value: string) => void;
  livePerFg: { salePerKg: number; salePerKgAed: number; totalAed: number } | null;
  currencyCode: string;
}

export function FgPricingCard({ fg, index, marginInput, onMarginChange, livePerFg, currencyCode }: Props) {
  const costPerKg = fg.costs?.totalCostPerKg ?? 0;
  return (
    <View
      style={{
        marginHorizontal: 12,
        marginVertical: 6,
        padding: 14,
        borderRadius: 12,
        backgroundColor: "white",
        borderWidth: 1,
        borderColor: "#e5e7eb",
      }}
    >
      <Text style={{ fontSize: 12, color: "#64748b", fontWeight: "700", letterSpacing: 0.5 }}>
        FG {index + 1}
      </Text>
      <Text style={{ fontSize: 15, fontWeight: "600", color: "#0f172a", marginTop: 4 }}>
        {fg.item.description}
      </Text>
      <Text style={{ fontSize: 12, color: "#94a3b8", marginTop: 2 }}>
        {fg.item.code} · {fg.expectedQty.toLocaleString()} KG
      </Text>

      <View style={{ marginTop: 12, padding: 10, backgroundColor: "#f8fafc", borderRadius: 8 }}>
        <RowKV label="Cost/KG" value={`${currencyCode} ${costPerKg.toFixed(2)}`} />
        <View style={{ flexDirection: "row", alignItems: "center", marginTop: 8 }}>
          <Text style={{ flex: 1, fontSize: 13, color: "#475569" }}>Margin/KG</Text>
          <TextInput
            value={marginInput}
            onChangeText={onMarginChange}
            placeholder="0.00"
            placeholderTextColor="#94a3b8"
            keyboardType="decimal-pad"
            style={{
              borderWidth: 1,
              borderColor: "#cbd5e1",
              borderRadius: 8,
              paddingHorizontal: 10,
              paddingVertical: 8,
              fontSize: 14,
              minWidth: 100,
              textAlign: "right",
              color: "#0f172a",
              backgroundColor: "white",
            }}
          />
        </View>
        {livePerFg ? (
          <View style={{ borderTopWidth: 1, borderTopColor: "#e2e8f0", marginTop: 8, paddingTop: 8 }}>
            <RowKV label="Sale/KG" value={`${currencyCode} ${livePerFg.salePerKg.toFixed(2)}`} highlight />
            <RowKV
              label="Total"
              value={`${currencyCode} ${livePerFg.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}`}
              highlight
            />
          </View>
        ) : null}
      </View>
    </View>
  );
}

function RowKV({ label, value, highlight }: { label: string; value: string; highlight?: boolean }) {
  return (
    <View style={{ flexDirection: "row", justifyContent: "space-between" }}>
      <Text style={{ fontSize: 13, color: "#64748b" }}>{label}</Text>
      <Text
        style={{
          fontSize: 13,
          color: highlight ? "#0f172a" : "#475569",
          fontWeight: highlight ? "700" : "400",
        }}
      >
        {value}
      </Text>
    </View>
  );
}
