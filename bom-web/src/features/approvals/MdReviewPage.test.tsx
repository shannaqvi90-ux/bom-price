import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import MdReviewPage from "./MdReviewPage";
import { api } from "@/api/axios";
import { notify } from "@/lib/notify";

vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));

vi.mock("@/api/axios", () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
  },
}));

const mockedApi = api as unknown as {
  get: ReturnType<typeof vi.fn>;
  post: ReturnType<typeof vi.fn>;
};

const baseReview = {
  refNo: "REQ-0042",
  customerName: "ACME",
  currencyCode: "USD",
  exchangeRate: 3.672,
  readyForReview: true,
  items: [
    {
      requisitionItemId: 1,
      itemDescription: "HDPE Pipe 20mm",
      expectedQty: 5000,
      costStatus: "Submitted",
      cost: {
        rawMaterialCostPerKg: 2.45,
        landedCostPerKg: 0.32,
        fohPerKg: 0.18,
        totalCostPerKg: 2.95,
        materialCostPct: 83.05,
        landedCostPct: 10.85,
        fohPct: 6.1,
      },
    },
  ],
};

function renderPage(requisitionId = 42) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[`/requisitions/${requisitionId}/approval`]}>
        <Routes>
          <Route path="/requisitions/:id/approval" element={<MdReviewPage />} />
          <Route
            path="/requisitions/:id"
            element={<div>Requisition Detail Stub</div>}
          />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
});

