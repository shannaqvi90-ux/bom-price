import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import type { Notification } from "@/types/api";

// ── mock the API hooks ──────────────────────────────────────────────────────
const mockMarkReadMutate = vi.fn();
const mockMarkAllReadMutate = vi.fn();

vi.mock("./notificationsApi", () => ({
  useNotifications: vi.fn(() => ({ isLoading: false })),
  useMarkRead: vi.fn(() => ({ mutate: mockMarkReadMutate, isPending: false })),
  useMarkAllRead: vi.fn(() => ({ mutate: mockMarkAllReadMutate, isPending: false })),
}));

// ── mock the store ──────────────────────────────────────────────────────────
let mockNotifications: Notification[] = [];
let mockUnreadCount = 0;

vi.mock("@/store/notificationsStore", () => ({
  notificationsStore: vi.fn((selector?: (s: unknown) => unknown) => {
    const state = {
      notifications: mockNotifications,
      unreadCount: mockUnreadCount,
      connect: vi.fn(),
      disconnect: vi.fn(),
      setNotifications: vi.fn(),
      prependNotification: vi.fn(),
      markRead: vi.fn(),
      markAllRead: vi.fn(),
      connected: false,
      _connection: null,
    };
    return selector ? selector(state) : state;
  }),
}));

// ── mock navigate ───────────────────────────────────────────────────────────
const mockNavigate = vi.fn();
vi.mock("react-router-dom", async (importOriginal) => {
  const actual = await importOriginal<typeof import("react-router-dom")>();
  return { ...actual, useNavigate: () => mockNavigate };
});

import NotificationsPage from "./NotificationsPage";

function wrap(ui: ReactNode) {
  return <MemoryRouter>{ui}</MemoryRouter>;
}

const unread: Notification = {
  id: 1,
  message: "BOM ready for costing: REQ-0042",
  referenceId: 42,
  referenceType: "QuotationRequest",
  isRead: false,
  createdAt: new Date().toISOString(),
};

const read: Notification = {
  id: 2,
  message: "Quotation approved: REQ-0038",
  referenceId: 38,
  referenceType: "QuotationRequest",
  isRead: true,
  createdAt: new Date().toISOString(),
};

describe("NotificationsPage", () => {
  beforeEach(() => {
    mockNotifications = [];
    mockUnreadCount = 0;
    mockMarkReadMutate.mockReset();
    mockMarkAllReadMutate.mockReset();
    mockNavigate.mockReset();
  });

  it("shows empty state on Unread tab when there are no unread notifications", () => {
    mockNotifications = [read];
    mockUnreadCount = 0;
    render(wrap(<NotificationsPage />));
    expect(screen.getByText("You're all caught up.")).toBeInTheDocument();
  });

  it("shows unread notification with highlighted background on Unread tab", () => {
    mockNotifications = [unread];
    mockUnreadCount = 1;
    render(wrap(<NotificationsPage />));
    const row = screen.getByText("BOM ready for costing: REQ-0042").closest("button");
    expect(row).toHaveClass("bg-muted/40");
  });

  it("shows read notifications when All tab is selected", () => {
    mockNotifications = [unread, read];
    mockUnreadCount = 1;
    render(wrap(<NotificationsPage />));
    fireEvent.click(screen.getByRole("button", { name: /^All$/ }));
    expect(screen.getByText("Quotation approved: REQ-0038")).toBeInTheDocument();
  });

  it("calls mark-read mutation and navigates to requisition on notification click", async () => {
    mockNotifications = [unread];
    mockUnreadCount = 1;
    // Simulate mutation calling onSuccess
    mockMarkReadMutate.mockImplementation((_id: number, opts?: { onSuccess?: () => void }) => {
      opts?.onSuccess?.();
    });
    render(wrap(<NotificationsPage />));
    fireEvent.click(screen.getByText("BOM ready for costing: REQ-0042").closest("button")!);
    expect(mockMarkReadMutate).toHaveBeenCalledWith(1, expect.objectContaining({ onSuccess: expect.any(Function) }));
    await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith("/requisitions/42"));
  });

  it("calls mark-all-read mutation when Mark all read button is clicked", () => {
    mockNotifications = [unread];
    mockUnreadCount = 1;
    render(wrap(<NotificationsPage />));
    fireEvent.click(screen.getByRole("button", { name: /mark all read/i }));
    expect(mockMarkAllReadMutate).toHaveBeenCalled();
  });
});
