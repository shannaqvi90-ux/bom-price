import { useState } from "react";
import { Pressable, Text, TextInput, View } from "react-native";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import type { MdReviewItemDetail } from "@/types/api";
import { formatMoney } from "@/utils/numbers";
import { stripTags } from "@/utils/text";

interface Props {
  item: MdReviewItemDetail;
  price: number;
  onPriceChange: (next: number) => void;
  error?: string;
  onViewBom: () => void;
}

function computeMarginPct(cost: number, price: number): number | null {
  if (!Number.isFinite(cost) || cost <= 0) return null;
  if (!Number.isFinite(price) || price <= 0) return null;
  return ((price - cost) / cost) * 100;
}

function marginColor(margin: number | null): string {
  if (margin === null) return "#94a3b8";
  if (margin > 20) return "#059669";
  if (margin >= 10) return "#f59e0b";
  return "#dc2626";
}

function hexWithAlpha(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16);
  const g = parseInt(hex.slice(3, 5), 16);
  const b = parseInt(hex.slice(5, 7), 16);
  return `rgba(${r},${g},${b},${alpha})`;
}

export function ApprovalItemRow({ item, price, onPriceChange, error, onViewBom }: Props) {
  const [focused, setFocused] = useState(false);
  const [bomPressed, setBomPressed] = useState(false);

  const cost = item.cost?.totalCostPerKg ?? 0;
  const margin = computeMarginPct(cost, price);
  const mColor = marginColor(margin);
  const marginLabel = margin === null ? "-" : `${margin.toFixed(1)}%`;

  const borderColor = error ? "#dc2626" : focused ? "#1e40af" : "#e2e8f0";

  return (
    <View
      style={{
        backgroundColor: "#ffffff",
        borderWidth: 1,
        borderColor: "#e2e8f0",
        borderRadius: 14,
        padding: 14,
        marginBottom: 10,
      }}
    >
      {/* Item description */}
      <Text
        style={{ fontSize: 16, fontWeight: "700", color: "#0f172a", marginBottom: 6 }}
        numberOfLines={2}
      >
        {stripTags(item.itemDescription)}
      </Text>

      {/* Meta row */}
      <Text style={{ fontSize: 14, color: "#64748b", marginBottom: 10 }}>
        Qty: {item.expectedQty} · Cost/kg: {formatMoney(cost)}
      </Text>

      {/* Price input label */}
      <Text style={{ fontSize: 14, fontWeight: "600", color: "#334155", marginBottom: 6 }}>
        Sales price per kg (AED)
      </Text>

      {/* Price input */}
      <MotiView
        animate={{ borderColor }}
        transition={{ type: "timing", duration: 150 }}
        style={{
          borderWidth: 1,
          borderRadius: 10,
          backgroundColor: "#f8fafc",
          marginBottom: error ? 4 : 10,
        }}
      >
        <TextInput
          keyboardType="decimal-pad"
          value={price > 0 ? String(price) : ""}
          onChangeText={(t) => {
            const n = Number(t);
            onPriceChange(Number.isFinite(n) ? n : 0);
          }}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          placeholder="0.0000"
          placeholderTextColor="#94a3b8"
          style={{
            paddingHorizontal: 12,
            paddingVertical: 11,
            fontSize: 17,
            color: "#0f172a",
          }}
        />
      </MotiView>

      {error ? (
        <Text style={{ color: "#dc2626", fontSize: 13, marginBottom: 10 }}>{error}</Text>
      ) : null}

      {/* Bottom row: margin pill + revenue */}
      <View
        style={{
          flexDirection: "row",
          justifyContent: "space-between",
          alignItems: "center",
          marginBottom: 10,
        }}
      >
        <View
          style={{
            backgroundColor: hexWithAlpha(mColor, 0.15),
            paddingHorizontal: 10,
            paddingVertical: 4,
            borderRadius: 999,
          }}
        >
          <Text style={{ fontSize: 13, fontWeight: "600", color: mColor }}>
            {marginLabel}
          </Text>
        </View>
        <Text style={{ fontSize: 15, color: "#64748b" }}>
          Rev: {formatMoney(price * item.expectedQty)}
        </Text>
      </View>

      {/* View BOM button */}
      <Pressable
        onPressIn={() => {
          setBomPressed(true);
          Haptics.selectionAsync();
        }}
        onPressOut={() => setBomPressed(false)}
        onPress={onViewBom}
      >
        <MotiView
          animate={{ scale: bomPressed ? 0.98 : 1 }}
          transition={{ type: "spring", damping: 15, stiffness: 300 }}
          style={{
            borderWidth: 1,
            borderColor: "#1e40af",
            borderRadius: 10,
            paddingVertical: 10,
            paddingHorizontal: 14,
            alignItems: "center",
            backgroundColor: "transparent",
          }}
        >
          <Text style={{ fontSize: 15, fontWeight: "600", color: "#1e40af" }}>
            View BOM breakdown →
          </Text>
        </MotiView>
      </Pressable>
    </View>
  );
}
