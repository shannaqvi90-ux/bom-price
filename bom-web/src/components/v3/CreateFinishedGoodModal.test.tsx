import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { CreateFinishedGoodModal } from "./CreateFinishedGoodModal";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({ api: { post: vi.fn() } }));

function renderWith(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("CreateFinishedGoodModal", () => {
  beforeEach(() => vi.mocked(api.post).mockReset());

  it("creates a FinishedGood item and calls onCreated", async () => {
    vi.mocked(api.post).mockResolvedValue({
      data: {
        id: 87, code: "FG-0087", description: "Test FG",
        type: "FinishedGood", branchId: 2, isActive: true, lastPurchasePrice: null,
      },
    });

    const onCreated = vi.fn();
    renderWith(<CreateFinishedGoodModal open={true} onClose={vi.fn()} onCreated={onCreated} />);

    await userEvent.type(screen.getByLabelText(/description/i), "Test FG");
    await userEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() =>
      expect(onCreated).toHaveBeenCalledWith(
        expect.objectContaining({ id: 87, code: "FG-0087", type: "FinishedGood" }),
      ),
    );
  });
});
