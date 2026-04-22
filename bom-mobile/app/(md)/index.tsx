import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { useQuery } from "@tanstack/react-query";
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
import { useAuth } from "@/auth/AuthContext";
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
  const { logout } = useAuth();
  const q = useMdPending();

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
          paddingHorizontal: 10,
          paddingVertical: 7,
          borderRadius: 8,
          backgroundColor: "#f1f5f9",
        }}
      >
        <Text style={{ color: "#1e40af", fontSize: 13, fontWeight: "600" }}>
          Log out
        </Text>
      </Pressable>
    </>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader
        label="Managing Director"
        title="Pending approvals"
        count={q.data?.length}
        right={HeaderRight}
      />

      {q.isPending ? (
        <LoadingView variant="list" />
      ) : q.isError ? (
        <View style={{ padding: 16 }}>
          <ErrorBanner
            message={
              q.error instanceof Error
                ? q.error.message
                : "Failed to load pending approvals"
            }
            onRetry={() => q.refetch()}
          />
        </View>
      ) : (
        <FlatList
          data={q.data ?? []}
          keyExtractor={(r) => String(r.id)}
          contentContainerStyle={{ padding: 16, paddingTop: 4 }}
          refreshControl={
            <RefreshControl
              refreshing={q.isRefetching}
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
                delay: 200 + index * 80,
              }}
            >
              <RequisitionCard
                item={item}
                onPress={(id) => router.push(`/(md)/${id}`)}
              />
            </MotiView>
          )}
          ListEmptyComponent={
            <EmptyState
              title="All caught up"
              hint="Nothing pending your review right now."
              icon={<Text style={{ fontSize: 32 }}>✓</Text>}
            />
          }
        />
      )}
    </View>
  );
}
