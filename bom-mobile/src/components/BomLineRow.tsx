import { Text, View } from "react-native";
import { stripTags } from "@/utils/text";
import type { BomLine } from "@/types/api";

interface Props {
  line: BomLine;
}

export function BomLineRow({ line }: Props) {
  const qty = line.qtyPerKg;
  const waste = line.wastagePct;
  const costDisplay =
    line.costPerKgInAed != null
      ? `${line.costPerKgInAed.toFixed(4)} AED/kg`
      : line.costPerKg != null
      ? `${line.costPerKg.toFixed(4)} ${line.currencyCode ?? ""}`
      : "—";
  const contribDisplay =
    line.contributionAed != null
      ? `${line.contributionAed.toFixed(4)} AED`
      : "—";

  return (
    <View
      style={{
        backgroundColor: "#ffffff",
        borderWidth: 1,
        borderColor: "#e2e8f0",
        borderRadius: 12,
        padding: 12,
        marginBottom: 8,
      }}
    >
      <Text
        style={{ fontSize: 15, fontWeight: "600", color: "#0f172a" }}
        numberOfLines={2}
      >
        {stripTags(line.rawMaterialDescription)}
      </Text>
      <View style={{ marginTop: 6, flexDirection: "row", flexWrap: "wrap" }}>
        <Text style={{ fontSize: 13, color: "#64748b", marginRight: 12 }}>
          Qty/kg <Text style={{ fontWeight: "600", color: "#334155" }}>{qty}</Text>
        </Text>
        <Text style={{ fontSize: 13, color: "#64748b", marginRight: 12 }}>
          Wastage <Text style={{ fontWeight: "600", color: "#334155" }}>{waste}%</Text>
        </Text>
      </View>
      <View style={{ marginTop: 4, flexDirection: "row", justifyContent: "space-between" }}>
        <Text style={{ fontSize: 13, color: "#64748b" }}>Cost</Text>
        <Text style={{ fontSize: 13, fontWeight: "600", color: "#0f172a" }}>{costDisplay}</Text>
      </View>
      <View style={{ marginTop: 2, flexDirection: "row", justifyContent: "space-between" }}>
        <Text style={{ fontSize: 13, color: "#64748b" }}>Contribution</Text>
        <Text style={{ fontSize: 13, fontWeight: "600", color: "#1e40af" }}>{contribDisplay}</Text>
      </View>
    </View>
  );
}
