import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import CostingEntryPage from "./CostingEntryPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
  },
}));

const mockedApi = api as unknown as {
  get: ReturnType<typeof vi.fn>;
  post: ReturnType<typeof vi.fn>;
  put: ReturnType<typeof vi.fn>;
};

function renderPage(requisitionId = 5) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/requisitions/${requisitionId}/costing`]}>
        <Routes>
          <Route path="/requisitions/:id/costing" element={<CostingEntryPage />} />
          <Route path="/requisitions/:id" element={<div>Requisition Detail</div>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

afterEach(() => {
  vi.useRealTimers();
});

const baseRequisition = {
  id: 5,
  refNo: "REQ-0005",
  status: "CostingInProgress",
  customerName: "Fujairah Pipes LLC",
  currencyCode: "AED",
  customerId: 1,
  customerEmail: "",
  customerPhone: "",
  customerAddress: "",
  exchangeRateSnapshot: null,
  branchId: 1,
  branchName: "Fujairah",
  salesPersonId: 1,
  salesPersonName: "Ali",
  createdAt: "2026-04-14T00:00:00Z",
  updatedAt: "2026-04-14T00:00:00Z",
  items: [
    { id: 1, itemId: 1, itemDescription: "PP Pipe 110mm", expectedQty: 100, sortOrder: 1 },
  ],
  approval: null,
};

const baseBomLine = {
  bomLineId: 100,
  processId: 1,
  processName: "Extrusion",
  rawMaterialItemId: 10,
  rawMaterialDescription: "HDPE Granules",
  qtyPerKg: 0.85,
  wastagePct: 2.0,
};

function makeCostingReview(bomLineOverrides: Record<string, unknown> = {}, draftOverride: unknown = null) {
  return {
    requisitionId: 5,
    items: [
      {
        requisitionItemId: 1,
        itemId: 1,
        itemDescription: "PP Pipe 110mm",
        expectedQty: 100,
        bomHeaderId: 10,
        // Use a non-"NotStarted" / non-"Submitted" value so the cost form renders
        // AND canEditItem is true (costStatus !== "Submitted" && !isReadOnly)
        costStatus: "NotStarted",
        cost: null,
        bomLines: [{ ...baseBomLine, lastCost: null, ...bomLineOverrides }],
        draft: draftOverride,
      },
    ],
  };
}

function defaultGetHandler(costingReview: unknown, requisition = baseRequisition) {
  mockedApi.get.mockImplementation((url: string) => {
    if (url.startsWith("/costing/")) return Promise.resolve({ data: costingReview });
    if (url.startsWith("/requisitions/")) return Promise.resolve({ data: requisition });
    if (url.startsWith("/exchange-rates")) return Promise.resolve({ data: [
      { id: 1, currencyCode: "USD", currencyName: "US Dollar", rateToAed: 3.6725, effectiveDate: "", isActive: true, setByName: "" },
    ] });
    return Promise.reject(new Error(`Unexpected GET ${url}`));
  });
}

describe("CostingEntryPage", () => {
  it("pre-fills cost from last cost when no draft", async () => {
    const lastCost = { costPerKg: 1.25, currencyCode: "USD", updatedAt: new Date().toISOString() };
    const costing = makeCostingReview({ lastCost });
    // The page shows "Start Costing" when costStatus="NotStarted" && !isReadOnly.
    // To get past that gate, use costStatus !== "NotStarted".
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    renderPage();
    const costInput = await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    expect((costInput as HTMLInputElement).value).toBe("1.25");
  });

  it("prefers draft values over last cost", async () => {
    const lastCost = { costPerKg: 1.25, currencyCode: "USD", updatedAt: new Date().toISOString() };
    const costing = makeCostingReview(
      { lastCost },
      {
        lines: [{ bomLineId: 100, costPerKg: 9.99, currencyCode: "AED" }],
        landedCostType: "Percentage",
        landedCostValue: 5,
        fohAmount: 0.12,
      },
    );
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    renderPage();
    const costInput = await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    expect((costInput as HTMLInputElement).value).toBe("9.99");
  });

  it("shows stale warning when lastCost is older than 10 days", async () => {
    const old = new Date();
    old.setDate(old.getDate() - 14);
    const costing = makeCostingReview({
      lastCost: { costPerKg: 0.8, currencyCode: "USD", updatedAt: old.toISOString() },
    });
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    renderPage();
    // The new CostingEntryPage shows "! " prefix and "X days ago" for stale items
    expect(await screen.findByText(/! /)).toBeInTheDocument();
    expect(screen.getByText(/14 days ago/)).toBeInTheDocument();
  });

  it("does not show stale warning when lastCost is 3 days old", async () => {
    const recent = new Date();
    recent.setDate(recent.getDate() - 3);
    const costing = makeCostingReview({
      lastCost: { costPerKg: 0.8, currencyCode: "USD", updatedAt: recent.toISOString() },
    });
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    renderPage();
    await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    expect(screen.queryByText(/! /)).toBeNull();
  });

  it("disables submit when any cost is 0", async () => {
    const costing = makeCostingReview({ lastCost: null });
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    renderPage();
    const btn = await screen.findByRole("button", { name: /Submit Costing/i });
    expect(btn).toBeDisabled();
  });

  it("auto-saves draft after typing a cost", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const costing = makeCostingReview({ lastCost: null });
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    mockedApi.put.mockResolvedValue({ status: 204 });
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

    renderPage();
    const costInput = await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    await user.clear(costInput);
    await user.type(costInput, "1.5");
    vi.advanceTimersByTime(900);

    await waitFor(() => {
      expect(mockedApi.put).toHaveBeenCalledWith(
        "/costing/5/items/1/draft",
        expect.objectContaining({
          lines: [expect.objectContaining({ costPerKg: 1.5 })],
        }),
      );
    });
    vi.useRealTimers();
  });

  it("navigates to detail page after successful submit", async () => {
    const costing = makeCostingReview({
      lastCost: { costPerKg: 1.25, currencyCode: "AED", updatedAt: new Date().toISOString() },
    });
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    mockedApi.post.mockResolvedValue({ status: 204 });
    const user = userEvent.setup();
    renderPage();
    await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    const btn = screen.getByRole("button", { name: /Submit Costing/i });
    await user.click(btn);
    expect(await screen.findByText("Requisition Detail")).toBeInTheDocument();
  });

  it("shows inline message when submit fails with missing exchange rate", async () => {
    const costing = makeCostingReview({
      lastCost: { costPerKg: 5, currencyCode: "SAR", updatedAt: new Date().toISOString() },
    });
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    mockedApi.post.mockRejectedValue({
      response: { status: 400, data: { message: "No exchange rate found for SAR. Contact admin." } },
    });
    const user = userEvent.setup();
    renderPage();
    await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    const btn = screen.getByRole("button", { name: /Submit Costing/i });
    await user.click(btn);
    expect(await screen.findByText(/No exchange rate found for SAR/i)).toBeInTheDocument();
  });

  it("enables submit when all costs are greater than 0", async () => {
    const costing = makeCostingReview({
      lastCost: { costPerKg: 1.25, currencyCode: "AED", updatedAt: new Date().toISOString() },
    });
    costing.items[0].costStatus = "InProgress" as never;
    defaultGetHandler(costing);
    renderPage();
    // Wait for lines to be hydrated (cost input appears with pre-filled value)
    await screen.findByLabelText(/Cost per kg for HDPE Granules/i);
    const btn = screen.getByRole("button", { name: /Submit Costing/i });
    expect(btn).not.toBeDisabled();
  });
});
