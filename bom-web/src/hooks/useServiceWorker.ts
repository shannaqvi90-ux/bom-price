import { useEffect, useState } from "react";
import { Workbox } from "workbox-window";

interface ServiceWorkerState {
  updateAvailable: boolean;
  applyUpdate: () => void;
}

let wbInstance: Workbox | null = null;

export function useServiceWorker(): ServiceWorkerState {
  const [updateAvailable, setUpdateAvailable] = useState(false);

  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;
    if (import.meta.env.DEV) return;

    if (!wbInstance) {
      wbInstance = new Workbox("/sw.js", { scope: "/" });
      wbInstance.addEventListener("waiting", () => setUpdateAvailable(true));
      wbInstance.addEventListener("controlling", () => window.location.reload());
      wbInstance.register().catch((err) => {
        console.warn("Service worker registration failed", err);
      });
    }
  }, []);

  const applyUpdate = () => {
    if (!wbInstance) return;
    wbInstance.messageSkipWaiting();
  };

  return { updateAvailable, applyUpdate };
}
