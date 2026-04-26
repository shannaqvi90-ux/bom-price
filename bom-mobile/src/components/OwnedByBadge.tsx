import { Text, View } from "react-native";

interface Props {
  ownerName: string;
  prefix?: string;
}

export function OwnedByBadge({ ownerName, prefix = "by" }: Props) {
  return (
    <View>
      <Text style={{ fontSize: 11, color: "#64748b", marginTop: 2 }}>
        {prefix} {ownerName}
      </Text>
    </View>
  );
}
