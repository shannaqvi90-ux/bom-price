import axios, {
  AxiosError,
  type AxiosRequestConfig,
  type InternalAxiosRequestConfig,
} from "axios";
import { useAuthStore } from "@/store/authStore";
import type { LoginResponse } from "@/types/api";

// Relative base — Vite dev server proxies /api and /hubs to the ASP.NET Core
// backend on port 7300 (see vite.config.ts). In production this should be
// overridden via VITE_API_BASE_URL in a later plan.
export const API_BASE_URL = "/api";

export const api = axios.create({
  baseURL: API_BASE_URL,
  headers: { "Content-Type": "application/json" },
});

api.interceptors.request.use((config) => {
  const token = useAuthStore.getState().accessToken;
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

interface RetryConfig extends InternalAxiosRequestConfig {
  _retried?: boolean;
}

let refreshInFlight: Promise<string | null> | null = null;

async function performRefresh(
  adapter?: AxiosRequestConfig["adapter"],
): Promise<string | null> {
  const refreshToken = useAuthStore.getState().refreshToken;
  if (!refreshToken) return null;

  try {
    const resp = await api.post<LoginResponse>(
      `/auth/refresh`,
      { refreshToken },
      adapter ? { adapter } : undefined,
    );
    useAuthStore
      .getState()
      .updateTokens(resp.data.accessToken, resp.data.refreshToken);
    return resp.data.accessToken;
  } catch {
    useAuthStore.getState().logout();
    return null;
  }
}

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const original = error.config as RetryConfig | undefined;

    if (
      error.response?.status !== 401 ||
      !original ||
      original._retried ||
      original.url?.endsWith("/auth/refresh") ||
      original.url?.endsWith("/auth/login")
    ) {
      return Promise.reject(error);
    }

    original._retried = true;

    if (!refreshInFlight) {
      refreshInFlight = performRefresh(original.adapter).finally(() => {
        refreshInFlight = null;
      });
    }

    const newToken = await refreshInFlight;
    if (!newToken) {
      return Promise.reject(error);
    }

    original.headers.Authorization = `Bearer ${newToken}`;
    return api.request(original as AxiosRequestConfig);
  },
);
