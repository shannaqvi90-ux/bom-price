import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";
import type { BomReviewResponse, RequisitionDetail } from "@/types/api";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn(), put: vi.fn() } }));

vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));

import { api } from "@/api/axios";
import BomEntryPage from "./BomEntryPage";

// ── Mock data ──────────────────────────────────────────────────────────────────

const baseRequisition: RequisitionDetail = {
  id: 1, refNo: "REQ-0001", status: "BomPending",
  customerId: 1,
  customerName: "ACME", customerEmail: "a@b.com", customerPhone: "123",
  customerAddress: "Addr", currencyCode: "AED",
  exchangeRateSnapshot: null, branchId: 1, branchName: "Fujairah",
  salesPersonId: 2, salesPersonName: "Ali", createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
  items: [{ id: 1, itemId: 1, itemDescription: "HDPE Pipe", expectedQty: 100, sortOrder: 1 }],
  approval: null,
};

const mockRequisitionBomPending = { data: baseRequisition };
const mockRequisitionBomInProgress = { data: { ...baseRequisition, status: "BomInProgress" as const } };

const baseBom: BomReviewResponse = {
  requisitionId: 1,
  refNo: "REQ-0001",
  requisitionStatus: "BomInProgress",
  items: [
    {
      requisitionItemId: 1,
      itemId: 1,
      itemDescription: "HDPE Pipe",
      expectedQty: 100,
      sortOrder: 1,
      bomHeaderId: 10,
      bomStatus: "InProgress",
      lines: [],
      totalCostPerKg: 0,
      submittedAt: null,
    },
  ],
};

const baseBomNotStarted: BomReviewResponse = {
  ...baseBom,
  requisitionStatus: "BomPending",
  items: [
    {
      ...baseBom.items[0],
      bomHeaderId: null,
      bomStatus: "NotStarted",
    },
  ],
};

const mockBomEmpty = { data: baseBom };

const mockBomWithLines: { data: BomReviewResponse } = {
  data: {
    ...baseBom,
    items: [
      {
        ...baseBom.items[0],
        lines: [
          {
            id: 1, processId: 5, processName: "Extrusion",
            rawMaterialItemId: 3, rawMaterialDescription: "HDPE Granules",
            qtyPerKg: 0.85, wastagePct: 2.0,
            costPerKg: null, currencyCode: null,
            costPerKgInAed: null, contributionAed: null,
          },
        ],
      },
    ],
  },
};

const mockProcesses = {
  data: [
    { id: 5, name: "Extrusion", displayOrder: 1, isActive: true },
    { id: 6, name: "Blending", displayOrder: 2, isActive: true },
  ],
};

const mockItems = {
  data: [
    { id: 3, code: "RM-001", description: "HDPE Granules", type: "RawMaterial", branchId: 1, isActive: true, lastPurchasePrice: null },
    { id: 4, code: "RM-002", description: "UV Stabiliser", type: "RawMaterial", branchId: 1, isActive: true, lastPurchasePrice: null },
  ],
};

// ── Wrapper ────────────────────────────────────────────────────────────────────

