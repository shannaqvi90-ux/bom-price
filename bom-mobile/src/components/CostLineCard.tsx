import { useMemo } from "react";
import { Text, TextInput, View } from "react-native";
import type { CostingBomLine } from "@/types/api";
import { CurrencyPickerSheet } from "./CurrencyPickerSheet";
import { StaleCostBadge } from "./StaleCostBadge";

interface Props {
  line: CostingBomLine;
  value: { costPerKg: number; currencyCode: string };
  currencyOptions: string[];
  fieldError?: string;
  onChange: (v: { costPerKg: number; currencyCode: string }) => void;
  onBlur: () => void;
}

const STALE_DAYS = 10;

function daysSince(iso: string): number {
  return Math.floor((Date.now() - new Date(iso).getTime()) / 86_400_000);
}

export function CostLineCard({ line, value, currencyOptions, fieldError, onChange, onBlur }: Props) {
  const staleDays = useMemo(
    () => (line.lastCost ? daysSince(line.lastCost.updatedAt) : null),
    [line.lastCost],
  );

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
      <Text style={{ fontSize: 11, letterSpacing: 0.6, color: "#64748b", fontWeight: "700" }}>
        {line.processName.toUpperCase()}
      </Text>
      <Text style={{ fontSize: 15, fontWeight: "600", marginTop: 2 }}>
        {line.rawMaterialDescription}
      </Text>
      <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
        qty {line.qtyPerKg.toFixed(2)} / kg · wastage {line.wastagePct.toFixed(1)}%
      </Text>

      <View style={{ flexDirection: "row", gap: 8, marginTop: 10, alignItems: "center" }}>
        <Text style={{ fontSize: 12, color: "#475569", fontWeight: "600", width: 38 }}>Cost</Text>
        <View style={{ flex: 1 }}>
          <TextInput
            value={String(value.costPerKg ?? 0)}
            onChangeText={(t) => onChange({ ...value, costPerKg: Number(t) || 0 })}
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
        </View>
        <View style={{ minWidth: 90 }}>
          <CurrencyPickerSheet
            value={value.currencyCode}
            options={currencyOptions}
            onChange={(code) => onChange({ ...value, currencyCode: code })}
          />
        </View>
      </View>

      {fieldError ? (
        <Text style={{ marginTop: 6, color: "#b91c1c", fontSize: 12 }}>{fieldError}</Text>
      ) : null}

      {staleDays !== null && staleDays > STALE_DAYS && line.lastCost ? (
        <StaleCostBadge
          daysAgo={staleDays}
          costPerKg={line.lastCost.costPerKg}
          currencyCode={line.lastCost.currencyCode}
        />
      ) : null}
    </View>
  );
}
