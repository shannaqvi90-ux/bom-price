import { useEffect, useRef } from "react";
import { toast } from "sonner";
import { isStandalone } from "@/utils/platform";
import { usePushSubscription } from "@/features/notifications/usePushSubscription";

const SUPPRESS_KEY = "push-prompt-dismissed-at";
const SUPPRESS_TTL_MS = 14 * 24 * 60 * 60 * 1000;

export function NotificationPermissionPrompt() {
  const { permission, subscribe } = usePushSubscription();
  const shownRef = useRef(false);

  useEffect(() => {
    if (shownRef.current) return;
    if (!isStandalone()) return;
    if (permission !== "default") return;

    const dismissedAt = Number(localStorage.getItem(SUPPRESS_KEY) ?? 0);
    if (Date.now() - dismissedAt < SUPPRESS_TTL_MS) return;

    shownRef.current = true;

    toast("🔔 Get notified when reqs need you?", {
      description: "Enable to receive approval requests and status updates.",
      duration: Infinity,
      action: {
        label: "Enable",
        onClick: async () => {
          await subscribe();
        },
      },
      cancel: {
        label: "Not now",
        onClick: () => {
          localStorage.setItem(SUPPRESS_KEY, String(Date.now()));
        },
      },
    });
  }, [permission, subscribe]);

  return null;
}
