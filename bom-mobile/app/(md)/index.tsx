import { Text, View } from "react-native";
import { useAuth } from "@/auth/AuthContext";

export default function MdHome() {
  const { user } = useAuth();
  return (
    <View className="flex-1 items-center justify-center p-6">
      <Text className="text-xl font-semibold text-slate-900">
        Hello, {user?.name ?? "MD"}
      </Text>
      <Text className="text-slate-600 mt-2">Pending approvals coming next plan.</Text>
    </View>
  );
}
