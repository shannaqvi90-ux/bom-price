import { describe, it, expect, vi, beforeEach } from "vitest";
import { clearPwaApiCaches, PWA_API_CACHE_NAMES } from "./pwaCaches";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("clearPwaApiCaches", () => {
  it("calls caches.delete for both PWA API cache names", async () => {
    const deleteFn = vi.fn().mockResolvedValue(true);
    Object.defineProperty(window, "caches", {
      value: { delete: deleteFn },
      configurable: true,
    });

    await clearPwaApiCaches();

    expect(deleteFn).toHaveBeenCalledWith("bom-api-list-cache");
    expect(deleteFn).toHaveBeenCalledWith("bom-api-detail-cache");
    expect(deleteFn).toHaveBeenCalledTimes(2);
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

  it("exposes the cache names list", () => {
    expect(PWA_API_CACHE_NAMES).toEqual(["bom-api-list-cache", "bom-api-detail-cache"]);
  });
});
