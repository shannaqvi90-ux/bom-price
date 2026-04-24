import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitionsList } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { ScreenHeader } from "@/components/ScreenHeader";
import { SalesHeaderRight } from "@/components/SalesHeaderRight";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";

export default function SalesRequisitionsList() {
  const router = useRouter();
  const q = useRequisitionsList();

  if (q.isPending) return <LoadingView />;

  const count = q.data?.length ?? 0;

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <ScreenHeader
        label="SALES"
        title="Requisitions"
        count={count}
        right={<SalesHeaderRight />}
      />

      {q.isError ? (
        <View style={{ paddingHorizontal: 16, paddingBottom: 8 }}>
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load requisitions"}
            onRetry={() => q.refetch()}
          />
        </View>
      ) : null}

      <FlatList
        data={q.data ?? []}
        keyExtractor={(r) => String(r.id)}
        contentContainerStyle={{ padding: 12, paddingBottom: 96 }}
        refreshControl={
          <RefreshControl refreshing={q.isRefetching} onRefresh={() => q.refetch()} />
        }
        renderItem={({ item }) => (
          <RequisitionCard item={item} onPress={(id) => router.push(`/(sales)/${id}`)} />
        )}
        ListEmptyComponent={
          !q.isError ? (
            <EmptyState
              title="No requisitions yet"
              hint="Tap + to create your first requisition."
            />
          ) : null
        }
      />

      <Pressable
        onPress={async () => {
          await Haptics.selectionAsync();
          router.push("/(sales)/new");
        }}
        style={({ pressed }) => ({
          position: "absolute",
          bottom: 24,
          right: 24,
          width: 56,
          height: 56,
          borderRadius: 28,
          backgroundColor: "#1e40af",
          alignItems: "center",
          justifyContent: "center",
          shadowColor: "#1e40af",
          shadowOffset: { width: 0, height: 4 },
          shadowOpacity: pressed ? 0.25 : 0.35,
          shadowRadius: 10,
          elevation: 5,
          opacity: pressed ? 0.9 : 1,
        })}
      >
        <Text style={{ color: "white", fontSize: 30, fontWeight: "700", lineHeight: 32 }}>+</Text>
      </Pressable>
    </View>
  );
}
