import { api } from "@/api/axios";

export interface PushSubscribePayload {
  endpoint: string;
  keys: { p256dh: string; auth: string };
  userAgent?: string;
}

export const pushSubscriptions = {
  subscribe: async (payload: PushSubscribePayload): Promise<void> => {
    await api.post("/notifications/push-subscribe", payload);
  },
  unsubscribe: async (endpoint: string): Promise<void> => {
    await api.delete("/notifications/push-subscribe", { data: { endpoint } });
  },
};
