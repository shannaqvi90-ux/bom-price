import { render, screen } from "@testing-library/react";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { beforeEach, describe, expect, it } from "vitest";
import { ForceChangePasswordGuard } from "./ForceChangePasswordGuard";
import { useAuthStore } from "@/store/authStore";

function setUser(mustChangePassword: boolean) {
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role: "Admin",
    userId: 1,
    name: "Test User",
    branchId: null,
    mustChangePassword,
  });
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route
          path="/dashboard"
          element={
            <ForceChangePasswordGuard>
              <div>dashboard</div>
            </ForceChangePasswordGuard>
          }
        />
        <Route
          path="/change-password"
          element={
            <ForceChangePasswordGuard>
              <div>change pw page</div>
            </ForceChangePasswordGuard>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
}

describe("ForceChangePasswordGuard", () => {
  beforeEach(() => {
    useAuthStore.getState().logout();
  });

  it("redirects to /change-password when mustChangePassword is true", () => {
    setUser(true);
    renderAt("/dashboard");
    expect(screen.getByText("change pw page")).toBeInTheDocument();
  });

  it("does NOT redirect when mustChangePassword is false", () => {
    setUser(false);
    renderAt("/dashboard");
    expect(screen.getByText("dashboard")).toBeInTheDocument();
  });

  it("does NOT redirect when already on /change-password (avoids infinite loop)", () => {
    setUser(true);
    renderAt("/change-password");
    expect(screen.getByText("change pw page")).toBeInTheDocument();
  });
});
