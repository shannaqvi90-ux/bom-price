import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";

const mockNavigate = vi.fn();
vi.mock("react-router-dom", async () => {
  const actual = await vi.importActual<typeof import("react-router-dom")>("react-router-dom");
  return { ...actual, useNavigate: () => mockNavigate };
});

vi.mock("@/api/axios", () => ({
  api: { get: vi.fn(), post: vi.fn() },
}));

import { api } from "@/api/axios";
import NewRequisitionPage from "./NewRequisitionPage";

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

const customers = [{ id: 1, name: "ACME", address: "", email: "", phoneNumber: "", branchId: 1, createdByUserId: 10 }];
const items = [{ id: 2, code: "I-001", description: "HDPE Pipe 20mm", type: "RawMaterial", branchId: 1, isActive: true }];
const rates = [{ id: 3, currencyCode: "USD", currencyName: "US Dollar", rateToAed: 3.67, effectiveDate: "2026-04-01", isActive: true, setByName: "Acc" }];

function mockLookups() {
  vi.mocked(api.get).mockImplementation((url: string) => {
    if (url === "/customers") return Promise.resolve({ data: customers });
    if (url === "/items") return Promise.resolve({ data: items });
    if (url === "/exchange-rates/active") return Promise.resolve({ data: rates });
    return Promise.reject(new Error(`unexpected url ${url}`));
  });
}

describe("NewRequisitionPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
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

  it("populates lookups and renders the form", async () => {
    mockLookups();
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/expected qty/i)).toBeInTheDocument());
    expect(screen.getByLabelText(/customer/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/item/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/currency/i)).toBeInTheDocument();
  });

  it("blocks submit and surfaces validation errors when fields are missing", async () => {
    mockLookups();
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/expected qty/i)).toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: /create/i }));
    await waitFor(() =>
      expect(screen.getByText(/customer is required/i)).toBeInTheDocument(),
    );
    expect(api.post).not.toHaveBeenCalled();
  });

  it("submits and navigates to the detail page on success", async () => {
    mockLookups();
    vi.mocked(api.post).mockResolvedValueOnce({ data: { id: 42, refNo: "REQ-0042" } });
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/expected qty/i)).toBeInTheDocument());

    // Pick customer via SearchableSelect — use mouseDown not click
    const customerBox = screen.getByLabelText(/customer/i);
    fireEvent.focus(customerBox);
    fireEvent.mouseDown(screen.getByText("ACME"));

    // Pick item — use mouseDown not click
    const itemBox = screen.getByLabelText(/item/i);
    fireEvent.focus(itemBox);
    fireEvent.mouseDown(screen.getByText("HDPE Pipe 20mm"));

    // Qty
    fireEvent.change(screen.getByLabelText(/expected qty/i), {
      target: { value: "100" },
    });

    fireEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/requisitions", {
        customerId: 1,
        itemId: 2,
        expectedQty: 100,
        currencyCode: "AED",
      }),
    );
    await waitFor(() =>
      expect(mockNavigate).toHaveBeenCalledWith("/requisitions/42", { replace: true }),
    );
  });

  it("shows a server error message when submission fails", async () => {
    mockLookups();
    vi.mocked(api.post).mockRejectedValueOnce({
      response: { data: { message: "Boom" } },
    });
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/expected qty/i)).toBeInTheDocument());

    fireEvent.focus(screen.getByLabelText(/customer/i));
    fireEvent.mouseDown(screen.getByText("ACME"));
    fireEvent.focus(screen.getByLabelText(/item/i));
    fireEvent.mouseDown(screen.getByText("HDPE Pipe 20mm"));
    fireEvent.change(screen.getByLabelText(/expected qty/i), { target: { value: "10" } });

    fireEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() => expect(screen.getByText(/boom/i)).toBeInTheDocument());
  });
});
