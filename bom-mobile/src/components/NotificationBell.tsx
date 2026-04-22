import { useEffect, useRef, useState } from "react";
import { Pressable, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useUnreadCount } from "@/api/notifications";

export function NotificationBell() {
  const router = useRouter();
  const q = useUnreadCount();
  const count = q.data ?? 0;
  const prevCountRef = useRef<number | undefined>(undefined);
  const [wiggleKey, setWiggleKey] = useState(0);

  useEffect(() => {
    if (!q.isSuccess) return;
    if (prevCountRef.current !== undefined && count > prevCountRef.current) {
      setWiggleKey((k) => k + 1);
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    }
    prevCountRef.current = count;
  }, [count, q.isSuccess]);

  return (
    <Pressable
      onPress={() => router.push("/notifications")}
      style={{ paddingRight: 12 }}
    >
      <MotiView
        key={wiggleKey}
        from={{ rotate: "0deg" }}
        animate={{ rotate: ["0deg", "-15deg", "15deg", "-8deg", "0deg"] }}
        transition={{ type: "timing", duration: 400 }}
        style={{ position: "relative", paddingVertical: 4 }}
      >
        <Text style={{ color: "#1e40af", fontSize: 22, fontWeight: "600" }}>
          🔔
        </Text>
        {count > 0 ? (
          <View
            style={{
              position: "absolute",
              top: -4,
              right: -8,
              backgroundColor: "#dc2626",
              borderRadius: 11,
              minWidth: 22,
              height: 22,
              alignItems: "center",
              justifyContent: "center",
              paddingHorizontal: 4,
            }}
          >
            <Text style={{ color: "#ffffff", fontSize: 12, fontWeight: "700" }}>
              {count > 99 ? "99+" : count}
            </Text>
          </View>
        ) : null}
      </MotiView>
    </Pressable>
  );
}
