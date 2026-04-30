import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MdMarginPage } from "./MdMarginPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

function renderAt(path: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/approvals/:id/margin" element={<MdMarginPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("MdMarginPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
  });

  it("submits per-FG margins to set-margin endpoint", async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: {
        id: 100,
        refNo: "REQ-0100",
        status: "MdPricing",
        currencyCode: "USD",
        notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [
          {
            id: 50,
            expectedQty: 5000,
            hasPrinting: false,
            item: { id: 87, code: "FG-0087", description: "FG-A" },
            bomLines: [],
            costs: null,
          },
          {
            id: 51,
            expectedQty: 2000,
            hasPrinting: true,
            item: { id: 88, code: "FG-0088", description: "FG-B" },
            bomLines: [],
            costs: null,
          },
        ],
      },
    });
    vi.mocked(api.post).mockResolvedValue({
      data: { id: 100, status: "CustomerConfirm", approvalId: 5 },
    });

    renderAt("/approvals/100/margin");
    await waitFor(() => expect(screen.getByText("REQ-0100")).toBeInTheDocument());

    const inputs = screen.getAllByRole("spinbutton") as HTMLInputElement[];
    await userEvent.type(inputs[0], "0.5");
    await userEvent.type(inputs[1], "0.7");
    await userEvent.click(screen.getByRole("button", { name: /submit/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith(
        "/approvals/100/set-margin",
        expect.objectContaining({
          items: expect.arrayContaining([
            expect.objectContaining({ requisitionItemId: 50, marginPerKg: 0.5 }),
            expect.objectContaining({ requisitionItemId: 51, marginPerKg: 0.7 }),
          ]),
        }),
      ),
    );
  });
});
