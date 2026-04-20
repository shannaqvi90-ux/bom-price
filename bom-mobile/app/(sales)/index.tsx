import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useRequisitionsList } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";

export default function SalesRequisitionsList() {
  const router = useRouter();
  const q = useRequisitionsList();

  if (q.isPending) return <LoadingView />;

  return (
    <View className="flex-1 bg-slate-50">
      {q.isError ? (
        <View className="p-4">
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load requisitions"}
            onRetry={() => q.refetch()}
          />
        </View>
      ) : null}

      <FlatList
        data={q.data ?? []}
        keyExtractor={(r) => String(r.id)}
        contentContainerClassName="p-3"
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
        onPress={() => router.push("/(sales)/new")}
        className="absolute bottom-6 right-6 bg-brand-600 active:bg-brand-700 rounded-full w-14 h-14 items-center justify-center shadow-lg"
      >
        <Text className="text-white text-3xl leading-none">+</Text>
      </Pressable>
    </View>
  );
}
