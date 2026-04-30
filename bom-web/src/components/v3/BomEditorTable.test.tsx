import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it, expect, vi } from "vitest";
import { BomEditorTable, type BomLineRow } from "./BomEditorTable";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

function renderWith(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("BomEditorTable", () => {
  it("renders existing lines + supports adding new line", async () => {
    vi.mocked(api.get).mockResolvedValue({
      data: [
        { id: 12, code: "RM-0012", description: "BOPP", type: "RawMaterial", branchId: 2, isActive: true, lastPurchasePrice: null },
        { id: 34, code: "RM-0034", description: "INK", type: "RawMaterial", branchId: 2, isActive: true, lastPurchasePrice: null },
      ],
    });

    const lines: BomLineRow[] = [
      { itemId: 12, qtyPerKg: 0.44, micron: "20", processId: 1 },
    ];

    const onChange = vi.fn();
    renderWith(<BomEditorTable lines={lines} onChange={onChange} />);

    await waitFor(() => expect(screen.getByDisplayValue("0.44")).toBeInTheDocument());
    expect(screen.getByDisplayValue("20")).toBeInTheDocument();

    await userEvent.click(screen.getByRole("button", { name: /add raw material/i }));

    expect(onChange).toHaveBeenCalledWith(
      expect.arrayContaining([
        expect.objectContaining({ itemId: 12, qtyPerKg: 0.44, micron: "20" }),
        expect.objectContaining({ itemId: 0, qtyPerKg: 0, micron: "" }),
      ])
    );
  });

  it("renders read-only mode without inputs", () => {
    const lines: BomLineRow[] = [
      { itemId: 12, qtyPerKg: 0.44, micron: "20", processId: 1 },
    ];
    renderWith(<BomEditorTable lines={lines} readOnly={true} />);
    expect(screen.queryByDisplayValue("0.44")).not.toBeInTheDocument();
    expect(screen.getByText("0.44")).toBeInTheDocument();
  });
});
