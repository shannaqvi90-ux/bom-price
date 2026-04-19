import { Text, View } from "react-native";
import { colors } from "@/theme/tokens";

type Status = keyof typeof colors.status;

export function StatusPill({ status }: { status: Status }) {
  return (
    <View style={{ backgroundColor: colors.status[status] }} className="px-2 py-1 rounded-full self-start">
      <Text className="text-xs font-semibold text-white">{status}</Text>
    </View>
  );
}
