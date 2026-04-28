import { describe, it, expect, beforeEach, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { usePwaInstall } from "./usePwaInstall";

const setUA = (ua: string, maxTouch = 0) => {
  Object.defineProperty(navigator, "userAgent", { value: ua, configurable: true });
  Object.defineProperty(navigator, "maxTouchPoints", { value: maxTouch, configurable: true });
};

const setStandalone = (matches: boolean, navigatorStandalone = false) => {
  Object.defineProperty(window, "matchMedia", {
    value: () => ({ matches, addEventListener: () => {}, removeEventListener: () => {} }),
    configurable: true,
  });
  Object.defineProperty(navigator, "standalone", { value: navigatorStandalone, configurable: true });
};

beforeEach(() => {
  localStorage.clear();
  setStandalone(false, false);
});

describe("usePwaInstall", () => {
  it("shouldShowIosModal=true on iPhone Safari, not installed, not dismissed", () => {
    setUA("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4) Version/17.4 Safari");
    const { result } = renderHook(() => usePwaInstall());
    expect(result.current.shouldShowIosModal).toBe(true);
    expect(result.current.canPromptInstall).toBe(false);
    expect(result.current.isInstalled).toBe(false);
  });

  it("shouldShowIosModal=false on iPhone Chrome (CriOS)", () => {
    setUA("Mozilla/5.0 (iPhone; CPU iPhone OS 17_4) CriOS/120 Safari");
    const { result } = renderHook(() => usePwaInstall());
    expect(result.current.shouldShowIosModal).toBe(false);
  });

  it("shouldShowIosModal=true on modern iPadOS (Mac UA + touch)", () => {
    setUA("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) Version/17.4 Safari", 5);
    const { result } = renderHook(() => usePwaInstall());
    expect(result.current.shouldShowIosModal).toBe(true);
  });

  it("shouldShowIosModal=false after dismissIosModal called", () => {
    setUA("Mozilla/5.0 (iPhone) Version/17.4 Safari");
    const { result } = renderHook(() => usePwaInstall());
    expect(result.current.shouldShowIosModal).toBe(true);
    act(() => result.current.dismissIosModal());
    expect(result.current.shouldShowIosModal).toBe(false);
  });

  it("shouldShowIosModal=false when already installed (standalone)", () => {
    setUA("Mozilla/5.0 (iPhone) Version/17.4 Safari");
    setStandalone(true);
    const { result } = renderHook(() => usePwaInstall());
    expect(result.current.shouldShowIosModal).toBe(false);
    expect(result.current.isInstalled).toBe(true);
  });

  it("canPromptInstall=true on Android Chrome with deferred prompt", () => {
    setUA("Mozilla/5.0 (Linux; Android 14) Chrome/120", 5);
    const { result } = renderHook(() => usePwaInstall());
    act(() => {
      const e = Object.assign(new Event("beforeinstallprompt"), {
        prompt: vi.fn().mockResolvedValue(undefined),
        userChoice: Promise.resolve({ outcome: "accepted" as const }),
      });
      window.dispatchEvent(e);
    });
    expect(result.current.canPromptInstall).toBe(true);
  });

  it("promptInstall sets isInstalled=true on accepted outcome", async () => {
    setUA("Mozilla/5.0 (Linux; Android 14) Chrome/120", 5);
    const { result } = renderHook(() => usePwaInstall());
    act(() => {
      const e = Object.assign(new Event("beforeinstallprompt"), {
        prompt: vi.fn().mockResolvedValue(undefined),
        userChoice: Promise.resolve({ outcome: "accepted" as const }),
      });
      window.dispatchEvent(e);
    });
    await act(async () => {
      await result.current.promptInstall();
    });
    expect(result.current.isInstalled).toBe(true);
    expect(result.current.canPromptInstall).toBe(false);
  });
});
