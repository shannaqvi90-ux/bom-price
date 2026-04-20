import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { Notification } from "@/types/api";

export const notificationKeys = {
  all: ["notifications"] as const,
  list: () => [...notificationKeys.all, "list"] as const,
  unread: () => [...notificationKeys.all, "unread"] as const,
};

export function useNotifications() {
  return useQuery({
    queryKey: notificationKeys.list(),
    queryFn: async () => {
      const res = await api.get<Notification[]>("/api/notifications");
      return res.data;
    },
    staleTime: 10_000,
  });
}

export function useUnreadCount() {
  return useQuery({
    queryKey: notificationKeys.unread(),
    queryFn: async () => {
      const res = await api.get<{ count: number }>("/api/notifications/unread-count");
      return res.data.count;
    },
    staleTime: 10_000,
  });
}

/**
 * Client-only "mark as read" — backend has no PATCH endpoint (see plan's
 * scope deviations). Updates the TanStack Query cache in place so the UI
 * reflects the read state for the session; the server still sees it unread.
 */
export function useMarkReadLocal() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (id: number) => id,
    onSuccess: (id) => {
      qc.setQueryData<Notification[]>(notificationKeys.list(), (prev) =>
        prev ? prev.map((n) => (n.id === id ? { ...n, isRead: true } : n)) : prev
      );
      qc.setQueryData<number>(notificationKeys.unread(), (prev) =>
        typeof prev === "number" && prev > 0 ? prev - 1 : prev
      );
    },
  });
}
