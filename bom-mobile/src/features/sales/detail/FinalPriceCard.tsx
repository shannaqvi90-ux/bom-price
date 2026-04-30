import { View, Text } from "react-native";
import type { V3Requisition } from "../../../types/v3";

export function FinalPriceCard({ req }: { req: V3Requisition }) {
  if (!req.finalPrice) return null;
  return (
    <View style={{
      margin: 12, padding: 18, borderRadius: 14,
      backgroundColor: "#10b981",
    }}>
      <Text style={{ color: "rgba(255,255,255,0.85)", fontSize: 12, fontWeight: "600", letterSpacing: 0.6, textTransform: "uppercase" }}>
        Final price
      </Text>
      <Text style={{ color: "white", fontSize: 32, fontWeight: "700", marginTop: 4 }}>
        AED {req.finalPrice.totalAed.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })}
      </Text>
      {req.finalPrice.perFg.length > 1 && (
        <View style={{ marginTop: 10 }}>
          {req.finalPrice.perFg.map((p) => (
            <Text key={p.itemId} style={{ color: "rgba(255,255,255,0.9)", fontSize: 12, marginTop: 2 }}>
              FG {p.itemId}: AED {p.priceAed.toFixed(2)}
            </Text>
          ))}
        </View>
      )}
    </View>
  );
}
