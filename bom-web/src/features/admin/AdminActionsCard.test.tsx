import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useAuthStore } from "@/store/authStore";
import { AdminActionsCard } from "./AdminActionsCard";
import type { RequisitionStatus } from "@/types/api";

function makeReq(status: RequisitionStatus) {
  return { id: 1, refNo: "REQ-0001", status };
}

function loginAs(role: "Admin" | "SalesPerson") {
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role,
    userId: 1,
    name: "Test User",
    branchId: null,
    mustChangePassword: false,
  });
}

beforeEach(() => {
  loginAs("Admin");
});

afterEach(() => {
  useAuthStore.getState().logout();
});

describe("AdminActionsCard", () => {
  it("renders for Admin role", () => {
    render(<AdminActionsCard requisition={makeReq("BomPending")} />);
    expect(screen.getByRole("button", { name: /Admin actions/i })).toBeInTheDocument();
  });

  it("does not render for non-Admin role", () => {
    useAuthStore.getState().logout();
    loginAs("SalesPerson");
    const { container } = render(<AdminActionsCard requisition={makeReq("BomPending")} />);
    expect(container.firstChild).toBeNull();
  });

  it("card is collapsed by default — action buttons not visible", () => {
    render(<AdminActionsCard requisition={makeReq("BomPending")} />);
    expect(screen.queryByRole("button", { name: /Delete Requisition/i })).not.toBeInTheDocument();
  });

  it("hides Unlock BOM button when status is BomPending", async () => {
    const user = userEvent.setup();
    render(<AdminActionsCard requisition={makeReq("BomPending")} />);
    await user.click(screen.getByRole("button", { name: /Admin actions/i }));
    expect(screen.queryByRole("button", { name: /Unlock BOM/i })).not.toBeInTheDocument();
  });

  it("shows Unlock BOM when status is CostingPending", async () => {
    const user = userEvent.setup();
    render(<AdminActionsCard requisition={makeReq("CostingPending")} />);
    await user.click(screen.getByRole("button", { name: /Admin actions/i }));
    expect(screen.getByRole("button", { name: /Unlock BOM/i })).toBeInTheDocument();
  });

  it("shows Unlock Costing only when status is MdReview", async () => {
    const user = userEvent.setup();
    render(<AdminActionsCard requisition={makeReq("MdReview")} />);
    await user.click(screen.getByRole("button", { name: /Admin actions/i }));
    expect(screen.getByRole("button", { name: /Unlock Costing/i })).toBeInTheDocument();
  });

  it("does not show Unlock Costing when status is CostingPending", async () => {
    const user = userEvent.setup();
    render(<AdminActionsCard requisition={makeReq("CostingPending")} />);
    await user.click(screen.getByRole("button", { name: /Admin actions/i }));
    expect(screen.queryByRole("button", { name: /Unlock Costing/i })).not.toBeInTheDocument();
  });

  it("shows Rollback Status when status is Approved", async () => {
    const user = userEvent.setup();
    render(<AdminActionsCard requisition={makeReq("Approved")} />);
    await user.click(screen.getByRole("button", { name: /Admin actions/i }));
    expect(screen.getByRole("button", { name: /Rollback Status/i })).toBeInTheDocument();
  });

  it("hides Rollback Status when status is Rejected", async () => {
    const user = userEvent.setup();
    render(<AdminActionsCard requisition={makeReq("Rejected")} />);
    await user.click(screen.getByRole("button", { name: /Admin actions/i }));
    expect(screen.queryByRole("button", { name: /Rollback Status/i })).not.toBeInTheDocument();
  });

  it("Delete Requisition always visible to admin when expanded", async () => {
    const user = userEvent.setup();
    render(<AdminActionsCard requisition={makeReq("BomPending")} />);
    await user.click(screen.getByRole("button", { name: /Admin actions/i }));
    expect(screen.getByRole("button", { name: /Delete Requisition/i })).toBeInTheDocument();
  });
});
