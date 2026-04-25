import { useEffect, useRef, useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useCostingReview, useStartCostingItem } from "@/api/costing";
import { useRequisitionDetail } from "@/api/requisitions";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { ItemCardShell } from "@/components/ItemCardShell";
import { StatusPill } from "@/components/StatusPill";
import { NotificationBell } from "@/components/NotificationBell";
import { HistoricalRequisitionScreen } from "@/components/HistoricalRequisitionScreen";
import { CustomerSwapSheet } from "@/components/CustomerSwapSheet";

const COST_STATUS_COLORS: Record<string, { bg: string; fg: string; label: string }> = {
  NotStarted: { bg: "#f1f5f9", fg: "#64748b", label: "Not started" },
  InProgress: { bg: "#fef3c7", fg: "#b45309", label: "In progress" },
  Submitted:  { bg: "#dcfce7", fg: "#15803d", label: "Submitted" },
};

function CostStatusPill({ status }: { status: string }) {
  const c = COST_STATUS_COLORS[status] ?? COST_STATUS_COLORS.NotStarted;
  return (
    <View
      style={{
        alignSelf: "flex-start",
        backgroundColor: c.bg,
        paddingVertical: 3,
        paddingHorizontal: 8,
        borderRadius: 10,
        marginTop: 6,
      }}
    >
      <Text style={{ color: c.fg, fontSize: 11, fontWeight: "700" }}>{c.label}</Text>
    </View>
  );
}

export default function AccountantReqDetail() {
  const router = useRouter();
  const { logout } = useAuth();
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);

  const reqQ = useRequisitionDetail(id);
  const costQ = useCostingReview(id);
  const startItem = useStartCostingItem(id);
  const hasAutoStarted = useRef(false);

  useEffect(() => {
    if (
      !hasAutoStarted.current &&
      reqQ.data?.status === "CostingPending" &&
      costQ.data &&
      costQ.data.items.length > 0
    ) {
      const first = costQ.data.items[0];
      if (first.costStatus === "NotStarted") {
        hasAutoStarted.current = true;
        startItem.mutate(first.requisitionItemId);
      }
    }
  }, [reqQ.data?.status, costQ.data, startItem]);

  // Auto-pop to pending list 2 s after the requisition flips to MdReview
  // (last item submitted). Plan §7 / smoke M10.
  const allSubmitted = costQ.data?.items.every((i) => i.costStatus === "Submitted") ?? false;
  const reqStatus = reqQ.data?.status;
  const [swapOpen, setSwapOpen] = useState(false);
  useEffect(() => {
    if (allSubmitted && reqStatus === "MdReview") {
      const t = setTimeout(() => router.replace("/(accountant)"), 2000);
      return () => clearTimeout(t);
    }
  }, [allSubmitted, reqStatus, router]);

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
        style={{ paddingHorizontal: 12, paddingVertical: 9, borderRadius: 8, backgroundColor: "#f1f5f9" }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  if (reqQ.isPending || costQ.isPending) return <LoadingView />;
  if (reqQ.isError || costQ.isError || !reqQ.data || !costQ.data) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Requisition" back right={HeaderRight} />
        <ErrorBanner
          message="Failed to load requisition"
          onRetry={() => { reqQ.refetch(); costQ.refetch(); }}
        />
      </View>
    );
  }

  const r = reqQ.data;

  // Smart-route branch: read-only historical for non-active statuses.
  const isCostingActive =
    r.status === "CostingPending" || r.status === "CostingInProgress";
  if (!isCostingActive) {
    return <HistoricalRequisitionScreen requisitionId={id} routePrefix="/(accountant)" />;
  }

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title={r.refNo} back right={HeaderRight} />
      <ScrollView contentContainerStyle={{ padding: 16, gap: 10 }}>
        <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
          <Text style={{ fontSize: 14, color: "#64748b", flex: 1 }} numberOfLines={1}>
            {r.customerName}
          </Text>
          {r.status !== "Draft" ? <StatusPill status={r.status} /> : null}
        </View>

        {r.status === "CostingPending" || r.status === "CostingInProgress" ? (
          <View style={{ marginTop: 4, marginBottom: 8 }}>
            <Pressable
              onPress={() => setSwapOpen(true)}
              style={{
                alignSelf: "flex-start",
                paddingHorizontal: 12,
                paddingVertical: 8,
                borderRadius: 8,
                borderWidth: 1,
                borderColor: "#1e40af",
                backgroundColor: "#eff6ff",
              }}
            >
              <Text style={{ color: "#1e40af", fontWeight: "600", fontSize: 13 }}>Change customer</Text>
            </Pressable>
          </View>
        ) : null}

        {allSubmitted && r.status === "MdReview" ? (
          <View
            style={{
              backgroundColor: "#dcfce7",
              padding: 12,
              borderRadius: 8,
              marginVertical: 4,
            }}
          >
            <Text style={{ color: "#15803d", fontWeight: "700", fontSize: 14 }}>
              ✓ All items submitted — sent to MD review
            </Text>
          </View>
        ) : null}

        {costQ.data.items.map((it) => {
          const canDrillIn = it.costStatus !== "Submitted" && r.status !== "MdReview";
          return (
            <Pressable
              key={it.requisitionItemId}
              onPress={() => {
                if (!canDrillIn) return;
                Haptics.selectionAsync();
                router.push(`/(accountant)/item/${id}/${it.requisitionItemId}`);
              }}
              disabled={!canDrillIn}
              style={({ pressed }) => ({ opacity: pressed && canDrillIn ? 0.85 : 1 })}
            >
              <ItemCardShell>
                <Text style={{ fontSize: 15, fontWeight: "600" }}>{it.itemDescription}</Text>
                <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
                  Expected qty: {it.expectedQty}
                </Text>
                <CostStatusPill status={it.costStatus} />
                {canDrillIn ? (
                  <Text style={{ marginTop: 8, color: "#1e40af", fontSize: 13, fontWeight: "600" }}>
                    Edit costing ▸
                  </Text>
                ) : null}
              </ItemCardShell>
            </Pressable>
          );
        })}
      </ScrollView>

      <CustomerSwapSheet
        requisitionId={id}
        currentCustomerId={r.customerId}
        currentCustomerName={r.customerName}
        open={swapOpen}
        onClose={() => setSwapOpen(false)}
      />
    </View>
  );
}
