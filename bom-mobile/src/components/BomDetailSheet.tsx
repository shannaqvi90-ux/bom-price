import { Modal, Pressable, ScrollView, Text, useWindowDimensions, View } from "react-native";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import { Skeleton } from "./Skeleton";
import { ErrorBanner } from "./ErrorBanner";
import type { BomReviewResponse } from "@/types/api";

interface Props {
  visible: boolean;
  onClose: () => void;
  requisitionId: number;
  requisitionItemId: number;
  itemDescription: string;
}

function useBomReview(requisitionId: number, enabled: boolean) {
  return useQuery({
    queryKey: ["bom", "review", requisitionId],
    queryFn: async () => {
      const res = await api.get<BomReviewResponse>(`/api/bom/${requisitionId}`);
      return res.data;
    },
    enabled: enabled && Number.isFinite(requisitionId) && requisitionId > 0,
    staleTime: 30_000,
  });
}

export function BomDetailSheet({
  visible,
  onClose,
  requisitionId,
  requisitionItemId,
  itemDescription,
}: Props) {
  const { height } = useWindowDimensions();
  const q = useBomReview(requisitionId, visible);

  const bomItem = q.data?.items.find((it) => it.requisitionItemId === requisitionItemId);
  const lines = bomItem?.lines ?? [];

  return (
    <Modal
      visible={visible}
      animationType="slide"
      transparent
      onRequestClose={onClose}
    >
      {/* Backdrop */}
      <Pressable
        style={{ flex: 1, backgroundColor: "rgba(0,0,0,0.45)" }}
        onPress={onClose}
      />

      {/* Sheet */}
      <View
        style={{
          position: "absolute",
          bottom: 0,
          left: 0,
          right: 0,
          maxHeight: height * 0.7,
          backgroundColor: "#ffffff",
          borderTopLeftRadius: 24,
          borderTopRightRadius: 24,
        }}
      >
        {/* Sheet header */}
        <View
          style={{
            flexDirection: "row",
            alignItems: "center",
            justifyContent: "center",
            paddingHorizontal: 16,
            paddingTop: 16,
            paddingBottom: 8,
            borderBottomWidth: 1,
            borderBottomColor: "#e2e8f0",
          }}
        >
          <View style={{ flex: 1 }} />
          <View style={{ flex: 4, alignItems: "center" }}>
            <Text style={{ fontSize: 17, fontWeight: "700", color: "#0f172a" }}>
              BOM breakdown
            </Text>
            <Text
              style={{ fontSize: 14, color: "#475569", marginTop: 2 }}
              numberOfLines={1}
            >
              {itemDescription}
            </Text>
          </View>
          <View style={{ flex: 1, alignItems: "flex-end" }}>
            <Pressable
              onPress={onClose}
              style={{
                width: 32,
                height: 32,
                borderRadius: 16,
                backgroundColor: "#f1f5f9",
                alignItems: "center",
                justifyContent: "center",
              }}
            >
              <Text style={{ fontSize: 16, color: "#475569", fontWeight: "600" }}>✕</Text>
            </Pressable>
          </View>
        </View>

        {/* Body */}
        <ScrollView
          contentContainerStyle={{ padding: 16 }}
          showsVerticalScrollIndicator={false}
        >
          {q.isPending ? (
            <View>
              {[0, 1, 2, 3].map((i) => (
                <View key={i} style={{ marginBottom: 12 }}>
                  <Skeleton width="60%" height={14} style={{ marginBottom: 6 }} />
                  <Skeleton width="40%" height={12} />
                </View>
              ))}
            </View>
          ) : q.isError ? (
            <ErrorBanner
              message={
                q.error instanceof Error ? q.error.message : "Failed to load BOM"
              }
              onRetry={() => q.refetch()}
            />
          ) : lines.length === 0 ? (
            <Text style={{ fontSize: 15, color: "#94a3b8", textAlign: "center", paddingVertical: 32 }}>
              No BOM lines recorded
            </Text>
          ) : (
            lines.map((line) => (
              <View
                key={line.id}
                style={{
                  flexDirection: "row",
                  justifyContent: "space-between",
                  alignItems: "flex-start",
                  paddingVertical: 10,
                  borderBottomWidth: 1,
                  borderBottomColor: "#e2e8f0",
                }}
              >
                <View style={{ flex: 1, marginRight: 12 }}>
                  <Text style={{ fontSize: 15, fontWeight: "600", color: "#0f172a" }}>
                    {line.rawMaterialDescription}
                  </Text>
                  <Text style={{ fontSize: 13, color: "#64748b", marginTop: 2 }}>
                    Wastage {line.wastagePct}% · {line.qtyPerKg} kg/kg
                  </Text>
                </View>
                <Text style={{ fontSize: 15, fontWeight: "700", color: "#1e40af" }}>
                  {line.costPerKgInAed != null
                    ? line.costPerKgInAed.toFixed(4)
                    : line.costPerKg != null
                    ? `${line.costPerKg.toFixed(4)} ${line.currencyCode ?? ""}`
                    : "-"}
                </Text>
              </View>
            ))
          )}
        </ScrollView>
      </View>
    </Modal>
  );
}
