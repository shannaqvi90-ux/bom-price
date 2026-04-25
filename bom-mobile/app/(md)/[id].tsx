import { useEffect, useMemo, useState } from "react";
import {
  KeyboardAvoidingView,
  Platform,
  Pressable,
  ScrollView,
  Text,
  View,
} from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { useMdReview, useApproveRequisition, useRejectRequisition } from "@/api/approvals";
import { useCustomerChangeHistory } from "@/api/requisitions";
import { ApprovalItemRow } from "@/components/ApprovalItemRow";
import { BomDetailSheet } from "@/components/BomDetailSheet";
import { Button } from "@/components/Button";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { CustomerChangeHistorySheet } from "@/components/CustomerChangeHistorySheet";
import { RejectReasonPrompt } from "@/components/RejectReasonPrompt";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { ScreenHeader } from "@/components/ScreenHeader";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { formatMoney } from "@/utils/numbers";
import { stripTags } from "@/utils/text";
import { approveSchema } from "@/utils/validation";

export default function MdApprovalDetail() {
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);
  const router = useRouter();
  const q = useMdReview(id);
  const approveMut = useApproveRequisition();
  const rejectMut = useRejectRequisition();
  const insets = useSafeAreaInsets();

  const [prices, setPrices] = useState<Record<number, number>>({});
  const [itemErrors, setItemErrors] = useState<Record<number, string>>({});
  const [topError, setTopError] = useState<string | null>(null);
  const [approveOpen, setApproveOpen] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);
  const [bomSheetItem, setBomSheetItem] = useState<{
    reqItemId: number;
    desc: string;
  } | null>(null);
  const [historyOpen, setHistoryOpen] = useState(false);
  const historyQ = useCustomerChangeHistory(id, true);
  const historyCount = historyQ.data?.length ?? 0;

  // Initialize price state from backend once data arrives
  useEffect(() => {
    if (!q.data) return;
    setPrices((prev) => {
      if (Object.keys(prev).length > 0) return prev;
      const seed: Record<number, number> = {};
      for (const it of q.data.items) seed[it.requisitionItemId] = 0;
      return seed;
    });
  }, [q.data]);

  const grandTotal = useMemo(() => {
    if (!q.data) return 0;
    return q.data.items.reduce((sum, it) => {
      const p = prices[it.requisitionItemId] ?? 0;
      return sum + p * it.expectedQty;
    }, 0);
  }, [q.data, prices]);

  const onApprove = async () => {
    setTopError(null);
    setItemErrors({});

    const payload = {
      items: q.data!.items.map((it) => ({
        requisitionItemId: it.requisitionItemId,
        salesPricePerKgAed: prices[it.requisitionItemId] ?? 0,
      })),
    };

    const parsed = approveSchema.safeParse(payload);
    if (!parsed.success) {
      const errMap: Record<number, string> = {};
      for (const issue of parsed.error.issues) {
        if (issue.path[0] === "items" && typeof issue.path[1] === "number") {
          const item = q.data!.items[issue.path[1] as number];
          if (item) errMap[item.requisitionItemId] = issue.message;
        }
      }
      setItemErrors(errMap);
      setTopError("Please enter a valid price for every item.");
      setApproveOpen(false);
      return;
    }

    try {
      await approveMut.mutateAsync({ requisitionId: id, payload: parsed.data });
      setApproveOpen(false);
      router.back();
    } catch (e: unknown) {
      setApproveOpen(false);
      const msg =
        (e as { response?: { status?: number; data?: { message?: string } } }).response?.data
          ?.message ??
        (e instanceof Error ? e.message : "Approve failed");
      const status = (e as { response?: { status?: number } }).response?.status;
      if (status === 409) {
        setTopError("This requisition has changed — reloading.");
        q.refetch();
      } else {
        setTopError(msg);
      }
    }
  };

  const onReject = async (notes: string) => {
    setTopError(null);
    try {
      await rejectMut.mutateAsync({ requisitionId: id, payload: { notes } });
      setRejectOpen(false);
      router.back();
    } catch (e: unknown) {
      setRejectOpen(false);
      setTopError(e instanceof Error ? e.message : "Reject failed");
    }
  };

  const BackButton = (
    <Pressable
      onPress={() => router.back()}
      style={{
        paddingHorizontal: 12,
        paddingVertical: 9,
        borderRadius: 8,
        backgroundColor: "#f1f5f9",
      }}
    >
      <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>
        Back
      </Text>
    </Pressable>
  );

  if (q.isPending) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
        <Stack.Screen options={{ headerShown: false }} />
        <LoadingView variant="list" />
      </View>
    );
  }

  if (q.isError || !q.data) {
    return (
      <View style={{ flex: 1, backgroundColor: "#f8fafc", padding: 16 }}>
        <Stack.Screen options={{ headerShown: false }} />
        <ErrorBanner
          message={q.error instanceof Error ? q.error.message : "Failed to load review"}
          onRetry={() => q.refetch()}
        />
      </View>
    );
  }

  const r = q.data;
  const rateSnippet = r.exchangeRate != null ? ` · Rate ${formatMoney(r.exchangeRate)}` : "";
  const headerLabel = `${r.currencyCode}${rateSnippet}`;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader
        label={headerLabel}
        title={r.refNo}
        right={BackButton}
      />

      <KeyboardAvoidingView
        behavior={Platform.OS === "ios" ? "padding" : "height"}
        style={{ flex: 1 }}
      >
        <ScrollView
          contentContainerStyle={{ padding: 16, paddingBottom: 180 + insets.bottom }}
          showsVerticalScrollIndicator={false}
          keyboardShouldPersistTaps="handled"
        >
          {/* Customer name card */}
          <View
            style={{
              backgroundColor: "#ffffff",
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 14,
              padding: 14,
              marginBottom: 12,
            }}
          >
            {(() => {
              const name = stripTags(r.customerName);
              return (
                <Text
                  style={{
                    fontSize: 18,
                    fontWeight: "700",
                    color: name ? "#0f172a" : "#94a3b8",
                    fontStyle: name ? "normal" : "italic",
                  }}
                >
                  {name || "— No name"}
                </Text>
              );
            })()}
            <Text style={{ fontSize: 13, color: "#94a3b8", marginTop: 2 }}>Customer</Text>
            {historyCount > 0 ? (
              <Pressable
                onPress={() => setHistoryOpen(true)}
                style={{
                  alignSelf: "flex-start",
                  paddingHorizontal: 10,
                  paddingVertical: 6,
                  borderRadius: 999,
                  backgroundColor: "#fef3c7",
                  marginTop: 8,
                }}
              >
                <Text style={{ color: "#92400e", fontSize: 12, fontWeight: "600" }}>
                  Customer changed ({historyCount})
                </Text>
              </Pressable>
            ) : null}
          </View>

          {/* Readiness warning */}
          {!r.readyForReview ? (
            <View
              style={{
                backgroundColor: "#fef3c7",
                borderWidth: 1,
                borderColor: "#fde68a",
                borderRadius: 14,
                padding: 14,
                marginBottom: 12,
              }}
            >
              <Text style={{ fontSize: 14, color: "#92400e" }}>
                Costing still in progress for one or more items. Approval is disabled.
              </Text>
            </View>
          ) : null}

          {/* Top error */}
          {topError ? (
            <ErrorBanner message={topError} onRetry={() => setTopError(null)} />
          ) : null}

          {/* Items heading */}
          <Text
            style={{
              fontSize: 16,
              fontWeight: "700",
              color: "#0f172a",
              marginTop: 20,
              marginBottom: 10,
            }}
          >
            Items ({r.items.length})
          </Text>

          {r.items.map((it) => (
            <ApprovalItemRow
              key={it.requisitionItemId}
              item={it}
              price={prices[it.requisitionItemId] ?? 0}
              onPriceChange={(p) =>
                setPrices((prev) => ({ ...prev, [it.requisitionItemId]: p }))
              }
              error={itemErrors[it.requisitionItemId]}
              onViewBom={() =>
                setBomSheetItem({
                  reqItemId: it.requisitionItemId,
                  desc: stripTags(it.itemDescription),
                })
              }
            />
          ))}
        </ScrollView>

        {/* Sticky bottom bar */}
        <View
          style={{
            position: "absolute",
            bottom: 0,
            left: 0,
            right: 0,
            backgroundColor: "#ffffff",
            borderTopWidth: 1,
            borderTopColor: "#e2e8f0",
            paddingHorizontal: 16,
            paddingTop: 16,
            paddingBottom: Math.max(insets.bottom, 16) + 8,
          }}
        >
          <View
            style={{
              flexDirection: "row",
              justifyContent: "space-between",
              alignItems: "center",
              marginBottom: 12,
            }}
          >
            <Text style={{ fontSize: 15, color: "#64748b" }}>Total revenue</Text>
            <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a" }}>
              {formatMoney(grandTotal)}
            </Text>
          </View>
          <View style={{ flexDirection: "row", gap: 10 }}>
            <View style={{ flex: 1 }}>
              <Button
                title="Reject"
                variant="danger"
                onPress={() => setRejectOpen(true)}
                disabled={approveMut.isPending || rejectMut.isPending}
              />
            </View>
            <View style={{ flex: 1 }}>
              <Button
                title="Approve"
                variant="primary"
                onPress={() => setApproveOpen(true)}
                disabled={!r.readyForReview || approveMut.isPending || rejectMut.isPending}
              />
            </View>
          </View>
        </View>
      </KeyboardAvoidingView>

      <ConfirmDialog
        visible={approveOpen}
        title="Approve and send quotation?"
        message="The quotation PDF will be emailed to the customer."
        confirmLabel="Approve"
        loading={approveMut.isPending}
        onCancel={() => setApproveOpen(false)}
        onConfirm={onApprove}
      />

      <RejectReasonPrompt
        visible={rejectOpen}
        loading={rejectMut.isPending}
        onCancel={() => setRejectOpen(false)}
        onConfirm={onReject}
      />

      {bomSheetItem ? (
        <BomDetailSheet
          visible
          onClose={() => setBomSheetItem(null)}
          requisitionId={id}
          requisitionItemId={bomSheetItem.reqItemId}
          itemDescription={bomSheetItem.desc}
        />
      ) : null}

      <CustomerChangeHistorySheet
        requisitionId={id}
        open={historyOpen}
        onClose={() => setHistoryOpen(false)}
      />
    </View>
  );
}
