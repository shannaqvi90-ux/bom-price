import { Text, View } from "react-native";
import type { MdReviewItemCost } from "@/types/api";

interface Props {
  cost: MdReviewItemCost | null;
}

function Row({ label, value, pct }: { label: string; value: string; pct?: string }) {
  return (
    <View
      style={{
        flexDirection: "row",
        justifyContent: "space-between",
        alignItems: "baseline",
        paddingVertical: 4,
      }}
    >
      <Text style={{ fontSize: 14, color: "#475569" }}>{label}</Text>
      <Text style={{ fontSize: 14, color: "#0f172a", fontWeight: "600" }}>
        {value}
        {pct ? (
          <Text style={{ fontSize: 12, color: "#94a3b8", fontWeight: "500" }}>
            {`  (${pct})`}
          </Text>
        ) : null}
      </Text>
    </View>
  );
}

export function CostBreakdownCard({ cost }: Props) {
  if (cost === null) {
    return (
      <View
        style={{
          backgroundColor: "#ffffff",
          borderWidth: 1,
          borderColor: "#e2e8f0",
          borderRadius: 14,
          padding: 14,
          marginTop: 10,
        }}
      >
        <Text style={{ fontSize: 14, color: "#94a3b8", textAlign: "center" }}>
          Costing not completed
        </Text>
      </View>
    );
  }

  return (
    <View
      style={{
        backgroundColor: "#ffffff",
        borderWidth: 1,
        borderColor: "#e2e8f0",
        borderRadius: 14,
        padding: 14,
        marginTop: 10,
      }}
    >
      <Text
        style={{
          fontSize: 13,
          fontWeight: "700",
          color: "#64748b",
          letterSpacing: 0.5,
          marginBottom: 8,
        }}
      >
        COST BREAKDOWN (AED/kg)
      </Text>
      <Row
        label="Raw Material"
        value={cost.rawMaterialCostPerKg.toFixed(4)}
        pct={`${cost.materialCostPct.toFixed(1)}%`}
      />
      <Row
        label="Landed"
        value={cost.landedCostPerKg.toFixed(4)}
        pct={`${cost.landedCostPct.toFixed(1)}%`}
      />
      <Row
        label="FOH"
        value={cost.fohPerKg.toFixed(4)}
        pct={`${cost.fohPct.toFixed(1)}%`}
      />
      <View
        style={{
          flexDirection: "row",
          justifyContent: "space-between",
          alignItems: "baseline",
          paddingTop: 8,
          marginTop: 4,
          borderTopWidth: 1,
          borderTopColor: "#e2e8f0",
        }}
      >
        <Text style={{ fontSize: 14, fontWeight: "700", color: "#0f172a" }}>Total</Text>
        <Text style={{ fontSize: 15, fontWeight: "700", color: "#0f172a" }}>
          {cost.totalCostPerKg.toFixed(4)}
        </Text>
      </View>
    </View>
  );
}
