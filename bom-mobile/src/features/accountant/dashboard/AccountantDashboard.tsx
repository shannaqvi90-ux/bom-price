import { useCallback, useState } from "react";
import { Pressable, RefreshControl, ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAuth } from "../../../auth/AuthContext";
import { useAccountantDashboardV3 } from "../../../api/stats";
import { useUnreadCount } from "../../../api/notifications";
import { ScreenHeader } from "../../../components/ScreenHeader";
import { Skeleton } from "../../../components/Skeleton";
import { NotificationBell } from "../../../components/NotificationBell";
import { KpiHeroCard } from "./KpiHeroCard";
import { KpiRow } from "./KpiRow";

function greet(): string {
  const h = new Date().getHours();
  if (h < 12) return "Good morning";
  if (h < 17) return "Good afternoon";
  if (h < 21) return "Good evening";
  return "Good night";
}

function startOfMonthIsoDate(): string {
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)).toISOString().slice(0, 10);
}

export function AccountantDashboard() {
  const router = useRouter();
  const { user, logout } = useAuth();
  const insets = useSafeAreaInsets();
  const statsQ = useAccountantDashboardV3();
  const unreadQ = useUnreadCount();
  const [refreshing, setRefreshing] = useState(false);

  const onRefresh = useCallback(async () => {
    setRefreshing(true);
    try { await Promise.all([statsQ.refetch(), unreadQ.refetch()]); }
    finally { setRefreshing(false); }
  }, [statsQ, unreadQ]);

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const firstName = (user?.name ?? "").split(" ")[0] || "there";
  const monthStart = startOfMonthIsoDate();

  const navTo = (path: string) => {
    Haptics.selectionAsync();
    router.push(path as Parameters<typeof router.push>[0]);
  };

  const HeaderRight = (
    <>
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

      <ScreenHeader label="ACCOUNTANT" title="Accountant Dashboard" right={HeaderRight} />

      <ScrollView
        contentContainerStyle={{ padding: 16, paddingTop: 4, paddingBottom: Math.max(insets.bottom, 16) + 16 }}
        showsVerticalScrollIndicator={false}
        refreshControl={
          <RefreshControl refreshing={refreshing} onRefresh={onRefresh} tintColor="#1e40af" colors={["#1e40af"]} />
        }
      >
        <Text
          style={{ color: "#475569", fontSize: 16, fontWeight: "500", marginBottom: 12 }}
          numberOfLines={2}
        >
          {greet()}, {firstName} 👋
        </Text>

        <KpiHeroCard
          count={statsQ.data?.costing}
          loading={statsQ.isPending}
          onPress={() => navTo("/(accountant)/list?tab=queue")}
        />

        <KpiRow
          label="AWAITING MD"
          value={statsQ.data?.awaitingMd}
          loading={statsQ.isPending}
          delay={180}
          onPress={() => navTo("/(accountant)/list?tab=in-flight&filter=md")}
        />

        <KpiRow
          label="AWAITING CUSTOMER"
          value={statsQ.data?.awaitingCustomer}
          loading={statsQ.isPending}
          delay={260}
          onPress={() => navTo("/(accountant)/list?tab=in-flight&filter=customer")}
        />

        <KpiRow
          label="MD-BOUND THIS MONTH"
          value={statsQ.data?.submittedThisMonth}
          loading={statsQ.isPending}
          delay={340}
          onPress={() => navTo(`/(accountant)/list?tab=in-flight&from=${monthStart}`)}
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
            <Text style={{ color: "#64748b", fontSize: 14, marginTop: 2 }}>Accountant</Text>
          </View>
        </MotiView>
      </ScrollView>
    </View>
  );
}
