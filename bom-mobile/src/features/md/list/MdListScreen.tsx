import { useMemo, useState } from "react";
import { FlatList, Text, View, RefreshControl, Pressable } from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { useRequisitions } from "@/api/requisitions";
import { ScreenHeader } from "@/components/ScreenHeader";
import { ReqCard } from "@/components/ReqCard";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { EmptyState } from "@/components/EmptyState";
import { MdTabs } from "./MdTabs";
import { InFlightSubFilterChips } from "./InFlightSubFilterChips";
import { statusesForTab, type MdTab, type InFlightSubFilter } from "./tabFilters";

export function MdListScreen() {
  const router = useRouter();
  const params = useLocalSearchParams<{ tab?: string; filter?: string; from?: string }>();

  const VALID_TABS: MdTab[] = ["queue", "in-flight", "done", "closed"];
  const VALID_FILTERS: InFlightSubFilter[] = ["all", "customer", "costing"];
  const initialTab: MdTab = VALID_TABS.includes(params.tab as MdTab)
    ? (params.tab as MdTab)
    : "queue";
  const initialFilter: InFlightSubFilter = VALID_FILTERS.includes(params.filter as InFlightSubFilter)
    ? (params.filter as InFlightSubFilter)
    : "all";

  const [tab, setTab] = useState<MdTab>(initialTab);
  const [sub, setSub] = useState<InFlightSubFilter>(initialFilter);
  const [from, setFrom] = useState<string | undefined>(params.from);

  const statuses = useMemo(() => statusesForTab(tab, sub), [tab, sub]);
  const reqsQ = useRequisitions({ statuses, from });

  const onTabChange = (next: MdTab) => {
    setTab(next);
    setSub("all");
    if (next !== "in-flight") setFrom(undefined);
  };

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />
      <ScreenHeader title="Requisitions" back />

      <MdTabs active={tab} onChange={onTabChange} />
      {tab === "in-flight" ? (
        <InFlightSubFilterChips active={sub} onChange={setSub} />
      ) : null}

      {tab === "in-flight" && from ? (
        <View style={{
          flexDirection: "row",
          alignItems: "center",
          paddingHorizontal: 12,
          marginBottom: 8,
        }}>
          <View style={{
            backgroundColor: "#fef3c7",
            paddingHorizontal: 10,
            paddingVertical: 6,
            borderRadius: 999,
            flexDirection: "row",
            alignItems: "center",
          }}>
            <Text style={{ fontSize: 12, color: "#92400e", fontWeight: "600" }}>
              From {from}
            </Text>
            <Pressable onPress={() => setFrom(undefined)} style={{ marginLeft: 8 }}>
              <Text style={{ fontSize: 12, color: "#92400e", fontWeight: "700" }}>×</Text>
            </Pressable>
          </View>
        </View>
      ) : null}

      {reqsQ.isPending ? (
        <LoadingView variant="list" />
      ) : reqsQ.isError ? (
        <ErrorBanner message="Failed to load requisitions" onRetry={() => reqsQ.refetch()} />
      ) : (reqsQ.data ?? []).length === 0 ? (
        <EmptyState
          title={emptyTitleFor(tab, sub)}
          hint="Nothing to show here."
        />
      ) : (
        <FlatList
          data={reqsQ.data}
          keyExtractor={(r) => String(r.id)}
          renderItem={({ item }) => (
            <ReqCard req={item} onPress={(id) => router.push(`/(md)/${id}` as Parameters<typeof router.push>[0])} />
          )}
          refreshControl={
            <RefreshControl
              refreshing={reqsQ.isFetching}
              onRefresh={() => reqsQ.refetch()}
              tintColor="#1e40af"
            />
          }
        />
      )}
    </View>
  );
}

function emptyTitleFor(tab: MdTab, sub: InFlightSubFilter): string {
  if (tab === "queue") return "Nothing to price or sign";
  if (tab === "done") return "Nothing signed yet";
  if (tab === "closed") return "Nothing closed";
  if (sub === "customer") return "Nothing awaiting customer";
  if (sub === "costing") return "Nothing back in costing";
  return "Nothing in flight";
}
