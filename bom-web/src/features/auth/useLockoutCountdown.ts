import { useEffect, useState } from "react";

export interface LockoutCountdown {
  remaining: number;
  isExpired: boolean;
  formatted: string; // mm:ss
}

function formatMmSs(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const minutes = Math.floor(safe / 60);
  const seconds = safe % 60;
  return `${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
}

/**
 * Countdown timer for the login lockout banner.
 *
 * - When `initialSeconds` is null, the hook is in the expired state.
 * - When `initialSeconds` is a positive number, the hook starts at that value
 *   and decrements every second until it reaches 0.
 * - When `initialSeconds` changes (e.g. a fresh lockout response arrives while
 *   already counting), the countdown re-initialises from the new value.
 */
export function useLockoutCountdown(initialSeconds: number | null): LockoutCountdown {
  const [remaining, setRemaining] = useState<number>(initialSeconds ?? 0);
  const [prevInitial, setPrevInitial] = useState(initialSeconds);

  // React-docs "Storing information from previous renders" pattern
  // (https://react.dev/reference/react/useState#storing-information-from-previous-renders).
  // Calling setState during render triggers a render restart — React discards
  // the current render and immediately re-renders with the new state before
  // painting. This is NOT subject to react-hooks/set-state-in-effect (which
  // only targets effect bodies) and does NOT access refs during render.
  if (initialSeconds !== prevInitial) {
    setPrevInitial(initialSeconds);
    setRemaining(initialSeconds ?? 0);
  }

  // Tick down every second while remaining > 0. The functional updater inside
  // setInterval is not subject to the set-state-in-effect lint (callback, not
  // effect body).
  useEffect(() => {
    if (remaining <= 0) return;
    const id = window.setInterval(() => {
      setRemaining((r) => (r > 0 ? r - 1 : 0));
    }, 1000);
    return () => window.clearInterval(id);
  }, [remaining]);

  return {
    remaining,
    isExpired: remaining <= 0,
    formatted: formatMmSs(remaining),
  };
}
