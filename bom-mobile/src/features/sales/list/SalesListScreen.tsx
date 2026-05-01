import { useMemo, useState } from "react";
import { View, FlatList, RefreshControl, Pressable, Text, ActivityIndicator } from "react-native";
import { useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitions } from "../../../api/requisitions";
import { STATUS_TO_TAB, type ListTab } from "../../../utils/v3StatusMap";
import { StatusTabs } from "./StatusTabs";
import { ReqCard } from "../../../components/ReqCard";
import { theme } from "../../../theme";

export function SalesListScreen() {
  const router = useRouter();
  const [tab, setTab] = useState<ListTab>("active");
  const { data, isLoading, refetch, isRefetching } = useRequisitions();

  const counts = useMemo(() => {
    const c: Record<ListTab, number> = { active: 0, done: 0, closed: 0 };
    (data ?? []).forEach((r) => { c[STATUS_TO_TAB[r.status]] += 1; });
    return c;
  }, [data]);

  const filtered = useMemo(() => {
    return (data ?? []).filter((r) => STATUS_TO_TAB[r.status] === tab);
  }, [data, tab]);

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <StatusTabs current={tab} counts={counts} onChange={setTab} />
      {isLoading ? (
        <ActivityIndicator style={{ marginTop: 40 }} />
      ) : (
        <FlatList
          data={filtered}
          keyExtractor={(r) => String(r.id)}
          renderItem={({ item }) => <ReqCard req={item} onPress={(id) => router.push(`/(sales)/${id}`)} />}
          refreshControl={<RefreshControl refreshing={isRefetching} onRefresh={refetch} />}
          contentContainerStyle={{ paddingVertical: 8, paddingBottom: 88 }}
          ListEmptyComponent={
            <Text style={{ textAlign: "center", marginTop: 48, color: "#64748b" }}>
              No {tab} requisitions yet.
            </Text>
          }
        />
      )}
      <Pressable
        onPress={() => {
          Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
          router.push("/(sales)/new");
        }}
        style={{
          position: "absolute", bottom: 24, right: 24,
          backgroundColor: theme.colors.primary, width: 56, height: 56, borderRadius: 28,
          alignItems: "center", justifyContent: "center",
          shadowColor: "#000", shadowOpacity: 0.15, shadowOffset: { width: 0, height: 4 }, shadowRadius: 6,
          elevation: 6,
        }}
      >
        <Text style={{ color: "white", fontSize: 28, lineHeight: 30, fontWeight: "300" }}>+</Text>
      </Pressable>
    </View>
  );
}
