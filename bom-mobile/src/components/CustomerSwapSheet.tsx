import { useState } from "react";
import { Modal, Pressable, Text, TextInput, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import * as Haptics from "expo-haptics";
import { useChangeCustomer } from "@/api/requisitions";
import { SearchablePicker } from "@/components/SearchablePicker";
import { Button } from "@/components/Button";
import { ErrorBanner } from "@/components/ErrorBanner";
import { useCustomers } from "@/api/lookups";

interface Props {
  requisitionId: number;
  currentCustomerId: number;
  currentCustomerName: string;
  open: boolean;
  onClose: () => void;
}

export function CustomerSwapSheet({
  requisitionId,
  currentCustomerId,
  currentCustomerName,
  open,
  onClose,
}: Props) {
  const insets = useSafeAreaInsets();
  const customersQ = useCustomers();
  const change = useChangeCustomer(requisitionId);

  const [newCustomerId, setNewCustomerId] = useState<number | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  // Exclude the current customer from the picker list
  const customers = (customersQ.data ?? []).filter((c) => c.id !== currentCustomerId);

  const handleSave = async () => {
    if (!newCustomerId) return;
    setError(null);
    try {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
      await change.mutateAsync({
        customerId: newCustomerId,
        reason: reason.trim() ? reason.trim() : undefined,
      });
      setNewCustomerId(null);
      setReason("");
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to change customer");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    }
  };

  return (
    <Modal visible={open} animationType="slide" transparent onRequestClose={onClose}>
      <View style={{ flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" }}>
        <View
          style={{
            backgroundColor: "#ffffff",
            borderTopLeftRadius: 18,
            borderTopRightRadius: 18,
            padding: 20,
            paddingBottom: Math.max(insets.bottom, 16) + 12,
            maxHeight: "90%",
          }}
        >
          {/* Header */}
          <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
            <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a" }}>Change customer</Text>
            <Pressable onPress={onClose} hitSlop={12}>
              <Text style={{ fontSize: 22, color: "#64748b" }}>×</Text>
            </Pressable>
          </View>

          {/* Current customer */}
          <View style={{ marginTop: 16 }}>
            <Text style={{ fontSize: 12, fontWeight: "600", color: "#64748b", letterSpacing: 0.5 }}>
              CURRENT
            </Text>
            <Text style={{ fontSize: 16, fontWeight: "600", color: "#0f172a", marginTop: 2 }}>
              {currentCustomerName}
            </Text>
          </View>

          {/* New customer picker */}
          <View style={{ marginTop: 16 }}>
            <Text style={{ fontSize: 12, fontWeight: "600", color: "#64748b", letterSpacing: 0.5, marginBottom: 6 }}>
              NEW CUSTOMER
            </Text>
            <SearchablePicker
              options={customers.map((c) => ({ id: c.id, label: c.name, sublabel: c.code }))}
              value={newCustomerId}
              onChange={setNewCustomerId}
              placeholder="Search customers..."
              loading={customersQ.isPending}
            />
          </View>

          {/* Reason input */}
          <View style={{ marginTop: 16 }}>
            <Text style={{ fontSize: 12, fontWeight: "600", color: "#64748b", letterSpacing: 0.5, marginBottom: 6 }}>
              REASON (optional)
            </Text>
            <TextInput
              value={reason}
              onChangeText={setReason}
              placeholder="Reason for change (optional)"
              placeholderTextColor="#94a3b8"
              style={{
                borderWidth: 1,
                borderColor: "#cbd5e1",
                borderRadius: 10,
                paddingHorizontal: 12,
                paddingVertical: 10,
                fontSize: 14,
                color: "#0f172a",
              }}
              multiline
              numberOfLines={2}
            />
          </View>

          {/* Error banner */}
          {error ? (
            <View style={{ marginTop: 12 }}>
              <ErrorBanner message={error} onRetry={() => setError(null)} />
            </View>
          ) : null}

          {/* Action row */}
          <View style={{ flexDirection: "row", gap: 10, marginTop: 20 }}>
            <View style={{ flex: 1 }}>
              <Button title="Cancel" variant="secondary" onPress={onClose} />
            </View>
            <View style={{ flex: 1 }}>
              <Button
                title="Save"
                variant="primary"
                onPress={handleSave}
                loading={change.isPending}
                disabled={!newCustomerId}
              />
            </View>
          </View>
        </View>
      </View>
    </Modal>
  );
}
