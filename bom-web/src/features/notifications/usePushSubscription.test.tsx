import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";

vi.mock("@/api/pushSubscriptions", () => ({
  pushSubscriptions: { subscribe: vi.fn(), unsubscribe: vi.fn() },
}));

vi.stubEnv("VITE_VAPID_PUBLIC_KEY", "BNxPP9PhIxBjaHv4WdpFrApT7ot3YTeNW0z_uG44VZh3MqcJVDmZ-2I2qRtm6gwKfL0wvtmgrrHpLgSsOQE0aHs");

import { usePushSubscription } from "./usePushSubscription";
import { pushSubscriptions } from "@/api/pushSubscriptions";

const makeFakeSub = (endpoint: string) => ({
  endpoint,
  toJSON: () => ({ endpoint, keys: { p256dh: "p_test", auth: "a_test" } }),
  unsubscribe: vi.fn().mockResolvedValue(true),
});

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(pushSubscriptions.subscribe).mockResolvedValue(undefined);
  vi.mocked(pushSubscriptions.unsubscribe).mockResolvedValue(undefined);
  Object.defineProperty(window, "Notification", {
    value: Object.assign(vi.fn(), {
      permission: "default",
      requestPermission: vi.fn().mockResolvedValue("granted"),
    }),
    configurable: true,
  });
});

describe("usePushSubscription", () => {
  it("subscribe() requests permission, subscribes via PushManager, POSTs to backend", async () => {
    const fakeSub = makeFakeSub("https://x");
    Object.defineProperty(navigator, "serviceWorker", {
      value: {
        ready: Promise.resolve({
          pushManager: {
            getSubscription: vi.fn().mockResolvedValue(null),
            subscribe: vi.fn().mockResolvedValue(fakeSub),
          },
        }),
      },
      configurable: true,
    });

    const { result } = renderHook(() => usePushSubscription());
    await act(async () => {
      await result.current.subscribe();
    });

    expect(Notification.requestPermission).toHaveBeenCalled();
    expect(pushSubscriptions.subscribe).toHaveBeenCalledWith(
      expect.objectContaining({
        endpoint: "https://x",
        keys: { p256dh: "p_test", auth: "a_test" },
      })
    );
    expect(result.current.isSubscribed).toBe(true);
  });

  it("subscribe() bails if permission denied — no backend POST", async () => {
    vi.mocked(Notification.requestPermission).mockResolvedValueOnce("denied");
    Object.defineProperty(navigator, "serviceWorker", {
      value: {
        ready: Promise.resolve({
          pushManager: {
            getSubscription: vi.fn().mockResolvedValue(null),
            subscribe: vi.fn(),
          },
        }),
      },
      configurable: true,
    });

    const { result } = renderHook(() => usePushSubscription());
    await act(async () => {
      await result.current.subscribe();
    });

    expect(pushSubscriptions.subscribe).not.toHaveBeenCalled();
    expect(result.current.isSubscribed).toBe(false);
  });

  it("unsubscribe() calls backend DELETE + browser unsubscribe", async () => {
    const fakeSub = makeFakeSub("https://existing");
    Object.defineProperty(navigator, "serviceWorker", {
      value: {
        ready: Promise.resolve({
          pushManager: {
            getSubscription: vi.fn().mockResolvedValue(fakeSub),
            subscribe: vi.fn(),
          },
        }),
      },
      configurable: true,
    });

    const { result } = renderHook(() => usePushSubscription());
    await waitFor(() => expect(result.current.isSubscribed).toBe(true));

    await act(async () => {
      await result.current.unsubscribe();
    });

    expect(pushSubscriptions.unsubscribe).toHaveBeenCalledWith("https://existing");
    expect(fakeSub.unsubscribe).toHaveBeenCalled();
    expect(result.current.isSubscribed).toBe(false);
  });
});
