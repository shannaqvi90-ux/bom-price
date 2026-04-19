import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { getAccess, getRefresh, getUser, clearTokens, saveUser } from "./secureStore";
import { login as apiLogin, logout as apiLogout } from "@/api/auth";
import type { AuthUser } from "@/types/api";

interface AuthState {
  user: AuthUser | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<AuthUser>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthState | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    (async () => {
      try {
        const [access, refresh, cachedUser] = await Promise.all([
          getAccess(),
          getRefresh(),
          getUser<AuthUser>(),
        ]);
        if ((access || refresh) && cachedUser) {
          setUser(cachedUser);
        } else if (!access && !refresh) {
          await clearTokens();
        }
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const login = async (email: string, password: string) => {
    const u = await apiLogin(email, password);
    await saveUser(u);
    setUser(u);
    return u;
  };

  const logout = async () => {
    await apiLogout();
    setUser(null);
  };

  return (
    <AuthContext.Provider value={{ user, loading, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used inside AuthProvider");
  return ctx;
}

export function useRoleGuard(allowed: Array<"SalesPerson" | "ManagingDirector">) {
  const { user, loading } = useAuth();
  const allowedSet = allowed as readonly string[];
  if (loading) return { status: "loading" as const };
  if (!user) return { status: "unauthenticated" as const };
  if (!allowedSet.includes(user.role)) return { status: "forbidden" as const };
  return { status: "allowed" as const };
}
