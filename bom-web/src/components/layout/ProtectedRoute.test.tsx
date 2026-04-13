import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { ProtectedRoute } from "./ProtectedRoute";
import { useAuthStore } from "@/store/authStore";
import type { UserRole } from "@/types/api";

function renderAt(path: string, allow?: UserRole[]) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/login" element={<div>LOGIN</div>} />
        <Route path="/dashboard" element={<div>DASH</div>} />
        <Route
          path="/admin"
          element={
            <ProtectedRoute allow={allow}>
              <div>ADMIN</div>
            </ProtectedRoute>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ProtectedRoute", () => {
  beforeEach(() => {
    useAuthStore.getState().logout();
  });

  it("redirects to /login when unauthenticated", () => {
    renderAt("/admin");
    expect(screen.getByText("LOGIN")).toBeInTheDocument();
  });

  it("renders children when authenticated and no role restriction", () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "Admin",
      userId: 1,
      name: "A",
      branchId: null,
    });
    renderAt("/admin");
    expect(screen.getByText("ADMIN")).toBeInTheDocument();
  });

  it("redirects to /dashboard when role not in allow list", () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "SalesPerson",
      userId: 1,
      name: "A",
      branchId: 1,
    });
    renderAt("/admin", ["Admin"]);
    expect(screen.getByText("DASH")).toBeInTheDocument();
  });

  it("renders children when role is in allow list", () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "Admin",
      userId: 1,
      name: "A",
      branchId: null,
    });
    renderAt("/admin", ["Admin"]);
    expect(screen.getByText("ADMIN")).toBeInTheDocument();
  });
});
