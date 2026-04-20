import { FlatList, RefreshControl, View } from "react-native";
import { useRouter } from "expo-router";
import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import type { RequisitionListItem } from "@/types/api";

function useMdPending() {
  return useQuery({
    queryKey: [...requisitionKeys.list(), "mdReview"],
    queryFn: async () => {
      const res = await api.get<RequisitionListItem[]>("/api/requisitions", {
        params: { status: "MdReview" },
      });
      return res.data;
    },
  });
}

export default function MdPendingApprovals() {
  const router = useRouter();
  const q = useMdPending();

  if (q.isPending) return <LoadingView />;

  return (
    <View className="flex-1 bg-slate-50">
      {q.isError ? (
        <View className="p-4">
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load pending approvals"}
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
          <RequisitionCard
            item={item}
            onPress={(id) => router.push(`/(md)/${id}`)}
          />
        )}
        ListEmptyComponent={
          !q.isError ? (
            <EmptyState
              title="Nothing pending"
              hint="You're all caught up."
            />
          ) : null
        }
      />
    </View>
  );
}
