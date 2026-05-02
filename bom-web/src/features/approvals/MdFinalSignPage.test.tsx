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

const REQ_FIXTURE = {
  id: 100,
  refNo: "REQ-0100",
  status: "MdFinalSign",
  currencyCode: "USD",
  notes: "",
  customer: { id: 1, name: "Acme", code: "CUST-0001" },
  salesPerson: { id: 2, name: "Ali" },
  finishedGoods: [],
  finalPrice: {
    totalAed: 0,
    currencyCode: "USD",
    rateSnapshot: null,
    perFg: [],
  },
};

// MdFinalSignPage fetches both the requisition and the MD's signature blob.
// Differentiate the mock per URL so the signature returns a Blob (enabling the
// Sign button) rather than the req payload.
function mockGetByUrl(reqData: unknown) {
  vi.mocked(api.get).mockImplementation((url: string) => {
    if (url.includes("/profile/signature")) {
      return Promise.resolve({ data: new Blob(["x"], { type: "image/png" }) });
    }
    return Promise.resolve({ data: reqData });
  });
  // jsdom needs URL.createObjectURL to exist for blob preview.
  if (!URL.createObjectURL) {
    URL.createObjectURL = () => "blob:mock";
    URL.revokeObjectURL = () => {};
  }
}

describe("MdFinalSignPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
  });

  it("requires SIGN token to enable submit", async () => {
    mockGetByUrl(REQ_FIXTURE);

    renderAt("/approvals/100/final");
    await screen.findByText("REQ-0100");
    const submitBtn = screen.getByRole("button", { name: /sign and lock/i }) as HTMLButtonElement;
    expect(submitBtn).toBeDisabled();

    await userEvent.type(screen.getByLabelText(/type SIGN to confirm/i), "SIGN");
    await waitFor(() => expect(submitBtn).not.toBeDisabled());
  });

  it("posts final-sign on submit", async () => {
    mockGetByUrl(REQ_FIXTURE);
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
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /sign and lock/i })).not.toBeDisabled(),
    );
    await userEvent.click(screen.getByRole("button", { name: /sign and lock/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith(
        "/approvals/100/final-sign",
        expect.objectContaining({ confirmationToken: "SIGN" }),
      ),
    );
  });
});
