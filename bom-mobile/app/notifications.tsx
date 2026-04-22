import { useState } from "react";
import { FlatList, Pressable, RefreshControl, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useMarkReadLocal, useNotifications } from "@/api/notifications";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { ScreenHeader } from "@/components/ScreenHeader";
import { formatShortDate } from "@/utils/dates";
import { stripTags } from "@/utils/text";
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
  const [pressed, setPressed] = useState<number | null>(null);

  const notifications = q.data ?? [];
  const unreadCount = notifications.filter((n) => !n.isRead).length;

  const BackButton = (
    <Pressable
      onPress={() => router.back()}
      style={{
        paddingHorizontal: 12,
        paddingVertical: 9,
        borderRadius: 8,
        backgroundColor: "#f1f5f9",
      }}
    >
      <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>
        Back
      </Text>
    </Pressable>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader
        label="Inbox"
        title="Notifications"
        count={unreadCount}
        right={BackButton}
      />

      {q.isPending ? (
        <LoadingView variant="list" />
      ) : (
        <>
          {q.isError ? (
            <View style={{ paddingHorizontal: 16, paddingTop: 8 }}>
              <ErrorBanner
                message={
                  q.error instanceof Error
                    ? q.error.message
                    : "Failed to load notifications"
                }
                onRetry={() => q.refetch()}
              />
            </View>
          ) : null}

          <FlatList
            data={notifications}
            keyExtractor={(n) => String(n.id)}
            contentContainerStyle={{ padding: 16, paddingTop: 4 }}
            overScrollMode="never"
            bounces={false}
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
                  delay: index < 15 ? 200 + index * 70 : 0,
                }}
              >
                <Pressable
                  onPressIn={() => {
                    setPressed(item.id);
                    Haptics.selectionAsync();
                  }}
                  onPressOut={() => setPressed(null)}
                  onPress={() => {
                    markRead.mutate(item.id);
                    const path = user ? pathForNotification(item, user.role) : null;
                    if (path) router.push(path as Parameters<typeof router.push>[0]);
                  }}
                >
                  <MotiView
                    animate={{ scale: pressed === item.id ? 0.98 : 1 }}
                    transition={{ type: "spring", damping: 16, stiffness: 280 }}
                    style={
                      item.isRead
                        ? {
                            backgroundColor: "#ffffff",
                            borderWidth: 1,
                            borderColor: "#e2e8f0",
                            borderRadius: 14,
                            padding: 14,
                            marginBottom: 10,
                          }
                        : {
                            backgroundColor: "#eff6ff",
                            borderLeftWidth: 3,
                            borderLeftColor: "#1e40af",
                            borderTopWidth: 1,
                            borderTopColor: "#e2e8f0",
                            borderRightWidth: 1,
                            borderRightColor: "#e2e8f0",
                            borderBottomWidth: 1,
                            borderBottomColor: "#e2e8f0",
                            borderRadius: 14,
                            padding: 14,
                            marginBottom: 10,
                          }
                    }
                  >
                    <Text
                      style={{
                        fontSize: 15,
                        color: "#475569",
                        fontWeight: item.isRead ? "400" : "600",
                      }}
                    >
                      {stripTags(item.message)}
                    </Text>
                    <Text style={{ fontSize: 13, color: "#94a3b8", marginTop: 4 }}>
                      {formatShortDate(item.createdAt)}
                    </Text>
                  </MotiView>
                </Pressable>
              </MotiView>
            )}
            ListEmptyComponent={
              !q.isError ? (
                <EmptyState
                  title="No notifications yet"
                  hint="You'll see approval requests and status changes here."
                  icon={<Text style={{ fontSize: 40 }}>🔔</Text>}
                />
              ) : null
            }
          />
        </>
      )}
    </View>
  );
}
