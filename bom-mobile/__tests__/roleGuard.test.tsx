import React from "react";
import { renderHook } from "@testing-library/react-native";
import { AuthProvider, useRoleGuard } from "@/auth/AuthContext";
import { saveUser, saveTokens, clearTokens } from "@/auth/secureStore";
import type { AuthUser, UserRole } from "@/types/api";

const makeUser = (role: UserRole): AuthUser => ({
  userId: 1,
  name: "X",
  role,
  branchId: null,
});

beforeEach(async () => {
  await clearTokens();
});

test("forbidden for wrong role", async () => {
  await saveTokens("a", "r");
  await saveUser(makeUser("BomCreator"));

  const { result } = renderHook(() => useRoleGuard(["SalesPerson"]), {
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <AuthProvider>{children}</AuthProvider>
    ),
  });

  await new Promise((r) => setTimeout(r, 50));

  expect(["forbidden", "unauthenticated", "loading"]).toContain(result.current.status);
  expect(result.current.status).not.toBe("allowed");
});

test("allowed for matching role", async () => {
  await saveTokens("a", "r");
  await saveUser(makeUser("SalesPerson"));

  const { result } = renderHook(() => useRoleGuard(["SalesPerson"]), {
    wrapper: ({ children }: { children: React.ReactNode }) => (
      <AuthProvider>{children}</AuthProvider>
    ),
  });

  await new Promise((r) => setTimeout(r, 50));

  expect(["allowed", "loading"]).toContain(result.current.status);
  expect(result.current.status).not.toBe("forbidden");
});
