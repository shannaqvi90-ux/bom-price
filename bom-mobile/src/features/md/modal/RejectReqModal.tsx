import { useState } from "react";
import { Modal, Pressable, Text, TextInput, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import * as Haptics from "expo-haptics";
import { useRejectRequisition } from "@/features/md/api/approvals";
import { Button } from "@/components/Button";
import { ErrorBanner } from "@/components/ErrorBanner";

interface Props {
  requisitionId: number;
  refNo: string;
  open: boolean;
  onClose: () => void;
}

export function RejectReqModal({ requisitionId, refNo, open, onClose }: Props) {
  const insets = useSafeAreaInsets();
  const reject = useRejectRequisition(requisitionId);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const handleReject = async () => {
    if (reason.trim().length < 5) {
      setError("Reason must be at least 5 characters.");
      return;
    }
    setError(null);
    try {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
      await reject.mutateAsync({ reason: reason.trim() });
      setReason("");
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Reject failed");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    }
  };

  return (
    <Modal visible={open} animationType="slide" transparent onRequestClose={onClose}>
      <Pressable
        onPress={onClose}
        style={{ flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" }}
      >
        <Pressable
          onPress={() => {}}
          style={{
            backgroundColor: "white",
            borderTopLeftRadius: 18,
            borderTopRightRadius: 18,
            padding: 20,
            paddingBottom: Math.max(insets.bottom, 16) + 12,
          }}
        >
          <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a" }}>
            Reject {refNo}
          </Text>
          <Text style={{ fontSize: 13, color: "#94a3b8", marginTop: 4 }}>
            Provide a reason. The req moves to Rejected and the SP is notified.
          </Text>

          <Text style={{ marginTop: 16, fontSize: 12, fontWeight: "600", color: "#64748b", letterSpacing: 0.5 }}>
            REASON
          </Text>
          <TextInput
            value={reason}
            onChangeText={setReason}
            placeholder="e.g. Material cost looks wrong, please verify…"
            placeholderTextColor="#94a3b8"
            multiline
            style={{
              borderWidth: 1,
              borderColor: "#cbd5e1",
              borderRadius: 10,
              paddingHorizontal: 12,
              paddingVertical: 10,
              fontSize: 14,
              minHeight: 80,
              textAlignVertical: "top",
              marginTop: 6,
              color: "#0f172a",
            }}
          />

          {error ? (
            <View style={{ marginTop: 12 }}>
              <ErrorBanner message={error} />
            </View>
          ) : null}

          <View style={{ flexDirection: "row", gap: 10, marginTop: 20 }}>
            <View style={{ flex: 1 }}>
              <Button title="Cancel" variant="secondary" onPress={onClose} />
            </View>
            <View style={{ flex: 1 }}>
              <Button title="Reject" variant="danger" onPress={handleReject} loading={reject.isPending} />
            </View>
          </View>
        </Pressable>
      </Pressable>
    </Modal>
  );
}
