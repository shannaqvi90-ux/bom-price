import { create } from "zustand";
import * as signalR from "@microsoft/signalr";
import { api } from "@/api/axios";
import type { Notification } from "@/types/api";

interface NotificationsState {
  notifications: Notification[];
  unreadCount: number;
  connected: boolean;
  _connection: signalR.HubConnection | null;
  connect: (token: string) => Promise<void>;
  disconnect: () => Promise<void>;
  setNotifications: (ns: Notification[]) => void;
  prependNotification: (n: Notification) => void;
  markRead: (id: number) => void;
  markAllRead: () => void;
}

export const notificationsStore = create<NotificationsState>()((set, get) => ({
  notifications: [],
  unreadCount: 0,
  connected: false,
  _connection: null,

  connect: async (token: string) => {
    if (get().connected) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/hubs/notifications?access_token=${token}`)
      .withAutomaticReconnect()
      .build();

    connection.on("ReceiveNotification", (n: Notification) => {
      get().prependNotification(n);
    });

    await connection.start();

    const { data } = await api.get<{ count: number }>("/notifications/unread-count");
    set({ connected: true, _connection: connection, unreadCount: data.count });
  },

  disconnect: async () => {
    const conn = get()._connection;
    if (conn) await conn.stop();
    set({ connected: false, _connection: null, notifications: [], unreadCount: 0 });
  },

  setNotifications: (ns: Notification[]) => set({ notifications: ns }),

  prependNotification: (n: Notification) =>
    set((state) => ({
      notifications: [n, ...state.notifications],
      unreadCount: state.unreadCount + 1,
    })),

  markRead: (id: number) =>
    set((state) => ({
      notifications: state.notifications.map((n) =>
        n.id === id ? { ...n, isRead: true } : n,
      ),
      unreadCount: Math.max(0, state.unreadCount - 1),
    })),

  markAllRead: () =>
    set((state) => ({
      notifications: state.notifications.map((n) => ({ ...n, isRead: true })),
      unreadCount: 0,
    })),
}));
