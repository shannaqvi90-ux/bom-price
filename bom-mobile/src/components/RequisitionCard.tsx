import { useState } from "react";
import { Pressable, Text, View } from "react-native";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { StatusPill } from "./StatusPill";
import type { RequisitionListItem } from "@/types/api";
import { formatShortDate } from "@/utils/dates";

interface Props {
  item: RequisitionListItem;
  onPress: (id: number) => void;
}

export function RequisitionCard({ item, onPress }: Props) {
  const [pressed, setPressed] = useState(false);

  return (
    <Pressable
      onPressIn={() => {
        setPressed(true);
        Haptics.selectionAsync();
      }}
      onPressOut={() => setPressed(false)}
      onPress={() => onPress(item.id)}
    >
      <MotiView
        animate={{ scale: pressed ? 0.98 : 1 }}
        transition={{ type: "spring", damping: 16, stiffness: 280 }}
        style={{
          backgroundColor: "#ffffff",
          borderWidth: 1,
          borderColor: "#e2e8f0",
          borderRadius: 14,
          padding: 14,
          marginBottom: 10,
          shadowColor: "#000",
          shadowOffset: { width: 0, height: 1 },
          shadowOpacity: 0.04,
          shadowRadius: 3,
          elevation: 1,
        }}
      >
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            alignItems: "center",
            marginBottom: 6,
          }}
        >
          <Text style={{ fontSize: 16, fontWeight: "700", color: "#0f172a" }}>
            {item.refNo}
          </Text>
          <StatusPill status={item.status as Parameters<typeof StatusPill>[0]["status"]} />
        </View>
        <Text
          style={{ fontSize: 15, color: "#475569", marginBottom: 8 }}
          numberOfLines={1}
        >
          {item.customerName}
        </Text>
        <View
          style={{
            flexDirection: "row",
            justifyContent: "space-between",
            alignItems: "center",
          }}
        >
          <Text style={{ fontSize: 13, color: "#94a3b8" }}>
            {item.itemCount} {item.itemCount === 1 ? "item" : "items"} · {item.currencyCode}
          </Text>
          <Text style={{ fontSize: 13, color: "#94a3b8" }}>
            {formatShortDate(item.createdAt)}
          </Text>
        </View>
      </MotiView>
    </Pressable>
  );
}
