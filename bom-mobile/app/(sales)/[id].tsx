import { useState } from "react";
import { ScrollView, Text, View } from "react-native";
import { useLocalSearchParams } from "expo-router";
import { useRequisitionDetail } from "@/api/requisitions";
import { downloadRequisitionPdf } from "@/api/pdf";
import { Button } from "@/components/Button";
import { StatusPill } from "@/components/StatusPill";
import { ItemStageBadge } from "@/components/ItemStageBadge";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { formatShortDate } from "@/utils/dates";

export default function RequisitionDetail() {
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);
  const q = useRequisitionDetail(id);
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [pdfLoading, setPdfLoading] = useState(false);

  if (q.isPending) return <LoadingView />;
  if (q.isError || !q.data) {
    return (
      <View className="flex-1 p-4 bg-slate-50">
        <ErrorBanner
          message={
            q.error instanceof Error ? q.error.message : "Failed to load requisition"
          }
          onRetry={() => q.refetch()}
        />
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
    <ScrollView className="flex-1 bg-slate-50" contentContainerClassName="p-4">
      <View className="flex-row items-center justify-between mb-2">
        <Text className="text-2xl font-bold text-slate-900">{r.refNo}</Text>
        <StatusPill status={r.status as Parameters<typeof StatusPill>[0]["status"]} />
      </View>
      <Text className="text-base text-slate-700">{r.customerName}</Text>
      <Text className="text-xs text-slate-500 mb-4">
        {r.branchName} · Created {formatShortDate(r.createdAt)} · {r.currencyCode}
      </Text>

      {isRejected && r.approval?.notes ? (
        <View className="bg-rose-50 border border-rose-200 rounded-md p-3 mb-4">
          <Text className="text-sm font-semibold text-rose-800 mb-1">
            Rejection reason
          </Text>
          <Text className="text-sm text-rose-900">{r.approval.notes}</Text>
        </View>
      ) : null}

      <Text className="text-base font-semibold text-slate-900 mb-2">Items</Text>
      {r.items.map((it) => (
        <View
          key={it.id}
          className="bg-white border border-slate-200 rounded-md p-3 mb-2"
        >
          <View className="flex-row justify-between">
            <Text className="text-sm font-medium text-slate-900 flex-1 pr-2" numberOfLines={2}>
              {it.itemDescription}
            </Text>
            <Text className="text-sm text-slate-700">{it.expectedQty}</Text>
          </View>
          <ItemStageBadge status={r.status} />
        </View>
      ))}

      {isApproved ? (
        <View className="mt-6">
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
        <Text className="text-xs text-slate-500 text-center mt-4">
          Approved on {formatShortDate(r.approval.approvedAt)}
        </Text>
      ) : null}
    </ScrollView>
  );
}
