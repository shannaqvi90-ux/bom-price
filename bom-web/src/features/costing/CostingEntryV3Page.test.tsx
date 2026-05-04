import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import CostingEntryV3Page from "./CostingEntryV3Page";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), put: vi.fn(), post: vi.fn() } }));

function renderPage(path = "/requisitions/1/costing") {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/requisitions/:id/costing" element={<CostingEntryV3Page />} />
          <Route path="/requisitions/:id" element={<div>Detail Page</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

function makeReqFixture(statusOverride: string) {
  return {
    id: 1,
    refNo: "REQ-0001",
    status: statusOverride,
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
  };
}

describe("CostingEntryV3Page", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.put).mockReset();
    vi.mocked(api.post).mockReset();
  });

  function mockApiGet(statusOverride: string) {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url.includes("exchange-rates")) {
        return Promise.resolve({ data: [] });
      }
      return Promise.resolve({ data: makeReqFixture(statusOverride) });
    });
  }

  it("renders the costing form when status is Costing", async () => {
    mockApiGet("Costing");
    renderPage();
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /save/i })).toBeEnabled();
    expect(screen.getByRole("button", { name: /submit to md/i })).toBeInTheDocument();
  });

  it("shows a status-mismatch message when status is not Costing or MdPricing", async () => {
    mockApiGet("Draft");
    renderPage();
    await waitFor(() =>
      expect(
        screen.getByText((_, el) =>
          el?.tagName === "P" && /not in.*costing.*state/i.test(el.textContent ?? ""),
        ),
      ).toBeInTheDocument(),
    );
    expect(screen.queryByRole("button", { name: /save/i })).not.toBeInTheDocument();
  });

  it("shows amber banner and keeps form editable when status is MdPricing", async () => {
    mockApiGet("MdPricing");
    renderPage();

    await waitFor(() => {
      expect(screen.getByText(/MD pricing pending/i)).toBeInTheDocument();
    });

    // Save button should be enabled (not disabled by status gate)
    const saveBtn = screen.getByRole("button", { name: /save/i });
    expect(saveBtn).toBeEnabled();

    // Submit button must be HIDDEN at MdPricing — req has already been submitted;
    // calling /submit again would 400 with "Cannot submit costing from MdPricing".
    expect(screen.queryByRole("button", { name: /submit to md/i })).not.toBeInTheDocument();
  });
});
