import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAuth } from "@/auth/AuthContext";
import { useAccountantDashboardStats } from "@/api/stats";
import { useUnreadCount } from "@/api/notifications";
import { ScreenHeader } from "@/components/ScreenHeader";
import { Skeleton } from "@/components/Skeleton";
import { NotificationBell } from "@/components/NotificationBell";

function greet(): string {
  const h = new Date().getHours();
  if (h < 12) return "Good morning";
  if (h < 17) return "Good afternoon";
  if (h < 21) return "Good evening";
  return "Good night";
}

function startOfMonthIsoDate(): string {
  // UTC start-of-month — must match backend's `new DateTime(UtcNow.Year, UtcNow.Month, 1, Utc)`.
  // Building in LOCAL time and ISO-formatting causes a mismatch near month boundaries.
  const now = new Date();
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), 1)).toISOString().slice(0, 10);
}

export default function AccountantDashboard() {
  const router = useRouter();
  const { user, logout } = useAuth();
  const insets = useSafeAreaInsets();
  const statsQ = useAccountantDashboardStats();
  const unreadQ = useUnreadCount();

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const firstName = (user?.name ?? "").split(" ")[0] || "there";

  const HeaderRight = (
    <>
      <NotificationBell />
      <Pressable
        onPress={onLogout}
        style={{
          paddingHorizontal: 12,
          paddingVertical: 9,
          borderRadius: 8,
          backgroundColor: "#f1f5f9",
        }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  const navTo = (path: string) => {
    Haptics.selectionAsync();
    router.push(path as Parameters<typeof router.push>[0]);
  };

  const monthStart = startOfMonthIsoDate();

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader label="ACCOUNTANT" title={`${greet()}, ${firstName} 👋`} right={HeaderRight} />

      <ScrollView
        contentContainerStyle={{
          padding: 16,
          paddingTop: 4,
          paddingBottom: Math.max(insets.bottom, 16) + 16,
        }}
        showsVerticalScrollIndicator={false}
      >
        {/* Hero: Pending Costing */}
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 100 }}
        >
          <Pressable
            onPress={() => navTo("/(accountant)/list?onlyStatus=CostingPending")}
            style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
          >
            <View
              style={{
                backgroundColor: "#1e40af",
                borderRadius: 16,
                padding: 20,
                marginBottom: 12,
                shadowColor: "#1e40af",
                shadowOffset: { width: 0, height: 6 },
                shadowOpacity: 0.3,
                shadowRadius: 12,
                elevation: 6,
              }}
            >
              <Text style={{ color: "#dbeafe", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
                PENDING COSTING
              </Text>
              <View style={{ flexDirection: "row", alignItems: "flex-end", marginTop: 10 }}>
                {statsQ.isPending ? (
                  <Skeleton width={80} height={44} radius={8} style={{ backgroundColor: "rgba(255,255,255,0.25)" }} />
                ) : (
                  <Text style={{ color: "#ffffff", fontSize: 44, fontWeight: "800", letterSpacing: -1 }}>
                    {statsQ.data?.pendingCosting ?? 0}
                  </Text>
                )}
                <Text style={{ color: "#dbeafe", fontSize: 15, marginLeft: 10, marginBottom: 8 }}>
                  to review
                </Text>
              </View>
              <Text style={{ color: "#dbeafe", fontSize: 14, marginTop: 14 }}>Tap to open the list →</Text>
            </View>
          </Pressable>
        </MotiView>

        {/* Row: In Progress */}
        <KpiRow
          label="IN PROGRESS"
          value={statsQ.data?.inProgress ?? 0}
          loading={statsQ.isPending}
          delay={180}
          onPress={() => navTo("/(accountant)/list?onlyStatus=CostingInProgress")}
        />

        {/* Row: Submitted This Month */}
        <KpiRow
          label="MD-BOUND THIS MONTH"
          value={statsQ.data?.submittedThisMonth ?? 0}
          loading={statsQ.isPending}
          delay={260}
          onPress={() => navTo(`/(accountant)/list?chip=MD%20review&from=${monthStart}`)}
        />

        {/* Row: Awaiting MD */}
        <KpiRow
          label="AWAITING MD"
          value={statsQ.data?.awaitingMd ?? 0}
          loading={statsQ.isPending}
          delay={340}
          onPress={() => navTo("/(accountant)/list?chip=MD%20review")}
        />

        {/* Notifications card */}
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 420 }}
        >
          <Pressable
            onPress={() => navTo("/notifications")}
            style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
          >
            <View
              style={{
                backgroundColor: "#ffffff",
                borderWidth: 1,
                borderColor: "#e2e8f0",
                borderRadius: 14,
                padding: 16,
                marginTop: 4,
                marginBottom: 12,
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "space-between",
              }}
            >
              <View style={{ flex: 1 }}>
                <Text style={{ color: "#64748b", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
                  NOTIFICATIONS
                </Text>
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

        {/* User card */}
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 500 }}
        >
          <View
            style={{
              backgroundColor: "#ffffff",
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 14,
              padding: 16,
            }}
          >
            <Text style={{ color: "#64748b", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
              SIGNED IN AS
            </Text>
            <Text style={{ color: "#0f172a", fontSize: 17, fontWeight: "700", marginTop: 6 }}>
              {user?.name ?? "—"}
            </Text>
            <Text style={{ color: "#64748b", fontSize: 14, marginTop: 2 }}>Accountant</Text>
          </View>
        </MotiView>
      </ScrollView>
    </View>
  );
}

function KpiRow({
  label, value, loading, delay, onPress,
}: {
  label: string;
  value: number;
  loading: boolean;
  delay: number;
  onPress: () => void;
}) {
  return (
    <MotiView
      from={{ opacity: 0, translateY: 14 }}
      animate={{ opacity: 1, translateY: 0 }}
      transition={{ type: "spring", damping: 14, stiffness: 140, delay }}
    >
      <Pressable
        onPress={onPress}
        style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
      >
        <View
          style={{
            backgroundColor: "#ffffff",
            borderWidth: 1,
            borderColor: "#e2e8f0",
            borderRadius: 14,
            padding: 14,
            marginBottom: 8,
            flexDirection: "row",
            alignItems: "center",
            justifyContent: "space-between",
          }}
        >
          <Text style={{ color: "#64748b", fontSize: 12, fontWeight: "700", letterSpacing: 0.5 }}>
            {label}
          </Text>
          {loading ? (
            <Skeleton width={36} height={26} />
          ) : (
            <Text style={{ color: "#0f172a", fontSize: 22, fontWeight: "800" }}>{value}</Text>
          )}
        </View>
      </Pressable>
    </MotiView>
  );
}
