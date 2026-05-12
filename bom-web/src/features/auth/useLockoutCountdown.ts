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

  // Re-initialise when initialSeconds changes (covers null -> N and N -> M).
  useEffect(() => {
    setRemaining(initialSeconds ?? 0);
  }, [initialSeconds]);

  // Tick down every second while remaining > 0.
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
