import { useMemo } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitionDetail } from "@/api/requisitions";
// import { useBomReview } from "@/api/bom"; // TODO V3-mobile-D-3: BOM view pending MD phase
import { useMdReview } from "@/api/approvals";
import { useAuth } from "@/auth/AuthContext";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { NotificationBell } from "@/components/NotificationBell";
import { MarginHeroCard } from "@/components/MarginHeroCard";
import { CostBreakdownCard } from "@/components/CostBreakdownCard";
import { BomProcessGroup } from "@/components/BomProcessGroup";
import type { BomLine } from "@/types/api";
import { stripTags } from "@/utils/text";

export default function MdItemDrillDown() {
  const router = useRouter();
  const { logout } = useAuth();
  const params = useLocalSearchParams<{ reqId: string; itemId: string }>();
  const reqId = Number(params.reqId);
  const itemId = Number(params.itemId);

  const detailQ = useRequisitionDetail(reqId);
  // const bomQ = useBomReview(reqId); // TODO V3-mobile-D-3
  const bomQ = { isPending: false, error: null, data: null, refetch: () => {} } as any; // placeholder for deleted bom.ts
  const reviewQ = useMdReview(reqId);

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

  const isLoading = detailQ.isPending || bomQ.isPending || reviewQ.isPending;
  const firstError = detailQ.error ?? bomQ.error ?? reviewQ.error;

  const reqItem = detailQ.data?.items.find((i) => i.id === itemId) ?? null;
  const bomItem = bomQ.data?.items.find((i) => i.requisitionItemId === itemId) ?? null;
  const reviewItem = reviewQ.data?.items.find((i) => i.requisitionItemId === itemId) ?? null;
  const approvalPrice =
    detailQ.data?.approval?.items?.find((a) => a.requisitionItemId === itemId)?.pricePerKg ?? null;

  const linesByProcess = useMemo(() => {
    if (!bomItem) return [] as { processName: string; lines: BomLine[] }[];
    const byName = new Map<string, BomLine[]>();
    for (const l of bomItem.lines) {
      const list = byName.get(l.processName) ?? [];
      list.push(l);
      byName.set(l.processName, list);
    }
    return Array.from(byName, ([processName, lines]) => ({ processName, lines }));
  }, [bomItem]);

  if (isLoading) return <LoadingView />;

  if (firstError || !detailQ.data || !reqItem) {
    const status = (firstError as { response?: { status?: number } } | null)?.response?.status;
    const message =
      status === 403
        ? "Access denied"
        : status === 404 || !reqItem
        ? "Item not found"
        : firstError instanceof Error
        ? firstError.message
        : "Failed to load details";
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Item details" right={HeaderRight} />
        <View style={{ padding: 16 }}>
          <ErrorBanner
            message={message}
            onRetry={() => {
              detailQ.refetch();
              bomQ.refetch();
              reviewQ.refetch();
            }}
          />
        </View>
      </View>
    );
  }

  const r = detailQ.data;
  const showMargin = r.status === "Approved" && approvalPrice != null && reviewItem?.cost != null;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader
        label={`${r.refNo} · ${reqItem.expectedQty} kg`}
        title={stripTags(reqItem.itemDescription)}
        right={HeaderRight}
      />

      <ScrollView contentContainerStyle={{ padding: 14, paddingBottom: 32 }}>
        {showMargin && reviewItem?.cost ? (
          <MarginHeroCard
            pricePerKg={approvalPrice!}
            totalCostPerKg={reviewItem.cost.totalCostPerKg}
            currencyCode="AED"
          />
        ) : null}

        <CostBreakdownCard cost={reviewItem?.cost ?? null} />

        <View
          style={{
            backgroundColor: "#ffffff",
            borderWidth: 1,
            borderColor: "#e2e8f0",
            borderRadius: 14,
            padding: 14,
            marginTop: 12,
          }}
        >
          <Text style={{ fontSize: 14, color: "#94a3b8", textAlign: "center" }}>
            BOM view temporarily disabled — pending V3 mobile D-3 (MD phase)
          </Text>
        </View>
      </ScrollView>
    </View>
  );
}
