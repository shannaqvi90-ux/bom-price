import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import type { ReactNode } from "react";
import type { RequisitionDetail, Item } from "@/types/api";
import { useAuthStore } from "@/store/authStore";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

import { api } from "@/api/axios";
import EditRequisitionPage from "./EditRequisitionPage";

function wrap(ui: ReactNode, path = "/requisitions/1/edit") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/requisitions/:id/edit" element={ui} />
          <Route path="/requisitions/:id" element={<div>detail page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

const sampleRejected: RequisitionDetail = {
  id: 1, refNo: "REQ-0001", status: "Rejected",
  customerId: 3, customerName: "ACME",
  customerEmail: "s@a.test", customerPhone: "+971", customerAddress: "FZ",
  currencyCode: "AED", exchangeRateSnapshot: null,
  branchId: 1, branchName: "Fujairah",
  salesPersonId: 10, salesPersonName: "Ali",
  createdAt: "2026-04-14T10:00:00Z", updatedAt: "2026-04-14T11:00:00Z",
  items: [
    { id: 1, itemId: 2, itemDescription: "HDPE Pipe 20mm", expectedQty: 100, sortOrder: 1 },
  ],
  approval: { isApproved: false, notes: "Margin too low", approvedAt: "2026-04-15T12:00:00Z" },
};

const sampleItems: Item[] = [
  { id: 2, code: "FG-1", description: "HDPE Pipe 20mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
  { id: 3, code: "FG-2", description: "HDPE Pipe 32mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
];

function mockLookups() {
  vi.mocked(api.get).mockImplementation((url: string) => {
    if (url.includes("/requisitions/1")) return Promise.resolve({ data: sampleRejected });
    if (url.includes("/items")) return Promise.resolve({ data: sampleItems });
    return Promise.reject(new Error(`unmocked: ${url}`));
  });
}

describe("EditRequisitionPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "SalesPerson", userId: 10, name: "Ali", branchId: 1, mustChangePassword: false,
    });
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("renders the previous rejection reason banner", async () => {
    mockLookups();
    render(wrap(<EditRequisitionPage />));
    await waitFor(() => expect(screen.getByText(/Previous rejection reason/i)).toBeInTheDocument());
    expect(screen.getByText("Margin too low")).toBeInTheDocument();
  });

  it("shows a 'Cannot edit' message when status is not Rejected", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes("/requisitions/1")) return Promise.resolve({ data: { ...sampleRejected, status: "BomPending" } });
      if (url.includes("/items")) return Promise.resolve({ data: sampleItems });
      return Promise.reject(new Error(`unmocked: ${url}`));
    });
    render(wrap(<EditRequisitionPage />));
    await waitFor(() => expect(screen.getByText(/Cannot edit/i)).toBeInTheDocument());
    expect(screen.getByText(/BomPending/)).toBeInTheDocument();
  });

  it("blocks non-owning SalesPerson", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "SalesPerson", userId: 99, name: "Other", branchId: 1, mustChangePassword: false,
    });
    mockLookups();
    render(wrap(<EditRequisitionPage />));
    await waitFor(() =>
      expect(screen.getByText(/Only the owning sales person/i)).toBeInTheDocument(),
    );
  });

  it("submits resubmit and navigates to detail on success", async () => {
    mockLookups();
    vi.mocked(api.post).mockResolvedValueOnce({
      data: { id: 1, refNo: "REQ-0001", status: "BomPending" },
    });
    render(wrap(<EditRequisitionPage />));
    await waitFor(() => expect(screen.getByDisplayValue("HDPE Pipe 20mm")).toBeInTheDocument());

    await userEvent.click(screen.getByRole("button", { name: /resubmit for bom/i }));

    await waitFor(() => expect(api.post).toHaveBeenCalledWith(
      "/requisitions/1/resubmit",
      expect.objectContaining({
        items: expect.arrayContaining([
          expect.objectContaining({ itemId: 2, expectedQty: 100 }),
        ]),
      }),
    ));
    await waitFor(() => expect(screen.getByText("detail page")).toBeInTheDocument());
  });
});
