import { Pressable, Text, View } from "react-native";

interface Props {
  message: string;
  onRetry?: () => void;
}

export function ErrorBanner({ message, onRetry }: Props) {
  return (
    <View className="bg-rose-50 border border-rose-200 rounded-md p-3 mb-3">
      <Text className="text-rose-800 text-sm">{message}</Text>
      {onRetry ? (
        <Pressable onPress={onRetry} className="mt-2">
          <Text className="text-rose-700 font-semibold text-sm">Retry</Text>
        </Pressable>
      ) : null}
    </View>
  );
}
