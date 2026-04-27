import axios, {
  AxiosError,
  type AxiosRequestConfig,
  type InternalAxiosRequestConfig,
} from "axios";
import { useAuthStore } from "@/store/authStore";
import type { LoginResponse } from "@/types/api";

// API base URL.
//   - Dev: empty `VITE_API_BASE_URL` → "/api" (Vite dev-server proxies to localhost:7300, see vite.config.ts).
//   - Prod: set `VITE_API_BASE_URL` to e.g. "https://bom-fpf-api.fly.dev" — full origin
//     becomes "<origin>/api". See bom-web/.env.production.
//
// HUB_BASE_URL is the SignalR origin — in dev it's "" (relative, proxied),
// in prod it's the same Fly.io origin. notificationsStore.ts builds the
// hub URL as `${HUB_BASE_URL}/hubs/notifications`.
const apiOrigin = import.meta.env.VITE_API_BASE_URL ?? "";
export const API_BASE_URL = `${apiOrigin}/api`;
export const HUB_BASE_URL = apiOrigin;

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
