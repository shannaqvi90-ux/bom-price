import { Text, TextInput, View } from "react-native";
import type { MdReviewItemDetail } from "@/types/api";
import { formatMoney, formatPct } from "@/utils/numbers";

interface Props {
  item: MdReviewItemDetail;
  price: number;
  onPriceChange: (next: number) => void;
  error?: string;
}

function computeMarginPct(cost: number, price: number): number | null {
  if (!Number.isFinite(cost) || cost <= 0) return null;
  if (!Number.isFinite(price) || price <= 0) return null;
  return ((price - cost) / cost) * 100;
}

export function ApprovalItemRow({ item, price, onPriceChange, error }: Props) {
  const cost = item.cost?.totalCostPerKg ?? 0;
  const margin = computeMarginPct(cost, price);

  return (
    <View className="bg-white border border-slate-200 rounded-md p-3 mb-2">
      <Text className="text-sm font-semibold text-slate-900 mb-1" numberOfLines={2}>
        {item.itemDescription}
      </Text>
      <View className="flex-row justify-between mb-2">
        <Text className="text-xs text-slate-500">Qty: {item.expectedQty}</Text>
        <Text className="text-xs text-slate-500">Cost/kg: {formatMoney(cost)}</Text>
      </View>

      <Text className="text-sm text-slate-700 mb-1">Sales price per kg (AED)</Text>
      <TextInput
        keyboardType="decimal-pad"
        value={price > 0 ? String(price) : ""}
        onChangeText={(t) => {
          const n = Number(t);
          onPriceChange(Number.isFinite(n) ? n : 0);
        }}
        placeholder="0.0000"
        placeholderTextColor="#94a3b8"
        className={`border rounded-md px-3 py-2 text-base text-slate-900 bg-white ${
          error ? "border-rose-500" : "border-slate-300"
        }`}
      />
      {error ? <Text className="text-xs text-rose-600 mt-1">{error}</Text> : null}

      <View className="flex-row justify-between mt-2">
        <Text className="text-xs text-slate-500">Margin: {formatPct(margin)}</Text>
        <Text className="text-xs text-slate-500">
          Revenue: {formatMoney(price * item.expectedQty)}
        </Text>
      </View>
    </View>
  );
}
