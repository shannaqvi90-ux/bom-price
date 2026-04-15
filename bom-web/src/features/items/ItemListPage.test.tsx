import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn() } }));

import { api } from "@/api/axios";
import ItemListPage from "./ItemListPage";

function wrap(ui: ReactNode) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>
  );
}

const emptyItems = { data: [] };

describe("ItemListPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  it("shows Add, Import, and Import from Ledger buttons for Admin", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "Admin", userId: 1, name: "Admin", branchId: null,
    });
    vi.mocked(api.get).mockResolvedValue(emptyItems);

    render(wrap(<ItemListPage />));

    expect(await screen.findByRole("button", { name: /add item/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /^import$/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /import from ledger/i })).toBeInTheDocument();
  });

  it("shows only Add Item for SalesPerson", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "SalesPerson", userId: 2, name: "Ali", branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValue(emptyItems);

    render(wrap(<ItemListPage />));

    expect(await screen.findByRole("button", { name: /add item/i })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^import$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /import from ledger/i })).not.toBeInTheDocument();
  });

  it("shows no action buttons for BomCreator", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "BomCreator", userId: 3, name: "Bob", branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValue(emptyItems);

    render(wrap(<ItemListPage />));

    await waitFor(() =>
      expect(screen.queryAllByTestId("data-table-skeleton-row").length).toBe(0),
    );
    expect(screen.queryByRole("button", { name: /add item/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^import$/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /import from ledger/i })).not.toBeInTheDocument();
  });

  it("shows Edit and Deactivate buttons for Admin on each row", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "Admin", userId: 1, name: "Admin", branchId: null,
    });
    vi.mocked(api.get).mockResolvedValue({
      data: [
        { id: 1, code: "FG-001", description: "Pipe 20mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
      ],
    });

    render(wrap(<ItemListPage />));

    expect(await screen.findByRole("button", { name: /edit pipe 20mm/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /deactivate pipe 20mm/i })).toBeInTheDocument();
  });

  it("shows no Edit/Deactivate buttons for BomCreator", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "BomCreator", userId: 3, name: "Bob", branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValue({
      data: [
        { id: 1, code: "FG-001", description: "Pipe 20mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
      ],
    });

    render(wrap(<ItemListPage />));

    await waitFor(() =>
      expect(screen.queryAllByTestId("data-table-skeleton-row").length).toBe(0),
    );
    expect(screen.queryByRole("button", { name: /edit pipe 20mm/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /deactivate pipe 20mm/i })).not.toBeInTheDocument();
  });

  it("hides inactive items by default and shows them when 'Show inactive' is toggled", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "Admin", userId: 1, name: "Admin", branchId: null,
    });
    vi.mocked(api.get).mockResolvedValue({
      data: [
        { id: 1, code: "FG-001", description: "Pipe 20mm", type: "FinishedGood", branchId: 1, isActive: true, lastPurchasePrice: null },
        { id: 2, code: "RM-002", description: "PE100 Resin", type: "RawMaterial", branchId: 1, isActive: false, lastPurchasePrice: 4.25 },
      ],
    });

    const user = userEvent.setup();
    render(wrap(<ItemListPage />));

    // Active item visible, inactive item hidden by default
    expect(await screen.findByText("Pipe 20mm")).toBeInTheDocument();
    expect(screen.queryByText("PE100 Resin")).not.toBeInTheDocument();

    // Toggle "Show inactive"
    await user.click(screen.getByLabelText(/show inactive/i));

    // Now inactive item appears
    expect(screen.getByText("PE100 Resin")).toBeInTheDocument();
  });
});
