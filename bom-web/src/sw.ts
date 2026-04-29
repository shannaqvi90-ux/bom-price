/// <reference lib="webworker" />
import { precacheAndRoute } from "workbox-precaching";
import { registerRoute } from "workbox-routing";
import { NetworkFirst } from "workbox-strategies";
import { ExpirationPlugin } from "workbox-expiration";
import { CacheableResponsePlugin } from "workbox-cacheable-response";

declare const self: ServiceWorkerGlobalScope & { __WB_MANIFEST: Array<{ url: string; revision: string | null }> };

precacheAndRoute(self.__WB_MANIFEST);

const apiListPattern = /\/api\/(requisitions|customers|items|branches|users|groups)$/;
const apiDetailPattern = /\/api\/(requisitions|customers|items|bom|costing|approvals)\/\d+/;

registerRoute(
  ({ url }) => apiListPattern.test(url.pathname),
  new NetworkFirst({
    cacheName: "bom-api-list-cache-v3",
    networkTimeoutSeconds: 5,
    plugins: [
      new CacheableResponsePlugin({ statuses: [200] }),
      new ExpirationPlugin({ maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 }),
    ],
  })
);

registerRoute(
  ({ url }) => apiDetailPattern.test(url.pathname),
  new NetworkFirst({
    cacheName: "bom-api-detail-cache-v3",
    networkTimeoutSeconds: 5,
    plugins: [
      new CacheableResponsePlugin({ statuses: [200] }),
      new ExpirationPlugin({ maxEntries: 200, maxAgeSeconds: 60 * 60 * 24 }),
    ],
  })
);

self.addEventListener("message", (event) => {
  if (event.data?.type === "SKIP_WAITING") {
    self.skipWaiting();
  }
});

self.addEventListener("push", (event: PushEvent) => {
  let title = "FPF Quotations";
  let body = "You have a new notification";
  if (event.data) {
    try {
      const data = event.data.json() as { title?: string; body?: string };
      if (typeof data.title === "string") title = data.title;
      if (typeof data.body === "string") body = data.body;
    } catch {
      body = event.data.text();
    }
  }
  event.waitUntil(
    self.registration.showNotification(title, {
      body,
      icon: "/pwa-192x192.png",
      badge: "/pwa-192x192.png",
      tag: "bom-notification",
    })
  );
});

self.addEventListener("notificationclick", (event: NotificationEvent) => {
  event.notification.close();
  event.waitUntil(
    self.clients.matchAll({ type: "window", includeUncontrolled: true }).then((clients) => {
      for (const client of clients) {
        if ("focus" in client) return client.focus();
      }
      if (self.clients.openWindow) return self.clients.openWindow("/");
    })
  );
});
