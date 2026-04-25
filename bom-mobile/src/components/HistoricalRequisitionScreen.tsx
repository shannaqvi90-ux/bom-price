import { useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitionDetail } from "@/api/requisitions";
import { downloadRequisitionPdf } from "@/api/pdf";
import { Button } from "@/components/Button";
import { StatusPill } from "@/components/StatusPill";
import { ItemStageBadge } from "@/components/ItemStageBadge";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { SectionCard } from "@/components/SectionCard";
import { ItemCardShell } from "@/components/ItemCardShell";
import { ItemPriceBlock } from "@/components/ItemPriceBlock";
import { NotificationBell } from "@/components/NotificationBell";
import { useAuth } from "@/auth/AuthContext";
import { formatShortDate } from "@/utils/dates";

export function HistoricalRequisitionScreen({
  requisitionId,
  routePrefix = "/(md)",
}: {
  requisitionId: number;
  routePrefix?: string;
}) {
  const router = useRouter();
  const { logout } = useAuth();
  const id = requisitionId;
  const q = useRequisitionDetail(id);
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [pdfLoading, setPdfLoading] = useState(false);

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const MdHeaderRight = (
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
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>
          Log out
        </Text>
      </Pressable>
    </>
  );

  if (q.isPending) return <LoadingView />;

  if (q.isError || !q.data) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ScreenHeader title="Requisition" right={MdHeaderRight} />
        <View style={{ padding: 16 }}>
          <ErrorBanner
            message={
              q.error instanceof Error ? q.error.message : "Failed to load requisition"
            }
            onRetry={() => q.refetch()}
          />
        </View>
      </View>
    );
  }

  const r = q.data;
  const isApproved = r.status === "Approved";
  const isRejected = r.status === "Rejected";

  const onDownload = async () => {
    setPdfError(null);
    setPdfLoading(true);
    try {
      await downloadRequisitionPdf(r.id, r.refNo);
    } catch (e: unknown) {
      setPdfError(e instanceof Error ? e.message : "PDF download failed");
    } finally {
      setPdfLoading(false);
    }
  };

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader
        label="QUOTATION"
        title={r.refNo}
        right={
          <>
            <StatusPill status={r.status as Parameters<typeof StatusPill>[0]["status"]} />
            {MdHeaderRight}
          </>
        }
      />

      <ScrollView contentContainerStyle={{ padding: 14, paddingBottom: 32 }}>
        {isRejected && r.approval?.notes ? (
          <View
            style={{
              backgroundColor: "#fef2f2",
              borderWidth: 1,
              borderColor: "#fecaca",
              borderRadius: 14,
              padding: 14,
              marginBottom: 12,
            }}
          >
            <Text
              style={{
                fontSize: 13,
                fontWeight: "700",
                color: "#991b1b",
                letterSpacing: 0.3,
                marginBottom: 6,
              }}
            >
              REJECTION REASON
            </Text>
            <Text style={{ fontSize: 15, color: "#7f1d1d" }}>{r.approval.notes}</Text>
          </View>
        ) : null}

        <SectionCard title="Customer">
          <Text style={{ fontSize: 16, fontWeight: "600", color: "#0f172a" }}>
            {r.customerName}
          </Text>
          <Text style={{ fontSize: 13, color: "#64748b", marginTop: 4 }}>
            {r.branchName} · {r.currencyCode} · Created {formatShortDate(r.createdAt)}
          </Text>
        </SectionCard>

        <Text
          style={{
            fontSize: 13,
            fontWeight: "700",
            color: "#64748b",
            marginBottom: 8,
            marginTop: 4,
            letterSpacing: 0.3,
          }}
        >
          {`ITEMS (${r.items.length})`}
        </Text>

        {r.items.map((it) => {
          const canDrillDown = r.status === "Approved" || r.status === "Rejected";
          return (
            <ItemCardShell key={it.id}>
              <View
                style={{
                  flexDirection: "row",
                  alignItems: "flex-start",
                  justifyContent: "space-between",
                }}
              >
                <Text
                  style={{ flex: 1, paddingRight: 12, fontSize: 15, fontWeight: "600", color: "#0f172a" }}
                  numberOfLines={2}
                >
                  {it.itemDescription}
                </Text>
                <Text style={{ fontSize: 15, color: "#334155", fontWeight: "600" }}>
                  {it.expectedQty}
                </Text>
              </View>
              <View style={{ marginTop: 6 }}>
                <ItemStageBadge status={r.status} />
              </View>
              {r.status === "Approved" && r.approval?.items
                ? (() => {
                    const approvalItem = r.approval.items?.find((ai) => ai.requisitionItemId === it.id);
                    return approvalItem ? (
                      <ItemPriceBlock
                        expectedQty={it.expectedQty}
                        pricePerKg={approvalItem.pricePerKg}
                        currencyCode={r.currencyCode}
                      />
                    ) : null;
                  })()
                : null}
              {canDrillDown ? (
                <Pressable
                  onPress={() => {
                    Haptics.selectionAsync();
                    router.push(`${routePrefix}/item/${r.id}/${it.id}` as Parameters<typeof router.push>[0]);
                  }}
                  style={({ pressed }) => ({
                    marginTop: 10,
                    opacity: pressed ? 0.7 : 1,
                  })}
                >
                  <View
                    style={{
                      backgroundColor: "#eff6ff",
                      borderRadius: 10,
                      paddingVertical: 10,
                      alignItems: "center",
                    }}
                  >
                    <Text style={{ color: "#1e40af", fontSize: 14, fontWeight: "700" }}>
                      View details ▸
                    </Text>
                  </View>
                </Pressable>
              ) : null}
            </ItemCardShell>
          );
        })}

        {isApproved ? (
          <View style={{ marginTop: 20 }}>
            {pdfError ? (
              <ErrorBanner message={pdfError} onRetry={() => setPdfError(null)} />
            ) : null}
            <Button
              title={pdfLoading ? "Preparing PDF..." : "Download PDF"}
              onPress={onDownload}
              loading={pdfLoading}
            />
          </View>
        ) : null}

        {r.approval && isApproved ? (
          <Text
            style={{
              fontSize: 12,
              color: "#64748b",
              textAlign: "center",
              marginTop: 12,
            }}
          >
            Approved on {formatShortDate(r.approval.approvedAt)}
          </Text>
        ) : null}
      </ScrollView>
    </View>
  );
}
