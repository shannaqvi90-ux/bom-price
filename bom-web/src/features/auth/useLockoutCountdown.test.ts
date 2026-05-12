import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useLockoutCountdown } from "./useLockoutCountdown";

describe("useLockoutCountdown", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("returns expired immediately when initial is null", () => {
    const { result } = renderHook(() => useLockoutCountdown(null));
    expect(result.current.remaining).toBe(0);
    expect(result.current.isExpired).toBe(true);
    expect(result.current.formatted).toBe("00:00");
  });

  it("decrements every second and emits isExpired at 0", () => {
    const { result } = renderHook(() => useLockoutCountdown(3));

    expect(result.current.remaining).toBe(3);
    expect(result.current.isExpired).toBe(false);
    expect(result.current.formatted).toBe("00:03");

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(result.current.remaining).toBe(2);
    expect(result.current.formatted).toBe("00:02");

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(result.current.remaining).toBe(1);

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(result.current.remaining).toBe(0);
    expect(result.current.isExpired).toBe(true);
    expect(result.current.formatted).toBe("00:00");
  });

  it("re-initialises when initial value changes (new lockout while counting)", () => {
    let initial: number | null = 5;
    const { result, rerender } = renderHook(() => useLockoutCountdown(initial));

    expect(result.current.remaining).toBe(5);

    act(() => {
      vi.advanceTimersByTime(2000);
    });
    expect(result.current.remaining).toBe(3);

    // Fresh lockout response from backend with a larger value
    initial = 900;
    rerender();
    expect(result.current.remaining).toBe(900);
    expect(result.current.isExpired).toBe(false);
    expect(result.current.formatted).toBe("15:00");
  });
});
