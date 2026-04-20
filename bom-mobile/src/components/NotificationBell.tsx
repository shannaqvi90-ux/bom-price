import { Pressable, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useUnreadCount } from "@/api/notifications";

export function NotificationBell() {
  const router = useRouter();
  const q = useUnreadCount();
  const count = q.data ?? 0;

  return (
    <Pressable onPress={() => router.push("/notifications")} className="pr-3">
      <View className="relative py-1">
        <Text className="text-brand-600 text-base font-semibold">🔔</Text>
        {count > 0 ? (
          <View className="absolute -top-1 -right-2 bg-rose-600 rounded-full min-w-[18px] h-[18px] items-center justify-center px-1">
            <Text className="text-white text-[10px] font-bold">
              {count > 99 ? "99+" : count}
            </Text>
          </View>
        ) : null}
      </View>
    </Pressable>
  );
}
