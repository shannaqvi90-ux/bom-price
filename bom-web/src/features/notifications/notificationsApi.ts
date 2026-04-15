import { useMutation, useQuery } from "@tanstack/react-query";
import { api } from "@/api/axios";
import { notificationsStore } from "@/store/notificationsStore";
import type { Notification } from "@/types/api";

export function useNotifications() {
  const setNotifications = notificationsStore((s) => s.setNotifications);
  return useQuery({
    queryKey: ["notifications"],
    queryFn: async () => {
      const { data } = await api.get<Notification[]>("/notifications");
      setNotifications(data);
      return data;
    },
  });
}

export function useMarkRead() {
  const markRead = notificationsStore((s) => s.markRead);
  return useMutation({
    mutationFn: (id: number) =>
      api.put(`/notifications/${id}/read`).then(() => id),
    onSuccess: (id: number) => markRead(id),
  });
}

export function useMarkAllRead() {
  const markAllRead = notificationsStore((s) => s.markAllRead);
  return useMutation({
    mutationFn: () => api.put("/notifications/read-all"),
    onSuccess: () => markAllRead(),
  });
}
