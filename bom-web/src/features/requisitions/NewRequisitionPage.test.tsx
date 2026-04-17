import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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

vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));

import { notify } from "@/lib/notify";

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
    vi.mocked(notify.fromApiError).mockReset();
    vi.mocked(notify.success).mockReset();
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
    await waitFor(() => expect(screen.getByLabelText(/customer/i)).toBeInTheDocument());
    expect(screen.getByPlaceholderText(/qty/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/currency/i)).toBeInTheDocument();
  });

  it("blocks submit and surfaces validation errors when fields are missing", async () => {
    mockLookups();
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/customer/i)).toBeInTheDocument());

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
    await waitFor(() => expect(screen.getByLabelText(/customer/i)).toBeInTheDocument());

    // Pick customer via SearchableSelect
    const customerBox = screen.getByLabelText(/customer/i);
    fireEvent.focus(customerBox);
    fireEvent.mouseDown(screen.getByText("ACME"));

    // Pick item — use mouseDown not click
    const itemBox = screen.getByPlaceholderText(/search items/i);
    fireEvent.focus(itemBox);
    fireEvent.mouseDown(screen.getByText("HDPE Pipe 20mm"));

    // Qty
    fireEvent.change(screen.getByPlaceholderText(/qty/i), {
      target: { value: "100" },
    });

    fireEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/requisitions", {
        customerId: 1,
        items: [{ itemId: 2, expectedQty: 100 }],
        currencyCode: "AED",
      }),
    );
    await waitFor(() =>
      expect(mockNavigate).toHaveBeenCalledWith("/requisitions/42", { replace: true }),
    );
  });

  it("calls notify.fromApiError when submission fails", async () => {
    mockLookups();
    const err = { response: { data: { message: "Boom" } } };
    vi.mocked(api.post).mockRejectedValueOnce(err);
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/customer/i)).toBeInTheDocument());

    fireEvent.focus(screen.getByLabelText(/customer/i));
    fireEvent.mouseDown(screen.getByText("ACME"));
    const itemBox = screen.getByPlaceholderText(/search items/i);
    fireEvent.focus(itemBox);
    fireEvent.mouseDown(screen.getByText("HDPE Pipe 20mm"));
    fireEvent.change(screen.getByPlaceholderText(/qty/i), { target: { value: "10" } });

    fireEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() =>
      expect(vi.mocked(notify.fromApiError)).toHaveBeenCalledWith(err, "Failed to create requisition"),
    );
  });

  it("highlights the offending row when the server rejects a field", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes("/customers")) return Promise.resolve({ data: [{ id: 1, name: "ACME" }] });
      if (url.includes("/items"))
        return Promise.resolve({
          data: [{ id: 10, code: "A", description: "Item A", type: "FinishedGood", isActive: true }],
        });
      if (url.includes("/exchange-rates/active")) return Promise.resolve({ data: [] });
      return Promise.resolve({ data: [] });
    });

    vi.mocked(api.post).mockRejectedValueOnce({
      response: {
        data: {
          detail: "ExpectedQty must be greater than 0.",
          errors: { "Items[0].ExpectedQty": ["Must be greater than 0."] },
        },
      },
    });

    const user = userEvent.setup();
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByLabelText(/customer/i)).toBeInTheDocument());

    // Select a customer
    const customerBox = screen.getByLabelText(/customer/i);
    fireEvent.focus(customerBox);
    fireEvent.mouseDown(screen.getByText("ACME"));

    // Select Item A
    const itemBox = screen.getAllByPlaceholderText(/search items/i)[0];
    fireEvent.focus(itemBox);
    fireEvent.mouseDown(screen.getByText("Item A"));

    // Enter valid qty (client passes, server rejects)
    fireEvent.change(screen.getAllByPlaceholderText(/qty/i)[0], { target: { value: "5" } });
    fireEvent.click(screen.getByRole("button", { name: /^Create$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Must be greater than 0\./i)).toBeInTheDocument(),
    );
  });

  it("excludes already-selected items from the second row's picker", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes("/customers")) return Promise.resolve({ data: [{ id: 1, name: "ACME" }] });
      if (url.includes("/items"))
        return Promise.resolve({
          data: [
            { id: 10, code: "A", description: "Item A", type: "FinishedGood", isActive: true },
            { id: 20, code: "B", description: "Item B", type: "FinishedGood", isActive: true },
          ],
        });
      if (url.includes("/exchange-rates/active")) return Promise.resolve({ data: [] });
      return Promise.resolve({ data: [] });
    });

    const user = userEvent.setup();
    render(wrap(<NewRequisitionPage />));
    await waitFor(() => expect(screen.getByRole("button", { name: /Add Item/i })).toBeInTheDocument());

    // Add a second row
    await user.click(screen.getByRole("button", { name: /Add Item/i }));

    // Select Item A in row 0
    const pickers = screen.getAllByPlaceholderText(/Search items/i);
    await user.click(pickers[0]);
    await user.click(screen.getByText("Item A"));

    // Open row 1's picker — Item A should no longer appear inside its dropdown list
    await user.click(pickers[1]);
    // Row 1's dropdown should only show Item B (not Item A which is already selected in row 0)
    const dropdownItems = screen.queryAllByRole("listitem");
    const dropdownTexts = dropdownItems.map((el) => el.textContent);
    expect(dropdownTexts).not.toContain("Item A");
    expect(dropdownTexts).toContain("Item B");
  });
});
