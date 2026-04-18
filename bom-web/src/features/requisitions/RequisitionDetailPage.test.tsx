import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import type { ReactNode } from "react";
import type { RequisitionDetail } from "@/types/api";
import { useAuthStore } from "@/store/authStore";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

import { api } from "@/api/axios";
import RequisitionDetailPage from "./RequisitionDetailPage";

function wrap(ui: ReactNode, path = "/requisitions/1") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/requisitions/:id" element={ui} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

const sample: RequisitionDetail = {
  id: 1,
  refNo: "REQ-0001",
  status: "BomPending",
  customerId: 3,
  customerName: "ACME",
  customerEmail: "sales@acme.test",
  customerPhone: "+971501234567",
  customerAddress: "Fujairah FZ",
  currencyCode: "AED",
  exchangeRateSnapshot: null,
  branchId: 1,
  branchName: "Fujairah",
  salesPersonId: 10,
  salesPersonName: "Ali",
  createdAt: "2026-04-14T10:00:00Z",
  updatedAt: "2026-04-14T11:00:00Z",
  items: [
    { id: 1, itemId: 2, itemDescription: "HDPE Pipe 20mm", expectedQty: 100, sortOrder: 1 },
  ],
  approval: null,
};

describe("RequisitionDetailPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "BomCreator",
      userId: 11,
      name: "Bob",
      branchId: 1,
    });
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("renders the header, timeline, and summary cards", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sample });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByText("ACME")).toBeInTheDocument();
    expect(screen.getByText(/Not yet submitted for approval/i)).toBeInTheDocument();
    expect(screen.getByTestId("step-Submitted")).toBeInTheDocument();
  });

  it('renders an enabled "Start BOM" button for BomCreator when status is BomPending', async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sample });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    const btn = screen.getByRole("button", { name: /start bom/i });
    expect(btn).not.toBeDisabled();
  });

  it("does not render action buttons for SalesPerson", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "SalesPerson",
      userId: 10,
      name: "Ali",
      branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValueOnce({ data: sample });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /start bom/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /start costing/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /review/i })).not.toBeInTheDocument();
  });

  it('shows a "not found" card on 404', async () => {
    vi.mocked(api.get).mockRejectedValueOnce({ response: { status: 404 } });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() =>
      expect(screen.getByText(/requisition not found/i)).toBeInTheDocument(),
    );
  });

  it("shows an access-denied card on 403", async () => {
    vi.mocked(api.get).mockRejectedValueOnce({ response: { status: 403 } });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() =>
      expect(screen.getByText(/don't have access/i)).toBeInTheDocument(),
    );
  });

  it("Start Costing button navigates to the costing page", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "Accountant", userId: 11, name: "Bob", branchId: null,
    });
    vi.mocked(api.get).mockResolvedValue({ data: { ...sample, status: "CostingPending" } });

    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={client}>
        <MemoryRouter initialEntries={["/requisitions/1"]}>
          <Routes>
            <Route path="/requisitions/:id" element={<RequisitionDetailPage />} />
            <Route path="/requisitions/:id/costing" element={<div>Costing Page</div>} />
          </Routes>
        </MemoryRouter>
      </QueryClientProvider>,
    );

    const btn = await screen.findByRole("button", { name: /start costing/i });
    await userEvent.click(btn);

    await waitFor(() =>
      expect(screen.getByText("Costing Page")).toBeInTheDocument(),
    );
  });

  it("shows populated Approval card when present", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({
      data: {
        ...sample,
        status: "Approved",
        approval: { isApproved: true, notes: null, approvedAt: "2026-04-15T12:00:00Z" },
      },
    });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getAllByText("Approved").length).toBeGreaterThanOrEqual(1);
  });

  it("renders rejection reason block when approval.isApproved is false", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "SalesPerson", userId: 10, name: "Ali", branchId: 1,
    });
    const rejected: RequisitionDetail = {
      ...sample,
      status: "Rejected",
      approval: {
        isApproved: false,
        notes: "Margin too low",
        approvedAt: "2026-04-15T12:00:00Z",
      },
    };
    vi.mocked(api.get).mockResolvedValueOnce({ data: rejected });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    expect(screen.getByText("Rejection reason")).toBeInTheDocument();
    expect(screen.getByText("Margin too low")).toBeInTheDocument();
    const notesEl = screen.getByText("Margin too low").closest("div");
    expect(notesEl).toHaveClass("text-destructive");
  });

  it("renders notes block (non-destructive) when approval.isApproved is true", async () => {
    const approved: RequisitionDetail = {
      ...sample,
      status: "Approved",
      approval: {
        isApproved: true,
        notes: "Approved with conditions",
        approvedAt: "2026-04-15T12:00:00Z",
      },
    };
    vi.mocked(api.get).mockResolvedValueOnce({ data: approved });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    expect(screen.getByText("Notes")).toBeInTheDocument();
    expect(screen.getByText("Approved with conditions")).toBeInTheDocument();
  });
});
