import axios, { AxiosError, type InternalAxiosRequestConfig } from "axios";
import Constants from "expo-constants";
import { getAccess, getRefresh, saveTokens, clearTokens } from "@/auth/secureStore";

const baseURL = (Constants.expoConfig?.extra?.apiBaseUrl as string) ?? "http://localhost:7300";

export const api = axios.create({ baseURL, timeout: 15000 });

let refreshPromise: Promise<string> | null = null;

export function __resetRefreshState() {
  refreshPromise = null;
}

api.interceptors.request.use(async (config: InternalAxiosRequestConfig) => {
  const token = await getAccess();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (r) => r,
  async (error: AxiosError<{ code?: string }>) => {
    const original = error.config as InternalAxiosRequestConfig & { _retried?: boolean };
    const isAuthEndpoint =
      original?.url?.includes("/api/auth/login") ||
      original?.url?.includes("/api/auth/refresh");

    const isAuthExpired =
      error.response?.status === 401 &&
      original &&
      !original._retried &&
      !isAuthEndpoint;

    if (!isAuthExpired) return Promise.reject(error);

    // If we've never logged in (no refresh token yet), surface the
    // original 401 instead of the generic "no-refresh-token" error.
    const refresh = await getRefresh();
    if (!refresh) return Promise.reject(error);

    original._retried = true;

    try {
      const newAccess = await (refreshPromise ??= doRefresh());
      original.headers.Authorization = `Bearer ${newAccess}`;
      return api.request(original);
    } catch (e) {
      await clearTokens();
      return Promise.reject(e);
    } finally {
      refreshPromise = null;
    }
  }
);

async function doRefresh(): Promise<string> {
  const refresh = await getRefresh();
  if (!refresh) throw new Error("no-refresh-token");
  const res = await api.post("/api/auth/refresh", { refreshToken: refresh });
  const { accessToken, refreshToken } = res.data as { accessToken: string; refreshToken: string };
  await saveTokens(accessToken, refreshToken);
  return accessToken;
}
