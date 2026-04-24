import { FlatList, Pressable, RefreshControl, View } from "react-native";
import { useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useRequisitionsList } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { ScreenHeader } from "@/components/ScreenHeader";
import { SalesHeaderRight } from "@/components/SalesHeaderRight";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";

export default function SalesRequisitionsList() {
  const router = useRouter();
  const insets = useSafeAreaInsets();
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
        style={{
          position: "absolute",
          bottom: Math.max(16 + insets.bottom, 60),
          right: 24,
          width: 60,
          height: 60,
          zIndex: 100,
        }}
      >
        {({ pressed }) => (
          <View
            style={{
              flex: 1,
              borderRadius: 30,
              backgroundColor: "#1e40af",
              borderWidth: 2,
              borderColor: "#1e3a8a",
              alignItems: "center",
              justifyContent: "center",
              elevation: 8,
              shadowColor: "#000",
              shadowOffset: { width: 0, height: 4 },
              shadowOpacity: 0.3,
              shadowRadius: 8,
              opacity: pressed ? 0.85 : 1,
            }}
          >
            <View style={{ width: 24, height: 3, backgroundColor: "white", borderRadius: 2, position: "absolute" }} />
            <View style={{ width: 3, height: 24, backgroundColor: "white", borderRadius: 2, position: "absolute" }} />
          </View>
        )}
      </Pressable>
    </View>
  );
}
