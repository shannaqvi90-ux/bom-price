import { api } from "./client";
import { saveTokens, saveUser, clearTokens } from "@/auth/secureStore";
import type { AuthUser, LoginResponse } from "@/types/api";

export async function login(email: string, password: string): Promise<AuthUser> {
  const res = await api.post<LoginResponse>("/api/auth/login", { email, password });
  const { accessToken, refreshToken, role, userId, name, branchId } = res.data;
  await saveTokens(accessToken, refreshToken);
  const user: AuthUser = { userId, name, role, branchId };
  await saveUser(user);
  return user;
}

export async function logout() {
  try {
    await api.post("/api/auth/logout");
  } catch {
    // server logout is best-effort
  } finally {
    await clearTokens();
  }
}
