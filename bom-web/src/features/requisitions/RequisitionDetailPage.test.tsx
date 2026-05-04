import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import type { ReactNode } from "react";
import type { V3Requisition } from "@/types/api";
import { useAuthStore } from "@/store/authStore";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

vi.mock("@/api/branches", () => ({
  useBranches: () => ({ data: [], isPending: false }),
}));

vi.mock("@/api/lookups", () => ({
  useItems: () => ({ data: [], isPending: false }),
}));

import { api } from "@/api/axios";
import RequisitionDetailPage from "./RequisitionDetailPage";

function wrap(ui: ReactNode, path = "/requisitions/1") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/requisitions/:id" element={ui} />
          <Route path="/requisitions/:id/costing" element={<div>Costing Page</div>} />
          <Route path="/requisitions/:id/customer-confirm" element={<div>Customer Confirm Page</div>} />
          <Route path="/approvals/:id/margin" element={<div>Margin Page</div>} />
          <Route path="/approvals/:id/final" element={<div>Final Sign Page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

function makeReq(overrides: Partial<V3Requisition> = {}): V3Requisition {
  return {
    id: 1,
    refNo: "REQ-0001",
    status: "Draft",
    currencyCode: "AED",
    notes: null,
    customer: { id: 3, name: "ACME", code: "CUST-0003" },
    salesPerson: { id: 10, name: "Ali" },
    finishedGoods: [
      {
        id: 50,
        expectedQty: 5000,
        hasPrinting: false,
        item: { id: 87, code: "FG-0087", description: "Test FG" },
        bomLines: [
          {
            id: 100,
            qtyPerKg: 0.44,
            micron: "20",
            item: { id: 12, code: "RM-0012", description: "BOPP" },
          },
        ],
        costs: null,
      },
    ],
    ...overrides,
  };
}

function setUser(role: string, userId = 10) {
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role: role as never,
    userId,
    name: "Tester",
    branchId: 1,
    mustChangePassword: false,
  });
}

function mockReqGet(req: V3Requisition) {
  vi.mocked(api.get).mockImplementation((url: string) => {
    if (url.includes("customer-history")) return Promise.resolve({ data: [] });
    if (url.includes("branch-history")) return Promise.resolve({ data: [] });
    return Promise.resolve({ data: req });
  });
}

describe("RequisitionDetailPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    setUser("SalesPerson", 10);
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("renders the V3 header with status badge and customer name", async () => {
    mockReqGet(makeReq());
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByText("ACME")).toBeInTheDocument();
    // V3StatusBadge renders the status text
    expect(screen.getByText("Draft")).toBeInTheDocument();
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

  it("renders Submit + Cancel buttons when status=Draft for owning SalesPerson", async () => {
    setUser("SalesPerson", 10); // matches salesPerson.id
    mockReqGet(makeReq({ status: "Draft" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /^submit$/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^cancel$/i })).toBeInTheDocument();
  });

  it("hides Submit/Cancel buttons when SalesPerson does not own the req", async () => {
    setUser("SalesPerson", 999); // not the salesPerson.id (10)
    mockReqGet(makeReq({ status: "Draft" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /^submit$/i })).not.toBeInTheDocument();
  });

  it("renders Edit costing button when status=Costing and role=Accountant", async () => {
    setUser("Accountant", 11);
    mockReqGet(makeReq({ status: "Costing" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    const btn = screen.getByRole("button", { name: /edit costing/i });
    expect(btn).toBeInTheDocument();
    await userEvent.click(btn);
    await waitFor(() =>
      expect(screen.getByText("Costing Page")).toBeInTheDocument(),
    );
  });

  it("renders Set Margin button when status=MdPricing and role=ManagingDirector", async () => {
    setUser("ManagingDirector", 50);
    mockReqGet(makeReq({ status: "MdPricing" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    const btn = screen.getByRole("button", { name: /set margin/i });
    expect(btn).toBeInTheDocument();
    await userEvent.click(btn);
    await waitFor(() =>
      expect(screen.getByText("Margin Page")).toBeInTheDocument(),
    );
  });

  it("renders Confirm with Customer link when status=CustomerConfirm and SalesPerson owns req", async () => {
    setUser("SalesPerson", 10);
    mockReqGet(makeReq({ status: "CustomerConfirm" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    const btn = screen.getByRole("button", { name: /confirm with customer/i });
    expect(btn).toBeInTheDocument();
    await userEvent.click(btn);
    await waitFor(() =>
      expect(screen.getByText("Customer Confirm Page")).toBeInTheDocument(),
    );
  });

  it("renders Sign Final button when status=MdFinalSign and role=ManagingDirector", async () => {
    setUser("ManagingDirector", 50);
    mockReqGet(makeReq({ status: "MdFinalSign" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    const btn = screen.getByRole("button", { name: /sign final/i });
    expect(btn).toBeInTheDocument();
    await userEvent.click(btn);
    await waitFor(() =>
      expect(screen.getByText("Final Sign Page")).toBeInTheDocument(),
    );
  });

  it("renders Download PDF button when status=Signed", async () => {
    setUser("SalesPerson", 10);
    mockReqGet(makeReq({ status: "Signed" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    // Two Download PDF buttons exist on Signed: the top status-aware action
    // and one inside the SignedQuotationViewer card. Either is sufficient
    // for this assertion.
    const buttons = screen.getAllByRole("button", { name: /download pdf/i });
    expect(buttons.length).toBeGreaterThanOrEqual(1);
  });

  it("renders no action buttons when status=Cancelled", async () => {
    setUser("SalesPerson", 10);
    mockReqGet(makeReq({ status: "Cancelled" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /^submit$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /set margin/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /sign final/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /download pdf/i })).not.toBeInTheDocument();
  });

  it("renders 'Edited by accountant' badge when bomLine.lastModifiedByUserId is set", async () => {
    setUser("SalesPerson", 10);
    mockReqGet(
      makeReq({
        status: "MdPricing",
        finishedGoods: [
          {
            id: 50,
            expectedQty: 5000,
            hasPrinting: false,
            item: { id: 87, code: "FG-0087", description: "Test FG" },
            bomLines: [
              {
                id: 100,
                qtyPerKg: 0.44,
                micron: "20",
                item: { id: 12, code: "RM-0012", description: "BOPP" },
                lastModifiedByUserId: 5,
                lastModifiedAt: "2026-04-29T10:00:00Z",
              },
            ],
            costs: null,
          },
        ],
      }),
    );
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByText(/edited by accountant/i)).toBeInTheDocument();
  });

  it("renders Admin can see action buttons regardless of ownership (Costing -> Edit costing)", async () => {
    setUser("Admin", 999);
    mockReqGet(makeReq({ status: "Costing" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /edit costing/i })).toBeInTheDocument();
  });

  it("Cancel input rejects reason shorter than 5 chars", async () => {
    setUser("SalesPerson", 10);
    mockReqGet(makeReq({ status: "Draft" }));
    render(wrap(<RequisitionDetailPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    await userEvent.click(screen.getByRole("button", { name: /^cancel$/i }));
    const input = screen.getByLabelText(/cancel reason/i);
    await userEvent.type(input, "no");
    await userEvent.click(screen.getByRole("button", { name: /confirm cancel/i }));
    // Cancel input still visible (no API call) — toast surfaced via sonner.
    expect(screen.getByRole("button", { name: /confirm cancel/i })).toBeInTheDocument();
  });

  // V2.3 customer-history + branch-swap modals removed in Task 20 (V3 = Alain only,
  // customer immutable post-Create). Tests for those triggers deleted with the modals.
});
