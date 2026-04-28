import { useMutation } from "@tanstack/react-query";
import { api } from "@/api/axios";
import { useAuthStore } from "@/store/authStore";
import { notificationsStore } from "@/store/notificationsStore";
import { clearPwaApiCaches } from "@/utils/pwaCaches";
import { unsubscribePushOnLogout } from "@/features/notifications/usePushSubscription";
import type { LoginRequest, LoginResponse } from "@/types/api";

async function loginRequest(req: LoginRequest): Promise<LoginResponse> {
  const resp = await api.post<LoginResponse>("/auth/login", req);
  return resp.data;
}

export function useLogin() {
  const setSession = useAuthStore((s) => s.setSession);
  return useMutation({
    mutationFn: loginRequest,
    onSuccess: (data) => setSession(data),
  });
}

async function logoutRequest(refreshToken: string): Promise<void> {
  await api.post("/auth/logout", { refreshToken });
}

export function useLogout() {
  const logout = useAuthStore((s) => s.logout);
  const refreshToken = useAuthStore((s) => s.refreshToken);
  return useMutation({
    mutationFn: async () => {
      // Unsubscribe push BEFORE token-clearing requests; the DELETE needs auth.
      await unsubscribePushOnLogout();
      const tasks: Promise<unknown>[] = [clearPwaApiCaches()];
      if (refreshToken) {
        tasks.push(
          logoutRequest(refreshToken).catch(() => {
            // best-effort; local logout still proceeds
          })
        );
      }
      await Promise.all(tasks);
    },
    onSettled: () => {
      notificationsStore.getState().disconnect();
      logout();
    },
  });
}
