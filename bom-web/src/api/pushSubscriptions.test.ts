import { describe, it, expect, vi, beforeEach } from "vitest";
import { pushSubscriptions } from "./pushSubscriptions";
import { api } from "./axios";

vi.mock("./axios", () => ({
  api: { post: vi.fn(), delete: vi.fn() },
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("pushSubscriptions", () => {
  it("subscribe POSTs payload to /notifications/push-subscribe", async () => {
    vi.mocked(api.post).mockResolvedValue({ data: null });
    await pushSubscriptions.subscribe({
      endpoint: "https://x",
      keys: { p256dh: "p", auth: "a" },
      userAgent: "iPhone Test",
    });
    expect(api.post).toHaveBeenCalledWith("/notifications/push-subscribe", {
      endpoint: "https://x",
      keys: { p256dh: "p", auth: "a" },
      userAgent: "iPhone Test",
    });
  });

  it("unsubscribe DELETEs with endpoint in body", async () => {
    vi.mocked(api.delete).mockResolvedValue({ data: null });
    await pushSubscriptions.unsubscribe("https://x");
    expect(api.delete).toHaveBeenCalledWith("/notifications/push-subscribe", {
      data: { endpoint: "https://x" },
    });
  });
});
