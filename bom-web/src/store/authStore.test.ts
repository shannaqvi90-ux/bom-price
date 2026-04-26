import { beforeEach, describe, expect, it } from "vitest";
import { useAuthStore } from "./authStore";

describe("authStore", () => {
  beforeEach(() => {
    useAuthStore.getState().logout();
    localStorage.clear();
  });

  it("starts unauthenticated", () => {
    const state = useAuthStore.getState();
    expect(state.user).toBeNull();
    expect(state.accessToken).toBeNull();
    expect(state.refreshToken).toBeNull();
    expect(state.isAuthenticated()).toBe(false);
  });

  it("setSession stores tokens and user and marks authenticated", () => {
    useAuthStore.getState().setSession({
      accessToken: "at.1",
      refreshToken: "rt.1",
      role: "SalesPerson",
      userId: 5,
      name: "Alice",
      branchId: 2,
      mustChangePassword: false,
    });

    const state = useAuthStore.getState();
    expect(state.accessToken).toBe("at.1");
    expect(state.refreshToken).toBe("rt.1");
    expect(state.user).toEqual({
      userId: 5,
      name: "Alice",
      role: "SalesPerson",
      branchId: 2,
      mustChangePassword: false,
    });
    expect(state.isAuthenticated()).toBe(true);
  });

  it("updateTokens replaces tokens without touching user", () => {
    useAuthStore.getState().setSession({
      accessToken: "at.1",
      refreshToken: "rt.1",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
      mustChangePassword: false,
    });

    useAuthStore.getState().updateTokens("at.2", "rt.2");

    const state = useAuthStore.getState();
    expect(state.accessToken).toBe("at.2");
    expect(state.refreshToken).toBe("rt.2");
    expect(state.user?.userId).toBe(1);
  });

  it("initAuth clears session when the access token is expired", () => {
    // JWT with exp = 0 (1970) — always expired
    const expiredJwt =
      "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." +
      // {"exp":0}
      "eyJleHAiOjB9" +
      ".sig";

    useAuthStore.setState({
      accessToken: expiredJwt,
      refreshToken: "rt.1",
      user: {
        userId: 1,
        name: "Admin",
        role: "Admin",
        branchId: null,
        mustChangePassword: false,
      },
    });

    useAuthStore.getState().initAuth();

    const state = useAuthStore.getState();
    expect(state.accessToken).toBeNull();
    expect(state.user).toBeNull();
  });

  it("initAuth keeps session when the access token is still valid", () => {
    const future = Math.floor(Date.now() / 1000) + 600;
    const payload = btoa(JSON.stringify({ exp: future }))
      .replace(/\+/g, "-")
      .replace(/\//g, "_")
      .replace(/=+$/, "");
    const validJwt = `eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.${payload}.sig`;

    useAuthStore.setState({
      accessToken: validJwt,
      refreshToken: "rt.1",
      user: {
        userId: 1,
        name: "Admin",
        role: "Admin",
        branchId: null,
        mustChangePassword: false,
      },
    });

    useAuthStore.getState().initAuth();

    expect(useAuthStore.getState().accessToken).toBe(validJwt);
    expect(useAuthStore.getState().user).not.toBeNull();
  });

  it("clearMustChangePassword flips flag to false without touching other fields", () => {
    useAuthStore.getState().setSession({
      accessToken: "at.1",
      refreshToken: "rt.1",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
      mustChangePassword: true,
    });

    useAuthStore.getState().clearMustChangePassword();

    const state = useAuthStore.getState();
    expect(state.user?.mustChangePassword).toBe(false);
    expect(state.user?.userId).toBe(1);
    expect(state.accessToken).toBe("at.1");
  });

  it("logout clears everything", () => {
    useAuthStore.getState().setSession({
      accessToken: "at.1",
      refreshToken: "rt.1",
      role: "Admin",
      userId: 1,
      name: "Admin",
      branchId: null,
      mustChangePassword: false,
    });

    useAuthStore.getState().logout();

    const state = useAuthStore.getState();
    expect(state.user).toBeNull();
    expect(state.accessToken).toBeNull();
    expect(state.refreshToken).toBeNull();
    expect(state.isAuthenticated()).toBe(false);
  });
});