describe("MdReviewPage", () => {
  it("renders cost breakdown rows from the API response", async () => {
    mockedApi.get
      .mockResolvedValueOnce({ data: baseReview })
      .mockResolvedValueOnce({ data: null });

    renderPage();

    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    expect(screen.getByText(/HDPE Pipe 20mm/)).toBeInTheDocument();
    expect(screen.getByText(/ACME/)).toBeInTheDocument();
    expect(screen.getByText(/2\.4500 \/kg/)).toBeInTheDocument();
    expect(screen.getByText(/0\.3200 \/kg/)).toBeInTheDocument();
    expect(screen.getByText(/0\.1800 \/kg/)).toBeInTheDocument();
    expect(screen.getByText(/2\.9500/)).toBeInTheDocument();
  });

  it("live-calculates profit margin as sales price is typed", async () => {
    mockedApi.get
      .mockResolvedValueOnce({ data: baseReview })
      .mockResolvedValueOnce({ data: null });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    // The sales price input has placeholder "0.0000" and no formal label association
    const priceInput = screen.getByPlaceholderText("0.0000");
    await user.type(priceInput, "4.2");

    // margin = ((4.2 - 2.95) / 4.2) * 100 = 29.76%
    expect(screen.getByText(/29\.76%/)).toBeInTheDocument();
  });

  it("approve fires mutation with payload and flips to approved state with Download PDF", async () => {
    mockedApi.get.mockImplementation((url: string) =>
      Promise.resolve({ data: url.includes("/bom/") ? null : baseReview }),
    );
    mockedApi.post.mockResolvedValueOnce({
      data: { message: "Approved", refNo: "REQ-0042" },
    });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    const priceInput = screen.getByPlaceholderText("0.0000");
    await user.type(priceInput, "4.2");

    const notesInput = screen.getByLabelText(/Notes/i);
    await user.type(notesInput, "Looks good");

    const approveButton = screen.getByRole("button", { name: /Approve All/i });
    await user.click(approveButton);

    // Confirmation dialog appears — confirm the approval.
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: /Approve quotation\?/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Approve$/i }));

    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: /Download PDF/i }),
      ).toBeInTheDocument(),
    );

    expect(mockedApi.post).toHaveBeenCalledWith(
      "/approvals/42/approve",
      {
        items: [{ requisitionItemId: 1, salesPricePerKgAed: 4.2 }],
        notes: "Looks good",
      },
    );
    expect(screen.getByText(/Quotation approved/i)).toBeInTheDocument();
  });

  it("renders field error when server rejects Items[0].SalesPricePerKgAed", async () => {
    mockedApi.get.mockImplementation((url: string) =>
      Promise.resolve({ data: url.includes("/bom/") ? null : baseReview }),
    );
    mockedApi.post.mockRejectedValueOnce({
      response: {
        data: {
          detail: "SalesPrice must be greater than 0.",
          errors: { "Items[0].SalesPricePerKgAed": ["Must be greater than 0."] },
        },
      },
    });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    // Enter a valid price so the client-side check passes
    const priceInput = screen.getByPlaceholderText("0.0000");
    await user.type(priceInput, "5");

    const approveButton = screen.getByRole("button", { name: /Approve All/i });
    await user.click(approveButton);

    // Confirm the approval in the dialog — only then the mutation fires.
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: /Approve quotation\?/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Approve$/i }));

    await waitFor(() =>
      expect(screen.getByText("Must be greater than 0.")).toBeInTheDocument(),
    );
  });

  it("reject with empty notes shows validation error and does not fire mutation", async () => {
    mockedApi.get
      .mockResolvedValueOnce({ data: baseReview })
      .mockResolvedValueOnce({ data: null });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    await user.click(screen.getByRole("button", { name: /^reject$/i }));

    expect(notify.error).toHaveBeenCalledWith(
      expect.stringContaining("Notes are required"),
    );
    expect(mockedApi.post).not.toHaveBeenCalled();
  });

  it("reject with notes fires mutation and navigates back to detail", async () => {
    mockedApi.get
      .mockResolvedValueOnce({ data: baseReview })
      .mockResolvedValueOnce({ data: null });
    mockedApi.post.mockResolvedValueOnce({ data: { message: "Rejected" } });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    await user.type(screen.getByLabelText(/Notes/i), "Price too high");
    await user.click(screen.getByRole("button", { name: /^reject$/i }));

    // Confirm the rejection in the dialog.
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: /Reject quotation\?/i })).toBeInTheDocument(),
    );
    const confirmButtons = screen.getAllByRole("button", { name: /^Reject$/i });
    // The second Reject button is the one inside the confirm dialog.
    await user.click(confirmButtons[confirmButtons.length - 1]);

    await waitFor(() =>
      expect(screen.getByText(/Requisition Detail Stub/i)).toBeInTheDocument(),
    );

    expect(mockedApi.post).toHaveBeenCalledWith(
      "/approvals/42/reject",
      { notes: "Price too high" },
    );
  });

  it("shows loading indicator while data is fetching", () => {
    mockedApi.get.mockReturnValueOnce(new Promise(() => {})); // never resolves
    renderPage();
    expect(screen.getByText(/Loading/i)).toBeInTheDocument();
  });

  it("shows a not-found card on 404", async () => {
    mockedApi.get.mockRejectedValueOnce({ response: { status: 404 } });
    renderPage();
    await waitFor(() =>
      expect(
        screen.getByText(/Requisition not found\./i),
      ).toBeInTheDocument(),
    );
  });

  it("View BOM dialog shows cost/kg and contribution columns", async () => {
    const bomData = {
      requisitionId: 42,
      refNo: "REQ-0042",
      requisitionStatus: "MdReview",
      items: [
        {
          requisitionItemId: 1,
          itemId: 1,
          itemDescription: "HDPE Pipe 20mm",
          expectedQty: 5000,
          sortOrder: 1,
          bomHeaderId: 1,
          bomStatus: "Submitted",
          totalCostPerKg: 3.98,
          submittedAt: "2026-04-15T10:00:00Z",
          lines: [
            {
              id: 1,
              processId: 1,
              processName: "Extrusion",
              rawMaterialItemId: 1,
              rawMaterialDescription: "PE100 Resin",
              qtyPerKg: 0.85,
              wastagePct: 2.0,
              costPerKg: 1.25,
              currencyCode: "USD",
              costPerKgInAed: 4.59,
              contributionAed: 3.9765,
            },
          ],
        },
      ],
    };
    mockedApi.get
      .mockResolvedValueOnce({ data: baseReview })  // approval data
      .mockResolvedValueOnce({ data: bomData });     // BOM data

    const user = userEvent.setup();
    renderPage();
    await waitFor(() => expect(screen.getByText("REQ-0042")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /View BOM/i }));

    await waitFor(() =>
      expect(screen.getByText("PE100 Resin")).toBeInTheDocument(),
    );

    // Column headers
    expect(screen.getByText("Contribution")).toBeInTheDocument();

    // Line values: cost/kg with 4dp + currency, contribution with AED suffix
    expect(screen.getByText(/1\.2500 USD/i)).toBeInTheDocument();
    expect(screen.getByText(/3\.9765 AED/i)).toBeInTheDocument();
  });

  it("shows '⚠ Negative margin' badge when price < totalCost but keeps Approve enabled", async () => {
    mockedApi.get.mockImplementation((url: string) => {
      if (url.includes("/approvals/"))
        return Promise.resolve({
          data: {
            refNo: "REQ-0001",
            customerName: "ACME",
            currencyCode: "AED",
            exchangeRate: null,
            readyForReview: true,
            items: [
              {
                requisitionItemId: 1,
                itemDescription: "Widget",
                expectedQty: 100,
                costStatus: "Submitted",
                cost: {
                  rawMaterialCostPerKg: 4,
                  landedCostPerKg: 0.5,
                  fohPerKg: 0.5,
                  totalCostPerKg: 5,
                  materialCostPct: 80,
                  landedCostPct: 10,
                  fohPct: 10,
                },
              },
            ],
          },
        });
      if (url.includes("/bom/"))
        return Promise.resolve({
          data: { refNo: "REQ-0001", requisitionStatus: "MdReview", items: [] },
        });
      return Promise.resolve({ data: [] });
    });

    const user = userEvent.setup();
    renderPage();
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    const priceInput = screen.getByPlaceholderText("0.0000");
    await user.type(priceInput, "1");

    expect(screen.getByText(/Negative margin/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Approve All/i })).toBeEnabled();
  });

  it("does not show the negative-margin badge when price >= totalCost", async () => {
    mockedApi.get.mockImplementation((url: string) => {
      if (url.includes("/approvals/"))
        return Promise.resolve({
          data: {
            refNo: "REQ-0001",
            customerName: "ACME",
            currencyCode: "AED",
            exchangeRate: null,
            readyForReview: true,
            items: [
              {
                requisitionItemId: 1,
                itemDescription: "Widget",
                expectedQty: 100,
                costStatus: "Submitted",
                cost: {
                  rawMaterialCostPerKg: 4,
                  landedCostPerKg: 0.5,
                  fohPerKg: 0.5,
                  totalCostPerKg: 5,
                  materialCostPct: 80,
                  landedCostPct: 10,
                  fohPct: 10,
                },
              },
            ],
          },
        });
      if (url.includes("/bom/"))
        return Promise.resolve({
          data: { refNo: "REQ-0001", requisitionStatus: "MdReview", items: [] },
        });
      return Promise.resolve({ data: [] });
    });

    const user = userEvent.setup();
    renderPage();
    await waitFor(() => expect(screen.getByText("REQ-0001")).toBeInTheDocument());

    const priceInput = screen.getByPlaceholderText("0.0000");
    await user.type(priceInput, "10");

    expect(screen.queryByText(/Negative margin/i)).toBeNull();
  });

  it("shows partial-costing banner and disables Approve when readyForReview is false", async () => {
    mockedApi.get.mockImplementation((url: string) => {
      if (url.includes("/approvals/"))
        return Promise.resolve({
          data: {
            refNo: "REQ-0010",
            customerName: "ACME",
            currencyCode: "AED",
            exchangeRate: null,
            readyForReview: false,
            items: [
              {
                requisitionItemId: 1,
                itemDescription: "Widget A",
                expectedQty: 100,
                costStatus: "Submitted",
                cost: {
                  rawMaterialCostPerKg: 4,
                  landedCostPerKg: 0.5,
                  fohPerKg: 0.5,
                  totalCostPerKg: 5,
                  materialCostPct: 80,
                  landedCostPct: 10,
                  fohPct: 10,
                },
              },
              {
                requisitionItemId: 2,
                itemDescription: "Widget B",
                expectedQty: 200,
                costStatus: "NotStarted",
                cost: null,
              },
            ],
          },
        });
      return Promise.resolve({ data: null });
    });

    const user = userEvent.setup();
    renderPage(10);
    await waitFor(() => expect(screen.getByText("REQ-0010")).toBeInTheDocument());

    // Banner text
    expect(screen.getByRole("alert")).toHaveTextContent(
      /1 item awaiting costing before approval can be done/i,
    );
    expect(screen.getByRole("alert")).toHaveTextContent("Widget B");

    // Approve button is disabled even after entering a price for the costed item
    const priceInput = screen.getByPlaceholderText("0.0000");
    await user.type(priceInput, "7");

    expect(screen.getByRole("button", { name: /Approve All/i })).toBeDisabled();
  });

  it("View BOM dialog shows frozen Cost/kg and Contribution with footer totals", async () => {
    const mockBom = {
      requisitionId: 42,
      refNo: "REQ-0042",
      requisitionStatus: "MdReview",
      items: [
        {
          requisitionItemId: 1,
          itemId: 1,
          itemDescription: "HDPE Pipe 20mm",
          expectedQty: 5000,
          sortOrder: 1,
          bomHeaderId: 1,
          bomStatus: "Submitted",
          lines: [
            {
              id: 101,
              processId: 1,
              processName: "Extrusion",
              rawMaterialItemId: 5,
              rawMaterialDescription: "HDPE Granules",
              qtyPerKg: 0.85,
              wastagePct: 2.0,
              costPerKg: 4.2,
              currencyCode: "USD",
              costPerKgInAed: 15.4224,
              contributionAed: 13.3817,
            },
          ],
          totalCostPerKg: 2.95,
          submittedAt: "2026-04-15T00:00:00Z",
        },
      ],
    };
    mockedApi.get.mockImplementation((url: string) => {
      if (String(url).includes("/approvals/")) return Promise.resolve({ data: baseReview });
      if (String(url).includes("/bom/")) return Promise.resolve({ data: mockBom });
      return Promise.reject(new Error(`Unexpected url: ${url}`));
    });

    const user = userEvent.setup();
    renderPage();
    await waitFor(() => expect(screen.getByText("REQ-0042")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /View BOM/i }));
    await waitFor(() => expect(screen.getByText("HDPE Granules")).toBeInTheDocument());

    expect(screen.getByText("4.2000 USD")).toBeInTheDocument();
    expect(screen.getByText("13.3817 AED")).toBeInTheDocument();
    // Footer: totalCostPerKg from BOM item
    expect(screen.getAllByText(/2\.9500/).length).toBeGreaterThan(0);
  });

  it("shows amber badge when customer change history has entries", async () => {
    const historyEntry = {
      id: 1,
      oldCustomerId: 2,
      oldCustomerName: "Old Corp",
      newCustomerId: 3,
      newCustomerName: "New Corp",
      changedByUserId: 10,
      changedByUserName: "Ali",
      changedAt: "2026-04-20T10:00:00Z",
      reason: null,
    };
    mockedApi.get.mockImplementation((url: string) => {
      if ((url as string).includes("customer-history"))
        return Promise.resolve({ data: [historyEntry] });
      if ((url as string).includes("/bom/"))
        return Promise.resolve({ data: null });
      return Promise.resolve({ data: baseReview });
    });

    renderPage();
    await waitFor(() =>
      expect(screen.getByText(/Customer changed \(1\)/i)).toBeInTheDocument(),
    );
  });
});
