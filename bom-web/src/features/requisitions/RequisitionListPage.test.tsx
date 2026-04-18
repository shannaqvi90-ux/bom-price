import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import type { RequisitionListItem } from "@/types/api";
import { useAuthStore } from "@/store/authStore";

const mockNavigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock("@/api/axios", () => ({ api: { get: vi.fn() } }));

import { api } from "@/api/axios";
import RequisitionListPage from "./RequisitionListPage";

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

const sampleRows: RequisitionListItem[] = [
  {
    id: 1,
    refNo: "REQ-0001",
    status: "BomPending",
    itemCount: 1,
    customerName: "ACME",
    currencyCode: "AED",
    branchName: "Fujairah",
    salesPersonName: "Ali",
    createdAt: new Date().toISOString(),
  },
  {
    id: 2,
    refNo: "REQ-0002",
    status: "Approved",
    itemCount: 1,
    customerName: "BetaCorp",
    currencyCode: "USD",
    branchName: "Fujairah",
    salesPersonName: "Ali",
    createdAt: new Date().toISOString(),
  },
];

describe("RequisitionListPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    mockNavigate.mockReset();
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "SalesPerson",
      userId: 10,
      name: "Ali",
      branchId: 1,
    });
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("shows a loading state and then the rows", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRows });
    render(wrap(<RequisitionListPage />));
    expect(screen.getAllByTestId("data-table-skeleton-row").length).toBeGreaterThan(0);
    await waitFor(() =>
      expect(screen.getByText("REQ-0001")).toBeInTheDocument(),
    );
    expect(screen.getByText("REQ-0002")).toBeInTheDocument();
  });

  it('renders the "New Requisition" button only for SalesPerson', async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    const { unmount } = render(wrap(<RequisitionListPage />));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /new requisition/i })).toBeInTheDocument(),
    );
    unmount();

    // Switch role → button disappears
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "BomCreator",
      userId: 11,
      name: "Bob",
      branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    render(wrap(<RequisitionListPage />));
    await waitFor(() => expect(screen.queryByText(/loading/i)).not.toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /new requisition/i })).not.toBeInTheDocument();
  });

  it("navigates to the detail page when a row is clicked", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRows });
    render(wrap(<RequisitionListPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    fireEvent.click(screen.getByText("REQ-0001"));
    expect(mockNavigate).toHaveBeenCalledWith("/requisitions/1");
  });

  it("filters rows by status", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRows });
    render(wrap(<RequisitionListPage />));
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    const statusFilter = screen.getByLabelText(/status/i);
    fireEvent.change(statusFilter, { target: { value: "Approved" } });

    expect(screen.queryByText("REQ-0001")).not.toBeInTheDocument();
    expect(screen.getByText("REQ-0002")).toBeInTheDocument();
  });

  it('shows a "Create your first requisition" empty state for a SalesPerson with no data', async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    render(wrap(<RequisitionListPage />));
    await waitFor(() =>
      expect(screen.getByText(/create your first requisition/i)).toBeInTheDocument(),
    );
  });
});
