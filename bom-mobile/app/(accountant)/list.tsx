import { useRef, useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  RefreshControl,
  Text,
  TextInput,
  View,
} from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { useInfiniteQuery, useQueryClient } from "@tanstack/react-query";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { ScreenHeader } from "@/components/ScreenHeader";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { NotificationBell } from "@/components/NotificationBell";
import { StatusChipRow, CHIP_TO_STATUSES, CHIPS, type ChipLabel } from "@/components/StatusChipRow";
import { useDebouncedValue } from "@/hooks/useDebouncedValue";
import { useAuth } from "@/auth/AuthContext";
import type { RequisitionListItem, RequisitionStatus } from "@/types/api";

const PAGE_SIZE = 20;
const STAGGER_CAP = 20;

const ALL_STATUSES: readonly RequisitionStatus[] = [
  "BomPending", "BomInProgress",
  "CostingPending", "CostingInProgress",
  "MdReview", "Approved", "Rejected",
];

function isChipLabel(value: string | undefined): value is ChipLabel {
  return !!value && (CHIPS as readonly string[]).includes(value);
}

function isStatus(value: string | undefined): value is RequisitionStatus {
  return !!value && (ALL_STATUSES as readonly string[]).includes(value);
}

function useAccountantList(
  statuses: string[],
  search: string,
  from: string | undefined,
  to: string | undefined,
) {
  return useInfiniteQuery({
    queryKey: [
      ...requisitionKeys.list(),
      "accountantList",
      { statuses: statuses.join(","), search, from, to },
    ],
    queryFn: async ({ pageParam }) => {
      const params = new URLSearchParams();
      for (const s of statuses) params.append("status", s);
      if (search) params.append("search", search);
      if (from) params.append("from", from);
      if (to) params.append("to", to);
      params.append("page", String(pageParam));
      params.append("pageSize", String(PAGE_SIZE));

      const res = await api.get<RequisitionListItem[]>(`/api/requisitions?${params.toString()}`);
      return res.data;
    },
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.length < PAGE_SIZE ? undefined : allPages.length + 1,
  });
}

export default function AccountantList() {
  const router = useRouter();
  const { logout } = useAuth();
  const qc = useQueryClient();
  const search = useLocalSearchParams<{
    chip?: string; onlyStatus?: string; from?: string; to?: string; search?: string;
  }>();

  const initialChip: ChipLabel = isChipLabel(search.chip) ? search.chip : "Costing";
  const initialOnlyStatus: RequisitionStatus | null = isStatus(search.onlyStatus) ? search.onlyStatus : null;

  const [activeChip, setActiveChip] = useState<ChipLabel>(initialChip);
  const [onlyStatus, setOnlyStatus] = useState<RequisitionStatus | null>(initialOnlyStatus);
  const [searchInput, setSearchInput] = useState(search.search ?? "");
  const debouncedSearch = useDebouncedValue(searchInput, 300);
  const listRef = useRef<FlatList<RequisitionListItem>>(null);

  // onlyStatus overrides chip's status set when present
  const statuses: string[] = onlyStatus ? [onlyStatus] : CHIP_TO_STATUSES[activeChip];

  const handleChipChange = (label: ChipLabel) => {
    // Reset cached pages so the new filter starts at page 1 (20 items),
    // not the same pageCount the previous filter had loaded — useInfiniteQuery
    // otherwise refetches all previously-loaded pages with the new key.
    qc.removeQueries({ queryKey: [...requisitionKeys.list(), "accountantList"] });
    setActiveChip(label);
    setOnlyStatus(null); // selecting a chip clears the dashboard's onlyStatus pin
    listRef.current?.scrollToOffset({ offset: 0, animated: false });
  };

  const q = useAccountantList(statuses, debouncedSearch, search.from, search.to);
  const items: RequisitionListItem[] = q.data?.pages.flat() ?? [];

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const HeaderRight = (
    <>
      <NotificationBell />
      <Pressable
        onPress={onLogout}
        style={{
          paddingHorizontal: 12,
          paddingVertical: 9,
          borderRadius: 8,
          backgroundColor: "#f1f5f9",
        }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader
        label="ACCOUNTANT"
        title="Requisitions"
        count={items.length}
        right={HeaderRight}
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

      <StatusChipRow active={activeChip} onChange={handleChipChange} />

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
          ref={listRef}
          data={items}
          keyExtractor={(r) => String(r.id)}
          contentContainerStyle={{ padding: 14, paddingTop: 6 }}
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
                onPress={(_id) => {
                  Haptics.selectionAsync();
                  router.push(`/(accountant)/${item.id}` as Parameters<typeof router.push>[0]);
                }}
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
              title={items.length === 0 && !debouncedSearch ? "No requisitions" : "No matches"}
              hint={
                debouncedSearch
                  ? `No matches for "${debouncedSearch}"`
                  : `No requisitions in this filter.`
              }
            />
          }
        />
      )}
    </View>
  );
}
