import { useState } from "react";
import { Alert, ScrollView, Text, TextInput, View } from "react-native";
import type { V3Requisition } from "@/types/v3";
import { useMdPricingState } from "../state/useMdPricingState";
import { useSetMargin } from "../api/approvals";
import { FgPricingCard } from "./FgPricingCard";
import { RejectReqModal } from "../modal/RejectReqModal";
import { Button } from "@/components/Button";

interface Props {
  req: V3Requisition;
}

export function ActiveMdPricingView({ req }: Props) {
  const state = useMdPricingState(req);
  const setMargin = useSetMargin(req.id);
  const [rejectOpen, setRejectOpen] = useState(false);

  const handleApprove = async () => {
    if (!state.isValid) return;
    try {
      await setMargin.mutateAsync({
        items: req.finishedGoods.map((fg) => ({
          requisitionItemId: fg.id,
          marginPerKg: parseFloat(state.margins[fg.id] ?? "0"),
        })),
        notes: state.notes.trim() || undefined,
      });
    } catch (e) {
      Alert.alert("Error", e instanceof Error ? e.message : "Set margin failed");
    }
  };

  return (
    <View style={{ flex: 1 }}>
      <ScrollView contentContainerStyle={{ paddingTop: 8, paddingBottom: 16 }}>
        <View style={{ paddingHorizontal: 12, marginBottom: 12 }}>
          <Text style={{ fontSize: 13, color: "#64748b", fontWeight: "600", letterSpacing: 0.5 }}>
            CUSTOMER
          </Text>
          <Text style={{ fontSize: 16, fontWeight: "700", color: "#0f172a", marginTop: 4 }}>
            {req.customer.name}
          </Text>
          <Text style={{ fontSize: 13, color: "#94a3b8" }}>Currency: {req.currencyCode}</Text>
        </View>

        {req.finishedGoods.map((fg, idx) => (
          <FgPricingCard
            key={fg.id}
            fg={fg}
            index={idx}
            marginInput={state.margins[fg.id] ?? ""}
            onMarginChange={(v) => state.setMargin(fg.id, v)}
            livePerFg={
              state.livePreview?.perFg.find((p) => p.requisitionItemId === fg.id) ?? null
            }
            currencyCode={req.currencyCode}
          />
        ))}

        {state.livePreview ? (
          <View
            style={{
              marginHorizontal: 12,
              marginVertical: 12,
              padding: 14,
              backgroundColor: "#1e40af",
              borderRadius: 12,
            }}
          >
            <Text style={{ color: "white", fontSize: 13, opacity: 0.85 }}>GRAND TOTAL</Text>
            <Text style={{ color: "white", fontSize: 24, fontWeight: "700", marginTop: 4 }}>
              {req.currencyCode}{" "}
              {state.livePreview.totalAed.toLocaleString(undefined, { maximumFractionDigits: 2 })}
            </Text>
            {req.currencyCode !== "AED" ? (
              <Text style={{ color: "white", fontSize: 11, opacity: 0.75, marginTop: 4 }}>
                Final AED total computed at backend on save (rate re-snap).
              </Text>
            ) : null}
          </View>
        ) : null}

        <View style={{ paddingHorizontal: 12, marginTop: 8 }}>
          <Text
            style={{
              fontSize: 12,
              color: "#64748b",
              fontWeight: "600",
              letterSpacing: 0.5,
              marginBottom: 6,
            }}
          >
            NOTES (optional)
          </Text>
          <TextInput
            value={state.notes}
            onChangeText={state.setNotes}
            placeholder="Optional notes for SP"
            placeholderTextColor="#94a3b8"
            multiline
            style={{
              borderWidth: 1,
              borderColor: "#cbd5e1",
              borderRadius: 10,
              padding: 10,
              fontSize: 14,
              minHeight: 60,
              textAlignVertical: "top",
              color: "#0f172a",
              backgroundColor: "white",
            }}
          />
        </View>
      </ScrollView>

      <View
        style={{
          flexDirection: "row",
          gap: 10,
          padding: 12,
          borderTopWidth: 1,
          borderTopColor: "#e2e8f0",
          backgroundColor: "white",
        }}
      >
        <View style={{ flex: 1 }}>
          <Button title="Reject" variant="danger" onPress={() => setRejectOpen(true)} />
        </View>
        <View style={{ flex: 2 }}>
          <Button
            title="Approve & send"
            onPress={handleApprove}
            loading={setMargin.isPending}
            disabled={!state.isValid}
          />
        </View>
      </View>

      <RejectReqModal
        requisitionId={req.id}
        refNo={req.refNo}
        open={rejectOpen}
        onClose={() => setRejectOpen(false)}
      />
    </View>
  );
}
