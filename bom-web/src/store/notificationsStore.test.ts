import { describe, it, expect, vi, beforeEach } from "vitest";
import { notificationsStore } from "./notificationsStore";
import { notify } from "@/lib/notify";
import type { Notification } from "@/types/api";

vi.mock("@/lib/notify", () => ({
  notify: {
    error: vi.fn(),
    success: vi.fn(),
    info: vi.fn(),
    fromApiError: vi.fn(),
  },
}));

beforeEach(() => {
  vi.clearAllMocks();
  notificationsStore.setState({
    notifications: [],
    unreadCount: 0,
    connected: false,
    _connection: null,
  });
});

describe("notificationsStore", () => {
  it("prependNotification adds and increments unreadCount", () => {
    const n: Notification = {
      id: 1,
      message: "Test",
      referenceId: 42,
      referenceType: "QuotationRequest",
      isRead: false,
      createdAt: "2026-04-16T12:00:00Z",
    };
    notificationsStore.getState().prependNotification(n);

    const state = notificationsStore.getState();
    expect(state.notifications).toHaveLength(1);
    expect(state.notifications[0].id).toBe(1);
    expect(state.unreadCount).toBe(1);
  });

  it("showToastForNotification fires notify.info with clickable View action for QuotationRequest", () => {
    const n: Notification = {
      id: 10,
      message: "Your quotation is ready",
      referenceId: 7,
      referenceType: "QuotationRequest",
      isRead: false,
      createdAt: "2026-04-16T12:00:00Z",
    };

    notificationsStore.getState().showToastForNotification(n);

    expect(notify.info).toHaveBeenCalledWith(
      "Your quotation is ready",
      expect.objectContaining({
        action: expect.objectContaining({
          label: "View",
          onClick: expect.any(Function),
        }),
      }),
    );
  });

  it("showToastForNotification fires notify.info WITHOUT action for unknown referenceType", () => {
    const n: Notification = {
      id: 20,
      message: "Unknown event",
      referenceId: 99,
      referenceType: "FutureTypeNotMapped",
      isRead: false,
      createdAt: "2026-04-16T12:00:00Z",
    };

    notificationsStore.getState().showToastForNotification(n);

    expect(notify.info).toHaveBeenCalledWith(
      "Unknown event",
      expect.objectContaining({
        action: undefined,
      }),
    );
  });
});
