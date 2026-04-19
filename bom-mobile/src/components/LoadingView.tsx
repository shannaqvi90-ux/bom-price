import { ActivityIndicator, View } from "react-native";

export function LoadingView() {
  return (
    <View className="flex-1 items-center justify-center">
      <ActivityIndicator size="large" color="#4f46e5" />
    </View>
  );
}
