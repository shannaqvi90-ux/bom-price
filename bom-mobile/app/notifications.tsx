import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { useRouter } from "expo-router";
import { useMarkReadLocal, useNotifications } from "@/api/notifications";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { formatShortDate } from "@/utils/dates";
import { useAuth } from "@/auth/AuthContext";
import type { Notification } from "@/types/api";

function pathForNotification(
  n: Notification,
  role: string
): string | null {
  if (n.referenceType !== "QuotationRequest") return null;
  if (role === "ManagingDirector") return `/(md)/${n.referenceId}`;
  if (role === "SalesPerson") return `/(sales)/${n.referenceId}`;
  return null;
}

export default function Notifications() {
  const router = useRouter();
  const q = useNotifications();
  const markRead = useMarkReadLocal();
  const { user } = useAuth();

  if (q.isPending) return <LoadingView />;

  return (
    <View className="flex-1 bg-slate-50">
      {q.isError ? (
        <View className="p-4">
          <ErrorBanner
            message={
              q.error instanceof Error ? q.error.message : "Failed to load notifications"
            }
            onRetry={() => q.refetch()}
          />
        </View>
      ) : null}

      <FlatList
        data={q.data ?? []}
        keyExtractor={(n) => String(n.id)}
        contentContainerClassName="p-3"
        refreshControl={
          <RefreshControl refreshing={q.isRefetching} onRefresh={() => q.refetch()} />
        }
        renderItem={({ item }) => (
          <Pressable
            onPress={() => {
              markRead.mutate(item.id);
              const path = user ? pathForNotification(item, user.role) : null;
              if (path) router.push(path);
            }}
            className={`border rounded-md p-3 mb-2 active:bg-slate-50 ${
              item.isRead ? "bg-white border-slate-200" : "bg-brand-50 border-brand-100"
            }`}
          >
            <Text
              className={`text-sm text-slate-900 ${item.isRead ? "" : "font-semibold"}`}
            >
              {item.message}
            </Text>
            <Text className="text-xs text-slate-500 mt-1">
              {formatShortDate(item.createdAt)}
            </Text>
          </Pressable>
        )}
        ListEmptyComponent={
          !q.isError ? (
            <EmptyState
              title="No notifications yet"
              hint="You'll see approval requests and status changes here."
            />
          ) : null
        }
      />
    </View>
  );
}
