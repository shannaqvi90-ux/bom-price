import { describe, it, expect, vi, beforeEach } from "vitest";
import { clearPwaApiCaches, PWA_API_CACHE_NAMES } from "./pwaCaches";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("clearPwaApiCaches", () => {
  it("calls caches.delete for V3 + legacy V2.3 PWA API cache names", async () => {
    const deleteFn = vi.fn().mockResolvedValue(true);
    Object.defineProperty(window, "caches", {
      value: { delete: deleteFn },
      configurable: true,
    });

    await clearPwaApiCaches();

    // V3 cache names
    expect(deleteFn).toHaveBeenCalledWith("bom-api-list-cache-v3");
    expect(deleteFn).toHaveBeenCalledWith("bom-api-detail-cache-v3");
    // Legacy V2.3 names (cleared on logout in case of mid-upgrade device)
    expect(deleteFn).toHaveBeenCalledWith("bom-api-list-cache");
    expect(deleteFn).toHaveBeenCalledWith("bom-api-detail-cache");
    expect(deleteFn).toHaveBeenCalledTimes(4);
  });

  it("swallows individual delete failures", async () => {
    const deleteFn = vi.fn().mockRejectedValue(new Error("boom"));
    Object.defineProperty(window, "caches", {
      value: { delete: deleteFn },
      configurable: true,
    });

    await expect(clearPwaApiCaches()).resolves.toBeUndefined();
  });

  it("is a no-op when caches API is unavailable", async () => {
    Object.defineProperty(window, "caches", {
      value: undefined,
      configurable: true,
    });

    await expect(clearPwaApiCaches()).resolves.toBeUndefined();
  });

  it("exposes the V3 cache names list", () => {
    expect(PWA_API_CACHE_NAMES).toEqual([
      "bom-api-list-cache-v3",
      "bom-api-detail-cache-v3",
    ]);
  });
});
