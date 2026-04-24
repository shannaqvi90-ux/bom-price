import { useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  RefreshControl,
  Text,
  TextInput,
  View,
} from "react-native";
import { useRouter } from "expo-router";
import { useInfiniteQuery } from "@tanstack/react-query";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { ScreenHeader } from "@/components/ScreenHeader";
import { SalesHeaderRight } from "@/components/SalesHeaderRight";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { StatusChipRow, CHIP_TO_STATUSES, type ChipLabel } from "@/components/StatusChipRow";
import { useDebouncedValue } from "@/hooks/useDebouncedValue";
import type { RequisitionListItem } from "@/types/api";

const PAGE_SIZE = 20;
const STAGGER_CAP = 20;

function useSalesRequisitions(statuses: string[], search: string) {
  return useInfiniteQuery({
    queryKey: [...requisitionKeys.list(), "salesList", { statuses: statuses.join(","), search }],
    queryFn: async ({ pageParam }) => {
      const params = new URLSearchParams();
      for (const s of statuses) params.append("status", s);
      if (search) params.append("search", search);
      params.append("page", String(pageParam));
      params.append("pageSize", String(PAGE_SIZE));

      const res = await api.get<RequisitionListItem[]>(
        `/api/requisitions?${params.toString()}`
      );
      return res.data;
    },
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.length < PAGE_SIZE ? undefined : allPages.length + 1,
  });
}

export default function SalesRequisitionsList() {
  const router = useRouter();
  const insets = useSafeAreaInsets();

  const [activeChip, setActiveChip] = useState<ChipLabel>("All");
  const [searchInput, setSearchInput] = useState("");
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const statuses = CHIP_TO_STATUSES[activeChip];

  const q = useSalesRequisitions(statuses, debouncedSearch);
  const items: RequisitionListItem[] = q.data?.pages.flat() ?? [];

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <ScreenHeader
        label="SALES"
        title="Requisitions"
        count={items.length}
        right={<SalesHeaderRight />}
      />

      <View style={{ paddingHorizontal: 14, paddingTop: 8, paddingBottom: 2, backgroundColor: "#f8fafc" }}>
        <TextInput
          value={searchInput}
          onChangeText={setSearchInput}
          placeholder="Search REQ-xxxx or customer..."
          placeholderTextColor="#94a3b8"
          style={{
            borderWidth: 1,
            borderColor: "#cbd5e1",
            backgroundColor: "#ffffff",
            borderRadius: 10,
            paddingHorizontal: 12,
            paddingVertical: 9,
            fontSize: 14,
            color: "#0f172a",
          }}
          autoCorrect={false}
          autoCapitalize="none"
        />
      </View>

      <StatusChipRow active={activeChip} onChange={setActiveChip} />

      {q.isPending ? (
        <LoadingView variant="list" />
      ) : q.isError ? (
        <View style={{ padding: 16 }}>
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load requisitions"}
            onRetry={() => q.refetch()}
          />
        </View>
      ) : (
        <FlatList
          data={items}
          keyExtractor={(r) => String(r.id)}
          contentContainerStyle={{ padding: 14, paddingTop: 6, paddingBottom: 96 }}
          overScrollMode="never"
          bounces={false}
          onEndReached={() => { if (q.hasNextPage && !q.isFetchingNextPage) q.fetchNextPage(); }}
          onEndReachedThreshold={0.5}
          refreshControl={
            <RefreshControl
              refreshing={q.isRefetching && !q.isFetchingNextPage}
              onRefresh={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                q.refetch();
              }}
              tintColor="#1e40af"
              colors={["#1e40af"]}
            />
          }
          renderItem={({ item, index }) => (
            <MotiView
              from={{ opacity: 0, translateY: 14 }}
              animate={{ opacity: 1, translateY: 0 }}
              transition={{
                type: "spring",
                damping: 16,
                stiffness: 140,
                delay: index < STAGGER_CAP ? 200 + index * 80 : 0,
              }}
            >
              <RequisitionCard
                item={item}
                onPress={() => router.push(`/(sales)/${item.id}`)}
              />
            </MotiView>
          )}
          ListFooterComponent={
            q.isFetchingNextPage ? (
              <View style={{ paddingVertical: 20, alignItems: "center" }}>
                <ActivityIndicator color="#1e40af" />
                <Text style={{ color: "#64748b", fontSize: 14, marginTop: 8 }}>Loading more…</Text>
              </View>
            ) : q.hasNextPage === false && items.length > PAGE_SIZE ? (
              <View style={{ paddingVertical: 20, alignItems: "center" }}>
                <Text style={{ color: "#94a3b8", fontSize: 13 }}>End of list · {items.length} total</Text>
              </View>
            ) : null
          }
          ListEmptyComponent={
            <EmptyState
              title={activeChip === "All" && !debouncedSearch ? "No requisitions yet" : "No requisitions"}
              hint={
                debouncedSearch
                  ? `No matches for "${debouncedSearch}"`
                  : activeChip === "All"
                    ? "Tap + to create your first requisition."
                    : `No requisitions in the ${activeChip} stage.`
              }
            />
          }
        />
      )}

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
