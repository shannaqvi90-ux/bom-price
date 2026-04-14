import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import MdReviewPage from "./MdReviewPage";
import { api } from "@/api/axios";

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
  itemDescription: "HDPE Pipe 20mm",
  customerName: "ACME",
  expectedQty: 5000,
  currencyCode: "USD",
  exchangeRate: 3.672,
  rawMaterialCostPerKg: 2.45,
  landedCostPerKg: 0.32,
  fohPerKg: 0.18,
  totalCostPerKg: 2.95,
  materialCostPct: 83.05,
  landedCostPct: 10.85,
  fohPct: 6.1,
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
    mockedApi.get.mockResolvedValueOnce({ data: baseReview });

    renderPage();

    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    expect(screen.getByText(/HDPE Pipe 20mm/)).toBeInTheDocument();
    expect(screen.getByText(/ACME/)).toBeInTheDocument();
    expect(screen.getByText(/2\.4500 AED\/kg/)).toBeInTheDocument();
    expect(screen.getByText(/0\.3200 AED\/kg/)).toBeInTheDocument();
    expect(screen.getByText(/0\.1800 AED\/kg/)).toBeInTheDocument();
    expect(screen.getByText(/2\.9500 AED/)).toBeInTheDocument();
  });

  it("live-calculates profit margin as sales price is typed", async () => {
    mockedApi.get.mockResolvedValueOnce({ data: baseReview });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    const priceInput = screen.getByLabelText(/Sales Price/i);
    await user.type(priceInput, "4.2");

    // margin = ((4.2 - 2.95) / 4.2) * 100 = 29.76%
    const pill = screen.getByTestId("margin-pill");
    expect(pill).toHaveTextContent(/29\.76%/);
    expect(pill.className).toMatch(/green/);
  });

  it("approve fires mutation with payload and flips to approved state with Download PDF", async () => {
    mockedApi.get.mockImplementation(() => Promise.resolve({ data: baseReview }));
    mockedApi.post.mockResolvedValueOnce({
      data: { message: "Approved", refNo: "REQ-0042" },
    });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    const priceInput = screen.getByLabelText(/Sales Price/i);
    await user.type(priceInput, "4.2");

    const notesInput = screen.getByLabelText(/Notes/i);
    await user.type(notesInput, "Looks good");

    const approveButton = screen.getByRole("button", { name: /^Approve$/i });
    await user.click(approveButton);

    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: /Download PDF/i }),
      ).toBeInTheDocument(),
    );

    expect(mockedApi.post).toHaveBeenCalledWith(
      "/approvals/42/approve",
      { salesPricePerKgAed: 4.2, notes: "Looks good" },
    );
    expect(screen.getByText(/Quotation approved/i)).toBeInTheDocument();
  });

  it("reject with empty notes shows validation error and does not fire mutation", async () => {
    mockedApi.get.mockResolvedValueOnce({ data: baseReview });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    await user.click(screen.getByRole("button", { name: /^reject$/i }));

    expect(
      screen.getByText(/Notes are required when rejecting/i),
    ).toBeInTheDocument();
    expect(mockedApi.post).not.toHaveBeenCalled();
  });

  it("reject with notes fires mutation and navigates back to detail", async () => {
    mockedApi.get.mockResolvedValueOnce({ data: baseReview });
    mockedApi.post.mockResolvedValueOnce({ data: { message: "Rejected" } });
    const user = userEvent.setup();

    renderPage();
    await waitFor(() =>
      expect(screen.getByText("REQ-0042")).toBeInTheDocument(),
    );

    await user.type(screen.getByLabelText(/Notes/i), "Price too high");
    await user.click(screen.getByRole("button", { name: /^reject$/i }));

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
        screen.getByText(/not found or not ready for review/i),
      ).toBeInTheDocument(),
    );
  });
});
