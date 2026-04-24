import { Text, View } from "react-native";

interface Props {
  pricePerKg: number;
  totalCostPerKg: number;
  currencyCode: string;
}

export function MarginHeroCard({ pricePerKg, totalCostPerKg, currencyCode }: Props) {
  const marginPct = pricePerKg > 0 ? ((pricePerKg - totalCostPerKg) / pricePerKg) * 100 : 0;
  const negative = marginPct < 0;

  return (
    <View
      style={{
        backgroundColor: negative ? "#fef2f2" : "#ecfdf5",
        borderWidth: 1,
        borderColor: negative ? "#fecaca" : "#a7f3d0",
        borderRadius: 14,
        padding: 14,
        marginTop: 10,
        alignItems: "center",
      }}
    >
      <Text
        style={{
          fontSize: 22,
          fontWeight: "800",
          color: negative ? "#991b1b" : "#047857",
        }}
      >
        {`Margin ${marginPct.toFixed(1)}%`}
      </Text>
      <Text
        style={{
          fontSize: 13,
          color: negative ? "#7f1d1d" : "#065f46",
          marginTop: 4,
        }}
      >
        {`Price ${pricePerKg.toFixed(4)} · Cost ${totalCostPerKg.toFixed(4)} ${currencyCode}/kg`}
      </Text>
    </View>
  );
}
