import { Text, View } from "react-native";
import { formatCurrency } from "@/utils/numbers";

interface Props {
  daysAgo: number;
  costPerKg: number;
  currencyCode: string;
}

export function StaleCostBadge({ daysAgo, costPerKg, currencyCode }: Props) {
  return (
    <View
      style={{
        marginTop: 8,
        backgroundColor: "#fef2f2",
        paddingVertical: 6,
        paddingHorizontal: 10,
        borderRadius: 6,
      }}
    >
      <Text style={{ color: "#dc2626", fontSize: 12 }}>
        ⚠ Last cost {formatCurrency(costPerKg, currencyCode)} · {daysAgo} days ago (stale)
      </Text>
    </View>
  );
}
