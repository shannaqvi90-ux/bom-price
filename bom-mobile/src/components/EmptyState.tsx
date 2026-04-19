import { Text, View } from "react-native";

export function EmptyState({ title, hint }: { title: string; hint?: string }) {
  return (
    <View className="flex-1 items-center justify-center p-8">
      <Text className="text-lg font-semibold text-slate-700">{title}</Text>
      {hint ? <Text className="text-sm text-slate-500 mt-2 text-center">{hint}</Text> : null}
    </View>
  );
}
