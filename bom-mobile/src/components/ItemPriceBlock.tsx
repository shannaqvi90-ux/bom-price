import { Text, View } from "react-native";
import { formatCurrency } from "@/utils/numbers";

interface Props {
  expectedQty: number;
  pricePerKg: number;
  currencyCode: string;
}

export function ItemPriceBlock({ expectedQty, pricePerKg, currencyCode }: Props) {
  const lineTotal = expectedQty * pricePerKg;
  return (
    <View style={{ marginTop: 10, paddingTop: 10, borderTopWidth: 1, borderTopColor: "#f1f5f9" }}>
      <Row label="Price / kg" value={formatCurrency(pricePerKg, currencyCode)} />
      <Row label="Line total" value={formatCurrency(lineTotal, currencyCode)} bold />
    </View>
  );
}

function Row({ label, value, bold = false }: { label: string; value: string; bold?: boolean }) {
  return (
    <View style={{ flexDirection: "row", justifyContent: "space-between", paddingVertical: 3 }}>
      <Text style={{ fontSize: 13, color: "#64748b" }}>{label}</Text>
      <Text style={{ fontSize: 14, color: "#0f172a", fontWeight: bold ? "700" : "500" }}>
        {value}
      </Text>
    </View>
  );
}
