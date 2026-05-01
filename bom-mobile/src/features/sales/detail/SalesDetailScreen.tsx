import { useState } from "react";
import { ScrollView, View, Text, ActivityIndicator } from "react-native";
import { useLocalSearchParams } from "expo-router";
import { useRequisition } from "../../../api/requisitions";
import { DetailHeader } from "./DetailHeader";
import { FgReadCard } from "./FgReadCard";
import { FinalPriceCard } from "./FinalPriceCard";
import { StatusFooterCta } from "./StatusFooterCta";

export function SalesDetailScreen() {
  const { id } = useLocalSearchParams<{ id: string }>();
  const reqId = Number(id);
  const { data: req, isLoading } = useRequisition(reqId);
  const [confirmModalOpen, setConfirmModalOpen] = useState(false);

  if (isLoading) return <ActivityIndicator style={{ marginTop: 40 }} />;
  if (!req) return <Text style={{ padding: 24 }}>Requisition not found.</Text>;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <ScrollView contentContainerStyle={{ paddingBottom: 16 }}>
        <DetailHeader req={req} />
        {req.status === "Signed" && <FinalPriceCard req={req} />}
        {req.status === "Cancelled" && req.cancelReason && (
          <View style={{ margin: 12, padding: 12, backgroundColor: "#fef2f2", borderRadius: 10 }}>
            <Text style={{ fontWeight: "600", color: "#991b1b" }}>Cancelled</Text>
            <Text style={{ color: "#7f1d1d", marginTop: 4 }}>{req.cancelReason}</Text>
            {req.cancelledAt && <Text style={{ color: "#9a3412", fontSize: 11, marginTop: 4 }}>
              {new Date(req.cancelledAt).toLocaleString()}
            </Text>}
          </View>
        )}
        <View style={{ marginTop: 8 }}>
          {req.finishedGoods.map((fg, i) => <FgReadCard key={fg.id ?? i} fg={fg} index={i} />)}
        </View>
        {req.notes && (
          <View style={{ margin: 12, padding: 12, backgroundColor: "white", borderRadius: 10, borderWidth: 1, borderColor: "#e5e7eb" }}>
            <Text style={{ fontSize: 12, color: "#64748b", textTransform: "uppercase", fontWeight: "600", letterSpacing: 0.5 }}>Notes</Text>
            <Text style={{ marginTop: 4, color: "#0f172a" }}>{req.notes}</Text>
          </View>
        )}
      </ScrollView>
      <StatusFooterCta
        req={req}
        onCustomerConfirm={() => setConfirmModalOpen(true)}
        onDownloadPdf={() => { /* wire in Task 26 */ }}
      />
      {/* CustomerConfirmModal will be wired in Task 25; for now confirmModalOpen state is set but unread */}
      {confirmModalOpen ? null : null}
    </View>
  );
}
