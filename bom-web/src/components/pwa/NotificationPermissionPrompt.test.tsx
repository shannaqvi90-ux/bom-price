import { describe, it, expect, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";

vi.mock("sonner", () => ({ toast: Object.assign(vi.fn(), { dismiss: vi.fn() }) }));
vi.mock("@/features/notifications/usePushSubscription", () => ({
  usePushSubscription: vi.fn(),
}));
vi.mock("@/utils/platform", () => ({ isStandalone: vi.fn() }));

import { NotificationPermissionPrompt } from "./NotificationPermissionPrompt";
import { toast } from "sonner";
import { usePushSubscription } from "@/features/notifications/usePushSubscription";
import { isStandalone } from "@/utils/platform";

const mockHook = (permission: NotificationPermission) =>
  vi.mocked(usePushSubscription).mockReturnValue({
    permission,
    isSubscribed: false,
    subscribe: vi.fn(),
    unsubscribe: vi.fn(),
  });

beforeEach(() => {
  vi.clearAllMocks();
  localStorage.clear();
});

describe("NotificationPermissionPrompt", () => {
  it("shows toast when standalone + permission default + not dismissed", () => {
    vi.mocked(isStandalone).mockReturnValue(true);
    mockHook("default");
    render(<NotificationPermissionPrompt />);
    expect(toast).toHaveBeenCalledWith(
      "🔔 Get notified when reqs need you?",
      expect.objectContaining({
        action: expect.objectContaining({ label: "Enable" }),
      })
    );
  });

  it("does NOT show toast when not standalone (browser tab)", () => {
    vi.mocked(isStandalone).mockReturnValue(false);
    mockHook("default");
    render(<NotificationPermissionPrompt />);
    expect(toast).not.toHaveBeenCalled();
  });

  it("does NOT show toast when permission already granted", () => {
    vi.mocked(isStandalone).mockReturnValue(true);
    mockHook("granted");
    render(<NotificationPermissionPrompt />);
    expect(toast).not.toHaveBeenCalled();
  });

  it("does NOT show toast when permission denied", () => {
    vi.mocked(isStandalone).mockReturnValue(true);
    mockHook("denied");
    render(<NotificationPermissionPrompt />);
    expect(toast).not.toHaveBeenCalled();
  });

  it("does NOT show toast when dismissed within 14 days", () => {
    vi.mocked(isStandalone).mockReturnValue(true);
    mockHook("default");
    localStorage.setItem("push-prompt-dismissed-at", String(Date.now() - 7 * 24 * 60 * 60 * 1000));
    render(<NotificationPermissionPrompt />);
    expect(toast).not.toHaveBeenCalled();
  });

  it("shows toast again after 14 days have passed", () => {
    vi.mocked(isStandalone).mockReturnValue(true);
    mockHook("default");
    localStorage.setItem("push-prompt-dismissed-at", String(Date.now() - 30 * 24 * 60 * 60 * 1000));
    render(<NotificationPermissionPrompt />);
    expect(toast).toHaveBeenCalled();
  });
});
