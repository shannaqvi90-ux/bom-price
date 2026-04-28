import { describe, it, expect, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useServiceWorker } from "./useServiceWorker";

vi.mock("workbox-window", () => ({
  Workbox: vi.fn().mockImplementation(() => ({
    addEventListener: vi.fn(),
    register: vi.fn().mockResolvedValue(undefined),
    messageSkipWaiting: vi.fn(),
  })),
}));

describe("useServiceWorker", () => {
  it("returns updateAvailable=false initially", () => {
    const { result } = renderHook(() => useServiceWorker());
    expect(result.current.updateAvailable).toBe(false);
  });

  it("exposes applyUpdate function", () => {
    const { result } = renderHook(() => useServiceWorker());
    expect(typeof result.current.applyUpdate).toBe("function");
  });
});
