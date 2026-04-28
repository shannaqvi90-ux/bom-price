import { useEffect, useState, useCallback } from "react";
import { pushSubscriptions } from "@/api/pushSubscriptions";
import { urlBase64ToUint8Array } from "@/utils/vapid";

function getVapidKey(): string {
  return (import.meta.env.VITE_VAPID_PUBLIC_KEY ?? "") as string;
}

export interface UsePushSubscriptionState {
  permission: NotificationPermission;
  isSubscribed: boolean;
  subscribe: () => Promise<void>;
  unsubscribe: () => Promise<void>;
}

export function usePushSubscription(): UsePushSubscriptionState {
  const [permission, setPermission] = useState<NotificationPermission>(
    typeof Notification !== "undefined" ? Notification.permission : "default"
  );
  const [isSubscribed, setIsSubscribed] = useState(false);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    let cancelled = false;
    navigator.serviceWorker.ready
      .then(async (reg) => {
        const sub = await reg.pushManager.getSubscription();
        if (!cancelled) setIsSubscribed(sub !== null);
      })
      .catch(() => {
        if (!cancelled) setIsSubscribed(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const subscribe = useCallback(async () => {
    const vapidKey = getVapidKey();
    if (!vapidKey) {
      console.warn("VITE_VAPID_PUBLIC_KEY not configured — cannot subscribe to push");
      return;
    }
    if (!("serviceWorker" in navigator) || typeof Notification === "undefined") return;

    const result = await Notification.requestPermission();
    setPermission(result);
    if (result !== "granted") return;

    const reg = await navigator.serviceWorker.ready;
    const existing = await reg.pushManager.getSubscription();
    const sub =
      existing ??
      (await reg.pushManager.subscribe({
        userVisibleOnly: true,
        applicationServerKey: urlBase64ToUint8Array(vapidKey) as BufferSource,
      }));

    const json = sub.toJSON();
    if (!json.endpoint || !json.keys?.p256dh || !json.keys?.auth) {
      throw new Error("PushSubscription missing required fields");
    }

    await pushSubscriptions.subscribe({
      endpoint: json.endpoint,
      keys: { p256dh: json.keys.p256dh, auth: json.keys.auth },
      userAgent: navigator.userAgent,
    });
    setIsSubscribed(true);
  }, []);

  const unsubscribe = useCallback(async () => {
    if (!("serviceWorker" in navigator)) return;
    const reg = await navigator.serviceWorker.ready;
    const sub = await reg.pushManager.getSubscription();
    if (!sub) {
      setIsSubscribed(false);
      return;
    }
    await pushSubscriptions.unsubscribe(sub.endpoint).catch(() => {});
    await sub.unsubscribe();
    setIsSubscribed(false);
  }, []);

  return { permission, isSubscribed, subscribe, unsubscribe };
}
