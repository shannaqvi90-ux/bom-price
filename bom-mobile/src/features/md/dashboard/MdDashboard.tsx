import { useCallback, useState } from "react";
import { Pressable, RefreshControl, ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAuth } from "@/auth/AuthContext";
import { useMdDashboard } from "@/api/stats";
import { useOwnSignature } from "@/features/profile/api/signature";
import { useUnreadCount } from "@/api/notifications";
import { ScreenHeader } from "@/components/ScreenHeader";
import { Skeleton } from "@/components/Skeleton";
import { NotificationBell } from "@/components/NotificationBell";
import { KpiRow } from "@/features/accountant/dashboard/KpiRow";
import { SignatureMissingBanner } from "./SignatureMissingBanner";

function greet(): string {
  const h = new Date().getHours();
  if (h < 12) return "Good morning";
  if (h < 17) return "Good afternoon";
  if (h < 21) return "Good evening";
  return "Good night";
}

export function MdDashboard() {
  const router = useRouter();
  const { user, logout } = useAuth();
  const insets = useSafeAreaInsets();
  const dashQ = useMdDashboard();
  const sigQ = useOwnSignature();
  const unreadQ = useUnreadCount();
  const [refreshing, setRefreshing] = useState(false);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    try { await Promise.all([dashQ.refetch(), sigQ.refetch(), unreadQ.refetch()]); }
    finally { setRefreshing(false); }
  }, [dashQ, sigQ, unreadQ]);

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const firstName = (user?.name ?? "").split(" ")[0] || "there";

  const navTo = (path: string) => {
    Haptics.selectionAsync();
    router.push(path as Parameters<typeof router.push>[0]);
  };

  const HeaderRight = (
    <>
      <Pressable
        onPress={() => {
          Haptics.selectionAsync();
          router.push("/profile");
        }}
        hitSlop={6}
        style={{ paddingHorizontal: 8, paddingVertical: 6 }}
      >
        <Text style={{ fontSize: 22 }}>👤</Text>
      </Pressable>
      <NotificationBell />
      <Pressable
        onPress={onLogout}
        style={{ paddingHorizontal: 12, paddingVertical: 9, borderRadius: 8, backgroundColor: "#f1f5f9" }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader label="MANAGING DIRECTOR" title={`${greet()}, ${firstName} 👋`} right={HeaderRight} />

      <ScrollView
        contentContainerStyle={{ padding: 16, paddingTop: 4, paddingBottom: Math.max(insets.bottom, 16) + 16 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#1e40af" colors={["#1e40af"]} />
        }
      >
        {sigQ.data && !sigQ.data.exists ? <SignatureMissingBanner /> : null}

        {/* Hero: To price */}
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 100 }}
        >
          <Pressable
            onPress={() => navTo("/(md)/list?tab=queue")}
            style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
          >
            <View style={{
              backgroundColor: "#1e40af",
              borderRadius: 16,
              padding: 20,
              marginBottom: 12,
              shadowColor: "#1e40af",
              shadowOffset: { width: 0, height: 6 },
              shadowOpacity: 0.3,
              shadowRadius: 12,
              elevation: 6,
            }}>
              <Text style={{ color: "#dbeafe", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
                📋  TO PRICE
              </Text>
              <View style={{ flexDirection: "row", alignItems: "flex-end", marginTop: 10 }}>
                {dashQ.isPending ? (
                  <Skeleton width={80} height={44} radius={8} style={{ backgroundColor: "rgba(255,255,255,0.25)" }} />
                ) : (
                  <Text style={{ color: "#ffffff", fontSize: 44, fontWeight: "800", letterSpacing: -1 }}>
                    {dashQ.data?.toPrice ?? 0}
                  </Text>
                )}
                <Text style={{ color: "#dbeafe", fontSize: 15, marginLeft: 10, marginBottom: 8 }}>
                  to review
                </Text>
              </View>
              <Text style={{ color: "#dbeafe", fontSize: 14, marginTop: 14 }}>
                Tap to open the queue →
              </Text>
            </View>
          </Pressable>
        </MotiView>

        <KpiRow
          label="TO SIGN"
          value={dashQ.data?.toSign}
          loading={dashQ.isPending}
          delay={180}
          onPress={() => navTo("/(md)/list?tab=queue")}
        />

        <KpiRow
          label="IN FLIGHT"
          value={dashQ.data?.inFlight}
          loading={dashQ.isPending}
          delay={260}
          onPress={() => navTo("/(md)/list?tab=in-flight")}
        />

        <KpiRow
          label="SIGNED TODAY"
          value={dashQ.data?.signedToday}
          loading={dashQ.isPending}
          delay={340}
          onPress={() => navTo("/(md)/list?tab=done")}
        />

        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 420 }}
        >
          <Pressable
            onPress={() => navTo("/notifications")}
            style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
          >
            <View style={{
              backgroundColor: "#ffffff",
              borderWidth: 1, borderColor: "#e2e8f0",
              borderRadius: 14, padding: 16, marginTop: 4, marginBottom: 12,
              flexDirection: "row", alignItems: "center", justifyContent: "space-between",
            }}>
              <View style={{ flex: 1 }}>
                <Text style={{ color: "#64748b", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>NOTIFICATIONS</Text>
                <View style={{ flexDirection: "row", alignItems: "baseline", marginTop: 4 }}>
                  {unreadQ.isPending ? (
                    <Skeleton width={40} height={24} />
                  ) : (
                    <Text style={{ color: "#0f172a", fontSize: 22, fontWeight: "700" }}>
                      {unreadQ.data ?? 0}
                    </Text>
                  )}
                  <Text style={{ color: "#64748b", fontSize: 14, marginLeft: 8 }}>unread</Text>
                </View>
              </View>
              <Text style={{ fontSize: 28 }}>🔔</Text>
            </View>
          </Pressable>
        </MotiView>

        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 500 }}
        >
          <View style={{
            backgroundColor: "#ffffff",
            borderWidth: 1, borderColor: "#e2e8f0",
            borderRadius: 14, padding: 16,
          }}>
            <Text style={{ color: "#64748b", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>SIGNED IN AS</Text>
            <Text style={{ color: "#0f172a", fontSize: 17, fontWeight: "700", marginTop: 6 }}>{user?.name ?? "—"}</Text>
            <Text style={{ color: "#64748b", fontSize: 14, marginTop: 2 }}>Managing Director</Text>
          </View>
        </MotiView>
      </ScrollView>
    </View>
  );
}
