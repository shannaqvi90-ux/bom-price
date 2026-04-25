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
import { Stack, useRouter } from "expo-router";
import { useInfiniteQuery } from "@tanstack/react-query";
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
import { useDebouncedValue } from "@/hooks/useDebouncedValue";
import { useAuth } from "@/auth/AuthContext";
import type { RequisitionListItem } from "@/types/api";

const PAGE_SIZE = 20;
const STAGGER_CAP = 20;
const PENDING_STATUSES = ["CostingPending", "CostingInProgress"];

function useAccountantPending(search: string) {
  return useInfiniteQuery({
    queryKey: [...requisitionKeys.list(), "accountantPending", { search }],
    queryFn: async ({ pageParam }) => {
      const params = new URLSearchParams();
      for (const s of PENDING_STATUSES) params.append("status", s);
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

export default function AccountantPending() {
  const router = useRouter();
  const { logout } = useAuth();

  const [searchInput, setSearchInput] = useState("");
  const debouncedSearch = useDebouncedValue(searchInput, 300);

  const q = useAccountantPending(debouncedSearch);
  const items: RequisitionListItem[] = q.data?.pages.flat() ?? [];

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const handleItemPress = (item: RequisitionListItem) => {
    Haptics.selectionAsync();
    router.push(`/(accountant)/${item.id}`);
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
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>
          Log out
        </Text>
      </Pressable>
    </>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader
        label="ACCOUNTANT"
        title="Pending Costing"
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
              <RequisitionCard item={item} onPress={(_id) => handleItemPress(item)} />
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
              title="All caught up"
              hint={
                debouncedSearch
                  ? `No matches for "${debouncedSearch}"`
                  : "No requisitions awaiting costing."
              }
              icon={<Text style={{ fontSize: 32 }}>✓</Text>}
            />
          }
        />
      )}
    </View>
  );
}
