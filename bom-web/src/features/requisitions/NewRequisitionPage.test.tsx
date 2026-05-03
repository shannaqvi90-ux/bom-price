import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { NewRequisitionPage } from "./NewRequisitionPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: { get: vi.fn(), post: vi.fn(), patch: vi.fn() },
  API_BASE_URL: "/api",
}));

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <NewRequisitionPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("NewRequisitionPage (V3)", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
  });

  it("loads customers and currency picker, renders empty state", async () => {
    vi.mocked(api.get).mockImplementation((url) => {
      if (url === "/customers")
        return Promise.resolve({
          data: [
            {
              id: 1,
              code: "CUST-0001",
              name: "Acme",
              isDeleted: false,
              address: "",
              email: "",
              phoneNumber: "",
              salesPersonId: null,
              salesPersonName: null,
              createdByUserId: 1,
            },
          ],
        });
      return Promise.resolve({ data: [] });
    });
    renderPage();
    await waitFor(() =>
      expect(screen.getByText(/new requisition/i)).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/customer/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/currency/i)).toBeInTheDocument();
    expect(screen.getByText(/no finished goods added/i)).toBeInTheDocument();
  });

  it("submits a V3 payload with finishedGoods array", async () => {
    vi.mocked(api.get).mockImplementation((url) => {
      if (url === "/customers")
        return Promise.resolve({
          data: [
            {
              id: 1,
              code: "CUST-0001",
              name: "Acme",
              isDeleted: false,
              address: "",
              email: "",
              phoneNumber: "",
              salesPersonId: null,
              salesPersonName: null,
              createdByUserId: 1,
            },
          ],
        });
      if (url === "/customers/1/items")
        return Promise.resolve({
          data: [{ id: 87, code: "FG-0087", description: "Test FG" }],
        });
      // useActiveCurrencies() — currency dropdown is now derived from /exchange-rates
      if (url === "/exchange-rates")
        return Promise.resolve({
          data: [
            {
              id: 1,
              currencyCode: "USD",
              currencyName: "US Dollar",
              rateToAed: 3.6725,
              effectiveDate: "2026-01-01T00:00:00Z",
              isActive: true,
              setByName: "Test",
            },
          ],
        });
      // useItems({type:"FinishedGood"}) and useItems({type:"RawMaterial"}) — match by query string
      if (typeof url === "string" && url.startsWith("/items"))
        return Promise.resolve({
          data: [
            {
              id: 87,
              code: "FG-0087",
              description: "Test FG",
              type: "FinishedGood",
              branchId: 2,
              isActive: true,
              lastPurchasePrice: null,
            },
            {
              id: 12,
              code: "RM-0012",
              description: "BOPP",
              type: "RawMaterial",
              branchId: 2,
              isActive: true,
              lastPurchasePrice: null,
            },
          ],
        });
      return Promise.resolve({ data: [] });
    });
    vi.mocked(api.post).mockImplementation((url) => {
      if (url === "/requisitions")
        return Promise.resolve({
          data: { id: 100, refNo: "REQ-0100", status: "Draft" },
        });
      if (url === "/requisitions/100/submit")
        return Promise.resolve({ data: { id: 100, status: "Costing" } });
      return Promise.reject(new Error(`unexpected POST ${url}`));
    });

    renderPage();

    // Wait for the customer option (id=1) to render after the async query resolves.
    await screen.findByRole("option", { name: /CUST-0001/i });
    await userEvent.selectOptions(screen.getByLabelText(/customer/i), "1");
    await userEvent.selectOptions(screen.getByLabelText(/currency/i), "USD");
    await userEvent.click(
      screen.getByRole("button", { name: /add finished good/i }),
    );
    await userEvent.selectOptions(screen.getByLabelText(/fg item/i), "87");
    await userEvent.type(screen.getByLabelText(/quantity/i), "5000");

    // Add a BOM line so validation passes
    await userEvent.click(
      screen.getByRole("button", { name: /add raw material/i }),
    );
    // BomEditorTable's first item dropdown
    const itemDropdowns = screen.getAllByLabelText(/item-0/i);
    await userEvent.selectOptions(itemDropdowns[0], "12");
    const qtyInputs = screen.getAllByLabelText(/qty-0/i);
    await userEvent.type(qtyInputs[0], "0.44");

    await userEvent.click(screen.getByRole("button", { name: /^submit$/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith(
        "/requisitions",
        expect.objectContaining({
          customerId: 1,
          quotationCurrency: "USD",
          finishedGoods: expect.arrayContaining([
            expect.objectContaining({
              itemId: 87,
              expectedQtyKg: 5000,
              bomLines: expect.arrayContaining([
                expect.objectContaining({ itemId: 12, qtyPerKg: 0.44 }),
              ]),
            }),
          ]),
        }),
      ),
    );
  });
});
