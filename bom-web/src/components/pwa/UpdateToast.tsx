import { useEffect, useRef } from "react";
import { toast } from "sonner";
import { useServiceWorker } from "@/hooks/useServiceWorker";

export function UpdateToast() {
  const { updateAvailable, applyUpdate } = useServiceWorker();
  const shownRef = useRef(false);

  useEffect(() => {
    if (!updateAvailable || shownRef.current) return;
    shownRef.current = true;
    toast("Naya version available", {
      description: "Refresh karne par new version active hoga.",
      duration: Infinity,
      action: {
        label: "Refresh now",
        onClick: applyUpdate,
      },
    });
  }, [updateAvailable, applyUpdate]);

  return null;
}
