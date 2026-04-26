import { Modal, Pressable, ScrollView, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useBranchChangeHistory } from "@/api/requisitions";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { formatShortDate } from "@/utils/dates";

interface Props {
  requisitionId: number;
  open: boolean;
  onClose: () => void;
}

export function BranchChangeHistorySheet({ requisitionId, open, onClose }: Props) {
  const insets = useSafeAreaInsets();
  // Only fetch when the sheet is open
  const q = useBranchChangeHistory(requisitionId, open);

  return (
    <Modal visible={open} animationType="slide" transparent onRequestClose={onClose}>
      <Pressable
        onPress={onClose}
        style={{ flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" }}
      >
        <Pressable
          onPress={() => {}}
          style={{
            backgroundColor: "#ffffff",
            borderTopLeftRadius: 18,
            borderTopRightRadius: 18,
            padding: 20,
            paddingBottom: Math.max(insets.bottom, 16) + 12,
            maxHeight: "85%",
          }}
        >
          <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
            <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a" }}>
              Branch change history
            </Text>
            <Pressable onPress={onClose} hitSlop={12}>
              <Text style={{ fontSize: 22, color: "#64748b" }}>×</Text>
            </Pressable>
          </View>

          <ScrollView style={{ marginTop: 16 }}>
            {q.isPending ? (
              <LoadingView variant="list" />
            ) : q.isError ? (
              <ErrorBanner
                message={q.error instanceof Error ? q.error.message : "Failed to load history"}
                onRetry={() => q.refetch()}
              />
            ) : (q.data?.length ?? 0) === 0 ? (
              <Text style={{ color: "#64748b", fontSize: 14, textAlign: "center", paddingVertical: 24 }}>
                No branch changes recorded.
              </Text>
            ) : (
              q.data!.map((entry) => (
                <View
                  key={entry.id}
                  style={{
                    borderWidth: 1,
                    borderColor: "#e2e8f0",
                    borderRadius: 12,
                    padding: 12,
                    marginBottom: 10,
                    backgroundColor: "#f8fafc",
                  }}
                >
                  <Text style={{ fontSize: 14, color: "#0f172a", fontWeight: "600" }}>
                    {entry.oldBranchName} → {entry.newBranchName}
                  </Text>
                  <Text style={{ fontSize: 12, color: "#64748b", marginTop: 4 }}>
                    by {entry.changedByUserName} · {formatShortDate(entry.changedAt)}
                  </Text>
                  {entry.reason ? (
                    <Text style={{ fontSize: 13, color: "#475569", marginTop: 6, fontStyle: "italic" }}>
                      "{entry.reason}"
                    </Text>
                  ) : null}
                </View>
              ))
            )}
          </ScrollView>
        </Pressable>
      </Pressable>
    </Modal>
  );
}
