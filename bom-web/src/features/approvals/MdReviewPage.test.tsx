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
});
