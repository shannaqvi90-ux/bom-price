// bom-mobile/src/features/sales/detail/CustomerConfirmModal.tsx
import { useState } from "react";
import { Modal, View, Text, TextInput, Pressable, ActivityIndicator } from "react-native";
import * as Haptics from "expo-haptics";
import { useAcceptCustomer, useRejectCustomer } from "../../../api/approvals";
import type { V3Requisition } from "../../../types/v3";

interface Props {
  visible: boolean;
  req: V3Requisition;
  onClose: () => void;
}

export function CustomerConfirmModal({ visible, req, onClose }: Props) {
  const [view, setView] = useState<"choose" | "reject">("choose");
  const [reason, setReason] = useState("");
  const accept = useAcceptCustomer();
  const reject = useRejectCustomer();

  const onAccept = async () => {
    Haptics.notificationAsync(Haptics.NotificationFeedbackType.Success);
    await accept.mutateAsync(req.id);
    onClose();
  };

  const onSubmitReject = async () => {
    if (reason.trim().length < 5) return;
    await reject.mutateAsync({ requisitionId: req.id, reason: reason.trim() });
    onClose();
  };

  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose}>
      <Pressable onPress={onClose} style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }} />
      <View style={{
        position: "absolute", left: 16, right: 16, top: "20%",
        backgroundColor: "white", borderRadius: 16, padding: 18,
      }}>
        <Text style={{ fontWeight: "700", fontSize: 16, color: "#0f172a" }}>Customer response on {req.refNo}</Text>
        <Text style={{ marginTop: 6, color: "#64748b", fontSize: 13 }}>
          {req.customer.name} · {req.currencyCode}
        </Text>

        {view === "choose" ? (
          <View style={{ marginTop: 16, gap: 10 }}>
            <Pressable onPress={onAccept} disabled={accept.isPending}
              style={{ backgroundColor: "#10b981", padding: 14, borderRadius: 10, alignItems: "center" }}>
              {accept.isPending ? <ActivityIndicator color="white" /> :
                <Text style={{ color: "white", fontWeight: "600" }}>Customer accepted</Text>}
            </Pressable>
            <Pressable onPress={() => setView("reject")}
              style={{ backgroundColor: "#fef3c7", padding: 14, borderRadius: 10, alignItems: "center" }}>
              <Text style={{ color: "#92400e", fontWeight: "600" }}>Customer rejected</Text>
            </Pressable>
          </View>
        ) : (
          <View style={{ marginTop: 16 }}>
            <Text style={{ fontSize: 11, color: "#64748b", fontWeight: "600", textTransform: "uppercase" }}>Reason (≥ 5 chars)</Text>
            <TextInput
              value={reason} onChangeText={setReason} multiline
              style={{ marginTop: 4, padding: 10, backgroundColor: "#f1f5f9", borderRadius: 8, minHeight: 80, textAlignVertical: "top" }}
            />
            <View style={{ flexDirection: "row", gap: 10, marginTop: 12 }}>
              <Pressable onPress={() => setView("choose")} style={{ flex: 1, padding: 12, alignItems: "center" }}>
                <Text style={{ color: "#64748b" }}>Back</Text>
              </Pressable>
              <Pressable
                onPress={onSubmitReject}
                disabled={reason.trim().length < 5 || reject.isPending}
                style={{
                  flex: 2, padding: 12, borderRadius: 10, alignItems: "center",
                  backgroundColor: "#dc2626", opacity: reason.trim().length < 5 ? 0.5 : 1,
                }}>
                {reject.isPending ? <ActivityIndicator color="white" /> :
                  <Text style={{ color: "white", fontWeight: "600" }}>Confirm rejection</Text>}
              </Pressable>
            </View>
          </View>
        )}
      </View>
    </Modal>
  );
}