function wrap(ui: ReactNode, initialPath = "/requisitions/1/bom") {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return (
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initialPath]}>
        <Routes>
          <Route path="/requisitions/:id/bom" element={ui} />
          <Route path="/requisitions/:id" element={<div data-testid="detail-page" />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("BomEntryPage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
    vi.mocked(api.put).mockReset();
    useAuthStore.getState().setSession({
      accessToken: "at", refreshToken: "rt",
      role: "BomCreator", userId: 3, name: "Bob", branchId: 1, mustChangePassword: false,
    });
  });

  afterEach(() => {
    useAuthStore.getState().logout();
  });

  function setupMocks(
    requisitionMock: typeof mockRequisitionBomPending,
    bomMock: { data: BomReviewResponse } | null,
  ) {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/requisitions/1") return Promise.resolve(requisitionMock);
      if (url === "/bom/1") {
        if (bomMock === null) return Promise.reject({ response: { status: 404 } });
        return Promise.resolve(bomMock);
      }
      if (url === "/processes") return Promise.resolve(mockProcesses);
      if (url === "/items") return Promise.resolve(mockItems);
      return Promise.reject(new Error(`Unmocked: ${url}`));
    });
  }

  it("auto-calls /start for the first NotStarted item when status is BomPending", async () => {
    let startCalled = false;
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/requisitions/1") {
        return Promise.resolve(startCalled ? mockRequisitionBomInProgress : mockRequisitionBomPending);
      }
      if (url === "/bom/1") {
        if (!startCalled) return Promise.resolve({ data: baseBomNotStarted });
        return Promise.resolve(mockBomEmpty);
      }
      if (url === "/processes") return Promise.resolve(mockProcesses);
      if (url === "/items") return Promise.resolve(mockItems);
      return Promise.reject(new Error(`Unmocked: ${url}`));
    });
    vi.mocked(api.post).mockImplementation((url: string) => {
      if (url === "/bom/1/items/1/start") {
        startCalled = true;
        return Promise.resolve({ data: { id: 10 } });
      }
      return Promise.reject(new Error(`Unmocked POST: ${url}`));
    });

    render(wrap(<BomEntryPage />));

    await waitFor(() => {
      expect(vi.mocked(api.post)).toHaveBeenCalledWith("/bom/1/items/1/start");
    }, { timeout: 3000 });
  });

  it("does NOT call /start when status is already BomInProgress", async () => {
    setupMocks(mockRequisitionBomInProgress, mockBomEmpty);

    render(wrap(<BomEntryPage />));

    await screen.findByText("BOM Entry");
    expect(vi.mocked(api.post)).not.toHaveBeenCalledWith(
      expect.stringContaining("/start"),
    );
  });

  it("renders existing lines from fetched BOM", async () => {
    setupMocks(mockRequisitionBomInProgress, mockBomWithLines);

    render(wrap(<BomEntryPage />));

    expect(await screen.findByText(/Extrusion/)).toBeInTheDocument();
    expect(screen.getByText("HDPE Granules")).toBeInTheDocument();
    expect(screen.getByText("0.8500")).toBeInTheDocument();
  });

  it("shows Net Qty warning when overall net deviates from 1.0 by more than 0.01", async () => {
    setupMocks(mockRequisitionBomInProgress, mockBomWithLines);

    render(wrap(<BomEntryPage />));

    expect(await screen.findByText(/Net Qty\/kg is/)).toBeInTheDocument();
  });

  it("shows no Net Qty warning when net is within 0.01 of 1.0", async () => {
    const nearOneBom: { data: BomReviewResponse } = {
      data: {
        ...baseBom,
        items: [
          {
            ...baseBom.items[0],
            lines: [
              {
                id: 1, processId: 5, processName: "Extrusion",
                rawMaterialItemId: 3, rawMaterialDescription: "HDPE Granules",
                qtyPerKg: 1.0, wastagePct: 0.0,
                costPerKg: null, currencyCode: null,
                costPerKgInAed: null, contributionAed: null,
              },
            ],
          },
        ],
      },
    };
    setupMocks(mockRequisitionBomInProgress, nearOneBom);

    render(wrap(<BomEntryPage />));

    await screen.findByText("HDPE Granules");
    expect(screen.queryByText(/Net Qty\/kg is/)).not.toBeInTheDocument();
  });

  it("Submit All button is disabled when no lines exist", async () => {
    setupMocks(mockRequisitionBomInProgress, mockBomEmpty);

    render(wrap(<BomEntryPage />));

    await screen.findByText("BOM Entry");
    expect(screen.getByRole("button", { name: /submit all/i })).toBeDisabled();
  });

  it("Submit All button is enabled when lines exist", async () => {
    setupMocks(mockRequisitionBomInProgress, mockBomWithLines);

    render(wrap(<BomEntryPage />));

    await screen.findByText("HDPE Granules");
    expect(screen.getByRole("button", { name: /submit all/i })).toBeEnabled();
  });

  it("navigates to detail page on successful submit", async () => {
    setupMocks(mockRequisitionBomInProgress, mockBomWithLines);
    vi.mocked(api.post).mockResolvedValueOnce({ data: {} }); // submit

    render(wrap(<BomEntryPage />));

    await screen.findByText("HDPE Granules");
    await userEvent.click(screen.getByRole("button", { name: /submit all/i }));

    await waitFor(() => {
      expect(screen.getByTestId("detail-page")).toBeInTheDocument();
    });
  });

  it("renders field error message when server rejects Lines[0].QtyPerKg", async () => {
    setupMocks(mockRequisitionBomInProgress, mockBomWithLines);
    vi.mocked(api.post).mockRejectedValueOnce({
      response: {
        data: {
          detail: "QtyPerKg must be greater than 0.",
          errors: { "Lines[0].QtyPerKg": ["Must be greater than 0."] },
        },
      },
    });

    render(wrap(<BomEntryPage />));

    await screen.findByText("HDPE Granules");
    await userEvent.click(screen.getByRole("button", { name: /submit all/i }));

    await waitFor(() => {
      expect(screen.getByText("Must be greater than 0.")).toBeInTheDocument();
    });
  });

  it("fires auto-save PUT /lines after removing a line", async () => {
    setupMocks(mockRequisitionBomInProgress, mockBomWithLines);
    vi.mocked(api.put).mockResolvedValue({ data: {} });

    render(wrap(<BomEntryPage />));

    await screen.findByText("HDPE Granules");
    await userEvent.click(screen.getByRole("button", { name: /remove line/i }));

    await waitFor(() => {
      expect(vi.mocked(api.put)).toHaveBeenCalledWith(
        expect.stringContaining("/bom/1/items/1/lines"),
        expect.objectContaining({ lines: [] }),
      );
    });
  });
});
