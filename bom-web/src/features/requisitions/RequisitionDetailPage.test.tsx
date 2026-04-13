import { render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import type { ReactNode } from "react";
import type { RequisitionDetail } from "@/types/api";
import { useAuthStore } from "@/store/authStore";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn() } }));

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
  itemId: 2,
  itemDescription: "HDPE Pipe 20mm",
  customerId: 3,
  customerName: "ACME",
  customerEmail: "sales@acme.test",
  customerPhone: "+971501234567",
  customerAddress: "Fujairah FZ",
  expectedQty: 100,
  currencyCode: "AED",
  exchangeRateSnapshot: null,
  branchId: 1,
  branchName: "Fujairah",
  salesPersonId: 10,
  salesPersonName: "Ali",
  createdAt: "2026-04-14T10:00:00Z",
  updatedAt: "2026-04-14T11:00:00Z",
  bom: null,
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
    expect(screen.getByText(/BOM not yet created/i)).toBeInTheDocument();
    expect(screen.getByText(/Not yet submitted for approval/i)).toBeInTheDocument();
    expect(screen.getByTestId("step-Submitted")).toBeInTheDocument();
  });

  it('renders a disabled "Start BOM" button for BomCreator when status is BomPending', async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sample });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    const btn = screen.getByRole("button", { name: /start bom/i });
    expect(btn).toBeDisabled();
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

  it("shows populated BOM and Approval cards when present", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({
      data: {
        ...sample,
        status: "Approved",
        bom: { id: 9, totalCostPerKg: 5.25, hasCost: true },
        approval: { salesPriceAed: 7.5, salesPriceForeign: null, profitMarginPct: 30, isApproved: true },
      },
    });
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByText(/5\.25/)).toBeInTheDocument();
    expect(screen.getByText(/7\.5/)).toBeInTheDocument();
    expect(screen.getByText(/30%/)).toBeInTheDocument();
  });
});
