import { Text, View } from "react-native";
import type { V3FinalPrice } from "@/types/v3";

interface Props {
  finalPrice: V3FinalPrice;
}

export function FinalSignSummary({ finalPrice }: Props) {
  return (
    <View style={{ marginHorizontal: 12, marginVertical: 8 }}>
      <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "700", letterSpacing: 0.5 }}>
        QUOTE SUMMARY
      </Text>
      <View
        style={{
          marginTop: 8,
          padding: 12,
          backgroundColor: "white",
          borderRadius: 10,
          borderWidth: 1,
          borderColor: "#e2e8f0",
        }}
      >
        {finalPrice.perFg.map((fg) => (
          <View
            key={fg.requisitionItemId}
            style={{
              paddingVertical: 8,
              borderBottomWidth: 1,
              borderBottomColor: "#f1f5f9",
            }}
          >
            <Text style={{ fontSize: 14, color: "#0f172a", fontWeight: "600" }}>
              {fg.description}
            </Text>
            <Text style={{ fontSize: 12, color: "#94a3b8", marginTop: 2 }}>
              {fg.expectedQty.toLocaleString()} KG × {finalPrice.currencyCode}{" "}
              {fg.salePerKg.toFixed(2)} = AED{" "}
              {fg.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
            </Text>
          </View>
        ))}
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            marginTop: 12,
          }}
        >
          <Text style={{ fontSize: 14, fontWeight: "700", color: "#0f172a" }}>TOTAL</Text>
          <Text style={{ fontSize: 18, fontWeight: "700", color: "#1e40af" }}>
            AED{" "}
            {finalPrice.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
          </Text>
        </View>
      </View>
    </View>
  );
}
