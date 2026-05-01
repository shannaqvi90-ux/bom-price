import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { Pressable, Text, View } from "react-native";
import * as Haptics from "expo-haptics";
import { useRequisition } from "../../../api/requisitions";
import { useAuth } from "../../../auth/AuthContext";
import { LoadingView } from "../../../components/LoadingView";
import { ErrorBanner } from "../../../components/ErrorBanner";
import { ScreenHeader } from "../../../components/ScreenHeader";
import { NotificationBell } from "../../../components/NotificationBell";
import { ActiveMdPricingView } from "./ActiveMdPricingView";
import { ActiveMdFinalSignView } from "./ActiveMdFinalSignView";
import { ReadonlyMdView } from "./ReadonlyMdView";

const V3_STATUSES = [
  "Draft",
  "Costing",
  "MdPricing",
  "CustomerConfirm",
  "MdFinalSign",
  "Signed",
  "Rejected",
  "Cancelled",
];

export function MdDetailScreen() {
  const router = useRouter();
  const { logout } = useAuth();
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);
  const reqQ = useRequisition(id);

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

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

  if (reqQ.isPending) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Requisition" back right={HeaderRight} />
        <LoadingView />
      </View>
    );
  }

  if (reqQ.isError || !reqQ.data) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Requisition" back right={HeaderRight} />
        <ErrorBanner message="Failed to load requisition" onRetry={() => reqQ.refetch()} />
      </View>
    );
  }

  const req = reqQ.data;

  if (!V3_STATUSES.includes(req.status)) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title={req.refNo} back right={HeaderRight} />
        <View style={{ padding: 24 }}>
          <Text style={{ fontSize: 15, color: "#0f172a", lineHeight: 22 }}>
            This requisition is in a legacy state — please view on web.
          </Text>
        </View>
      </View>
    );
  }

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title={req.refNo} back right={HeaderRight} />
      {req.status === "MdPricing" ? (
        <ActiveMdPricingView req={req} />
      ) : req.status === "MdFinalSign" ? (
        <ActiveMdFinalSignView req={req} />
      ) : (
        <ReadonlyMdView req={req} />
      )}
    </View>
  );
}
