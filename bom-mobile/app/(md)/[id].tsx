import { useEffect, useMemo, useState } from "react";
import { KeyboardAvoidingView, Platform, ScrollView, Text, View } from "react-native";
import { useLocalSearchParams, useRouter } from "expo-router";
import { useMdReview, useApproveRequisition, useRejectRequisition } from "@/api/approvals";
import { ApprovalItemRow } from "@/components/ApprovalItemRow";
import { Button } from "@/components/Button";
import { ConfirmDialog } from "@/components/ConfirmDialog";
import { RejectReasonPrompt } from "@/components/RejectReasonPrompt";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { formatMoney } from "@/utils/numbers";
import { approveSchema } from "@/utils/validation";

export default function MdApprovalDetail() {
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);
  const router = useRouter();
  const q = useMdReview(id);
  const approveMut = useApproveRequisition();
  const rejectMut = useRejectRequisition();

  const [prices, setPrices] = useState<Record<number, number>>({});
  const [itemErrors, setItemErrors] = useState<Record<number, string>>({});
  const [topError, setTopError] = useState<string | null>(null);
  const [approveOpen, setApproveOpen] = useState(false);
  const [rejectOpen, setRejectOpen] = useState(false);

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

  if (q.isPending) return <LoadingView />;
  if (q.isError || !q.data) {
    return (
      <View className="flex-1 p-4 bg-slate-50">
        <ErrorBanner
          message={q.error instanceof Error ? q.error.message : "Failed to load review"}
          onRetry={() => q.refetch()}
        />
      </View>
    );
  }

  const r = q.data;

  const onApprove = async () => {
    setTopError(null);
    setItemErrors({});

    const payload = {
      items: r.items.map((it) => ({
        requisitionItemId: it.requisitionItemId,
        salesPricePerKgAed: prices[it.requisitionItemId] ?? 0,
      })),
    };

    const parsed = approveSchema.safeParse(payload);
    if (!parsed.success) {
      const errMap: Record<number, string> = {};
      for (const issue of parsed.error.issues) {
        if (issue.path[0] === "items" && typeof issue.path[1] === "number") {
          const item = r.items[issue.path[1] as number];
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
      setTopError(
        e instanceof Error ? e.message : "Reject failed"
      );
    }
  };

  return (
    <KeyboardAvoidingView
      behavior={Platform.OS === "ios" ? "padding" : undefined}
      className="flex-1 bg-slate-50"
    >
      <ScrollView contentContainerClassName="p-4 pb-32">
        <Text className="text-2xl font-bold text-slate-900">{r.refNo}</Text>
        <Text className="text-base text-slate-700">{r.customerName}</Text>
        <Text className="text-xs text-slate-500 mb-4">
          {r.currencyCode}
          {r.exchangeRate != null ? ` · Rate ${formatMoney(r.exchangeRate)}` : ""}
        </Text>

        {topError ? (
          <ErrorBanner message={topError} onRetry={() => setTopError(null)} />
        ) : null}

        {!r.readyForReview ? (
          <View className="bg-amber-50 border border-amber-200 rounded-md p-3 mb-4">
            <Text className="text-sm text-amber-800">
              Costing is still in progress for one or more items. Approval is disabled.
            </Text>
          </View>
        ) : null}

        <Text className="text-base font-semibold text-slate-900 mb-2">Items</Text>
        {r.items.map((it) => (
          <ApprovalItemRow
            key={it.requisitionItemId}
            item={it}
            price={prices[it.requisitionItemId] ?? 0}
            onPriceChange={(p) =>
              setPrices((prev) => ({ ...prev, [it.requisitionItemId]: p }))
            }
            error={itemErrors[it.requisitionItemId]}
          />
        ))}
      </ScrollView>

      <View className="border-t border-slate-200 bg-white p-3">
        <View className="flex-row justify-between mb-3">
          <Text className="text-sm text-slate-600">Total revenue</Text>
          <Text className="text-base font-bold text-slate-900">{formatMoney(grandTotal)}</Text>
        </View>
        <View className="flex-row">
          <View className="flex-1 mr-2">
            <Button
              title="Reject"
              variant="danger"
              onPress={() => setRejectOpen(true)}
              disabled={approveMut.isPending || rejectMut.isPending}
            />
          </View>
          <View className="flex-1">
            <Button
              title="Approve"
              onPress={() => setApproveOpen(true)}
              disabled={!r.readyForReview || approveMut.isPending || rejectMut.isPending}
            />
          </View>
        </View>
      </View>

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
    </KeyboardAvoidingView>
  );
}
