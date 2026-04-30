import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MdFinalSignPage } from "./MdFinalSignPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

function renderAt(path: string) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/approvals/:id/final" element={<MdFinalSignPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("MdFinalSignPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
  });

  it("requires SIGN token to enable submit", async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: {
        id: 100,
        refNo: "REQ-0100",
        status: "MdFinalSign",
        currencyCode: "USD",
        notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [],
      },
    });

    renderAt("/approvals/100/final");
    await screen.findByText("REQ-0100");
    const submitBtn = screen.getByRole("button", { name: /sign and lock/i }) as HTMLButtonElement;
    expect(submitBtn).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/type SIGN to confirm/i), "SIGN");
    expect(submitBtn).not.toBeDisabled();
  });

  it("posts final-sign on submit", async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: {
        id: 100,
        refNo: "REQ-0100",
        status: "MdFinalSign",
        currencyCode: "USD",
        notes: "",
        customer: { id: 1, name: "Acme", code: "CUST-0001" },
        salesPerson: { id: 2, name: "Ali" },
        finishedGoods: [],
      },
    });
    vi.mocked(api.post).mockResolvedValue({
      data: {
        id: 100,
        status: "Signed",
        approvalId: 5,
        pdfDownloadUrl: "/api/approvals/100/pdf",
      },
    });

    renderAt("/approvals/100/final");
    await screen.findByText("REQ-0100");
    await userEvent.type(screen.getByLabelText(/type SIGN to confirm/i), "SIGN");
    await userEvent.click(screen.getByRole("button", { name: /sign and lock/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith(
        "/approvals/100/final-sign",
        expect.objectContaining({ confirmationToken: "SIGN" }),
      ),
    );
  });
});
