import { Pressable, Text, View } from "react-native";
import { StatusPill } from "./StatusPill";
import type { RequisitionListItem } from "@/types/api";
import { formatShortDate } from "@/utils/dates";

interface Props {
  item: RequisitionListItem;
  onPress: (id: number) => void;
}

export function RequisitionCard({ item, onPress }: Props) {
  return (
    <Pressable
      onPress={() => onPress(item.id)}
      className="bg-white border border-slate-200 rounded-md p-3 mb-2 active:bg-slate-50"
    >
      <View className="flex-row items-center justify-between mb-2">
        <Text className="text-base font-semibold text-slate-900">{item.refNo}</Text>
        <StatusPill status={item.status} />
      </View>
      <Text className="text-sm text-slate-700 mb-1" numberOfLines={1}>
        {item.customerName}
      </Text>
      <View className="flex-row justify-between">
        <Text className="text-xs text-slate-500">
          {item.itemCount} {item.itemCount === 1 ? "item" : "items"} · {item.currencyCode}
        </Text>
        <Text className="text-xs text-slate-500">{formatShortDate(item.createdAt)}</Text>
      </View>
    </Pressable>
  );
}
