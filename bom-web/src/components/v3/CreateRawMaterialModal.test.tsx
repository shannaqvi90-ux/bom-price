import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { CreateRawMaterialModal } from "./CreateRawMaterialModal";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({ api: { post: vi.fn() } }));

function renderWith(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("CreateRawMaterialModal", () => {
  beforeEach(() => vi.mocked(api.post).mockReset());

  it("creates a RawMaterial item and calls onCreated", async () => {
    vi.mocked(api.post).mockResolvedValue({
      data: {
        id: 34, code: "RM-0034", description: "Test RM",
        type: "RawMaterial", branchId: 2, isActive: true, lastPurchasePrice: null,
      },
    });

    const onCreated = vi.fn();
    renderWith(<CreateRawMaterialModal open={true} onClose={vi.fn()} onCreated={onCreated} />);

    await userEvent.type(screen.getByLabelText(/description/i), "Test RM");
    await userEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() =>
      expect(onCreated).toHaveBeenCalledWith(
        expect.objectContaining({ id: 34, code: "RM-0034", type: "RawMaterial" }),
      ),
    );
  });
});
