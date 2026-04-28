import { useEffect, useState } from "react";
import { isIOSorIPadOS, isSafari, isStandalone, isAndroidChrome } from "@/utils/platform";

interface BeforeInstallPromptEvent extends Event {
  prompt(): Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed" }>;
}

interface PwaInstallState {
  canPromptInstall: boolean;
  shouldShowIosModal: boolean;
  isInstalled: boolean;
  promptInstall: () => Promise<void>;
  dismissIosModal: () => void;
}

const DISMISS_KEY = "pwa-install-modal-dismissed";
const DISMISS_TTL_MS = 30 * 24 * 60 * 60 * 1000;

export function usePwaInstall(): PwaInstallState {
  const [deferredPrompt, setDeferredPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  const [isInstalled, setIsInstalled] = useState<boolean>(() => isStandalone());
  const [dismissTick, setDismissTick] = useState(0);

  useEffect(() => {
    const beforeHandler = (e: Event) => {
      e.preventDefault();
      setDeferredPrompt(e as BeforeInstallPromptEvent);
    };
    const installedHandler = () => setIsInstalled(true);
    window.addEventListener("beforeinstallprompt", beforeHandler);
    window.addEventListener("appinstalled", installedHandler);
    return () => {
      window.removeEventListener("beforeinstallprompt", beforeHandler);
      window.removeEventListener("appinstalled", installedHandler);
    };
  }, []);

  const dismissedAt = Number(localStorage.getItem(DISMISS_KEY) ?? 0);
  const dismissedRecently = Date.now() - dismissedAt < DISMISS_TTL_MS;
  void dismissTick;

  const shouldShowIosModal =
    isIOSorIPadOS() && isSafari() && !isInstalled && !dismissedRecently;

  const canPromptInstall = isAndroidChrome() && !isInstalled && deferredPrompt !== null;

  const promptInstall = async () => {
    if (!deferredPrompt) return;
    await deferredPrompt.prompt();
    const result = await deferredPrompt.userChoice;
    if (result.outcome === "accepted") setIsInstalled(true);
    setDeferredPrompt(null);
  };

  const dismissIosModal = () => {
    localStorage.setItem(DISMISS_KEY, String(Date.now()));
    setDismissTick((t) => t + 1);
  };

  return { canPromptInstall, shouldShowIosModal, isInstalled, promptInstall, dismissIosModal };
}
