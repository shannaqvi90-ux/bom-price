import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { AxiosAdapter, AxiosRequestConfig } from "axios";
import { useAuthStore } from "@/store/authStore";
import { api } from "./axios";

vi.mock("axios", async () => {
  const actual = await vi.importActual<typeof import("axios")>("axios");
  return actual;
});

describe("axios client with refresh interceptor", () => {
  beforeEach(() => {
    useAuthStore.getState().logout();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("attaches Authorization header from authStore on requests", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at.original",
      refreshToken: "rt.original",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
    });

    let seenAuth: string | undefined;
    const adapter: AxiosAdapter = async (config: AxiosRequestConfig) => {
      seenAuth = config.headers?.Authorization as string | undefined;
      return {
        data: { ok: true },
        status: 200,
        statusText: "OK",
        headers: {},
        config: config as never,
      };
    };

    // api is statically imported above
    await api.get("/ping", { adapter });

    expect(seenAuth).toBe("Bearer at.original");
  });

  it("on 401 calls /auth/refresh, updates tokens, and retries the original request", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at.expired",
      refreshToken: "rt.valid",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
    });

    const calls: Array<{ url?: string; auth?: string; data?: unknown }> = [];
    let requestCount = 0;

    const adapter: AxiosAdapter = async (config: AxiosRequestConfig) => {
      requestCount += 1;
      calls.push({
        url: config.url,
        auth: config.headers?.Authorization as string | undefined,
        data: config.data,
      });

      if (config.url?.endsWith("/auth/refresh")) {
        return {
          data: {
            accessToken: "at.new",
            refreshToken: "rt.new",
            role: "Admin",
            userId: 1,
            name: "Admin",
            branchId: null,
          },
          status: 200,
          statusText: "OK",
          headers: {},
          config: config as never,
        };
      }

      if (requestCount === 1) {
        const err = new Error("Unauthorized") as Error & {
          response?: unknown;
          config?: unknown;
          isAxiosError?: boolean;
        };
        err.isAxiosError = true;
        err.config = config;
        err.response = {
          status: 401,
          statusText: "Unauthorized",
          data: { message: "expired" },
          headers: {},
          config,
        };
        throw err;
      }

      return {
        data: { ok: true },
        status: 200,
        statusText: "OK",
        headers: {},
        config: config as never,
      };
    };

    // api is statically imported above
    const result = await api.get("/items", { adapter });

    expect(result.data).toEqual({ ok: true });
    expect(useAuthStore.getState().accessToken).toBe("at.new");
    expect(useAuthStore.getState().refreshToken).toBe("rt.new");

    const refreshCall = calls.find((c) => c.url?.endsWith("/auth/refresh"));
    expect(refreshCall).toBeDefined();
    expect(refreshCall?.data).toContain("rt.valid");

    const retry = calls.find(
      (c) => c.url === "/items" && c.auth === "Bearer at.new",
    );
    expect(retry).toBeDefined();
  });
});
