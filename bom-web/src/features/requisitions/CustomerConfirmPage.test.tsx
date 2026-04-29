import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { CustomerConfirmPage } from "./CustomerConfirmPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

function renderAt(path: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/requisitions/:id/customer-confirm" element={<CustomerConfirmPage />} />
          <Route path="/requisitions/:id" element={<div>req-detail</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("CustomerConfirmPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
  });

  it("renders MD-priced quotation + accept/reject buttons", async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: {
        id: 100,
        refNo: "REQ-0100",
        status: "CustomerConfirm",
        currencyCode: "USD",
        notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [
          {
            id: 50,
            expectedQty: 5000,
            hasPrinting: false,
            item: { id: 87, code: "FG-0087", description: "Test FG" },
            bomLines: [],
            costs: null,
          },
        ],
      },
    });

    renderAt("/requisitions/100/customer-confirm");
    await waitFor(() => expect(screen.getByText("REQ-0100")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /customer accepted/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /request md to re-price/i })).toBeInTheDocument();
  });

  it("calls accept-customer endpoint on Accept click", async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: {
        id: 100,
        refNo: "REQ-0100",
        status: "CustomerConfirm",
        currencyCode: "USD",
        notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [],
      },
    });
    vi.mocked(api.post).mockResolvedValue({ data: { id: 100, status: "MdFinalSign" } });

    renderAt("/requisitions/100/customer-confirm");
    await screen.findByRole("button", { name: /customer accepted/i });
    await userEvent.click(screen.getByRole("button", { name: /customer accepted/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith("/approvals/100/accept-customer", expect.any(Object)),
    );
  });
});
