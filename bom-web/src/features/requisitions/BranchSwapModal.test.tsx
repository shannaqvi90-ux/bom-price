import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { BranchSwapModal } from "./BranchSwapModal";

const changeBranch = vi.fn().mockResolvedValue({});

vi.mock("./requisitionsApi", async () => {
  const actual = await vi.importActual<typeof import("./requisitionsApi")>("./requisitionsApi");
  return { ...actual, useChangeBranch: () => ({ mutateAsync: changeBranch, isPending: false }) };
});

vi.mock("@/api/branches", () => ({
  useBranches: () => ({
    data: [
      { id: 1, name: "Fujairah", isActive: true },
      { id: 2, name: "Al Ain", isActive: true },
    ],
    isPending: false,
  }),
}));

function wrap(ui: ReactNode) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("BranchSwapModal", () => {
  it("Save calls useChangeBranch with picked branch + reason; closes on success", async () => {
    const onClose = vi.fn();
    render(
      wrap(
        <BranchSwapModal
          requisitionId={42}
          currentBranchId={1}
          open={true}
          onClose={onClose}
        />,
      ),
    );

    fireEvent.change(screen.getByRole("combobox"), { target: { value: "2" } });
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "wrong branch picked" } });
    fireEvent.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(changeBranch).toHaveBeenCalledWith({ branchId: 2, reason: "wrong branch picked" });
      expect(onClose).toHaveBeenCalled();
    });
  });
});
