import { create } from "zustand";
import * as signalR from "@microsoft/signalr";
import { api, HUB_BASE_URL } from "@/api/axios";
import { notify } from "@/lib/notify";
import { getAppNavigate } from "@/lib/navigator";
import type { Notification } from "@/types/api";

function pathForNotification(n: Notification): string | null {
  switch (n.referenceType) {
    case "QuotationRequest":
      return `/requisitions/${n.referenceId}`;
    default:
      return null;
  }
}

interface NotificationsState {
  notifications: Notification[];
  unreadCount: number;
  connected: boolean;
  _connection: signalR.HubConnection | null;
  connect: (token: string) => Promise<void>;
  disconnect: () => Promise<void>;
  setNotifications: (ns: Notification[]) => void;
  prependNotification: (n: Notification) => void;
  showToastForNotification: (n: Notification) => void;
  markRead: (id: number) => void;
  markAllRead: () => void;
}

export const notificationsStore = create<NotificationsState>()((set, get) => ({
  notifications: [],
  unreadCount: 0,
  connected: false,
  _connection: null,

  connect: async (token: string) => {
    if (get().connected || get()._connection) return;

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${HUB_BASE_URL}/hubs/notifications?access_token=${token}`)
      .withAutomaticReconnect()
      .build();

    connection.on("ReceiveNotification", (n: Notification) => {
      get().prependNotification(n);
      get().showToastForNotification(n);
    });

    set({ _connection: connection });

    try {
      await connection.start();
    } catch {
      // SignalR has built-in exponential backoff via withAutomaticReconnect();
      // keep the user-visible signal to a single toast instead of spamming the
      // console in production builds. Notifications stay reachable via manual
      // refresh until the socket re-establishes.
      set({ _connection: null });
      notify.error("Notifications unavailable — retrying in background.");
      return;
    }

    const { data } = await api.get<{ count: number }>("/notifications/unread-count");
    set({ connected: true, unreadCount: data.count });
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

  showToastForNotification: (n: Notification) => {
    const path = pathForNotification(n);
    notify.info(n.message, {
      action: path
        ? {
            label: "View",
            onClick: () => {
              const navigate = getAppNavigate();
              if (navigate) navigate(path);
              else window.location.assign(path);
            },
          }
        : undefined,
    });
  },

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
