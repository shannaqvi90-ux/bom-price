import { vi, describe, it, expect, beforeEach } from "vitest";
import { toast } from "sonner";
import { notify } from "./notify";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), {
    error: vi.fn(),
    success: vi.fn(),
  }),
}));

beforeEach(() => {
  vi.clearAllMocks();
});

describe("notify", () => {
  it("error calls toast.error with 6s default duration", () => {
    notify.error("oops");
    expect(toast.error).toHaveBeenCalledWith("oops", { duration: 6000 });
  });

  it("success calls toast.success with 3s default duration", () => {
    notify.success("ok");
    expect(toast.success).toHaveBeenCalledWith("ok", { duration: 3000 });
  });

  it("info calls toast with 4s default and optional action", () => {
    const onClick = vi.fn();
    notify.info("hi", { action: { label: "View", onClick } });
    expect(toast).toHaveBeenCalledWith(
      "hi",
      expect.objectContaining({
        duration: 4000,
        action: { label: "View", onClick },
      }),
    );
  });

  it("fromApiError extracts message and calls error", () => {
    const err = { response: { data: { message: "Bad request" } } };
    notify.fromApiError(err);
    expect(toast.error).toHaveBeenCalledWith("Bad request", { duration: 6000 });
  });

  it("fromApiError uses fallback when no message", () => {
    notify.fromApiError(new Error("x"), "Custom fallback");
    expect(toast.error).toHaveBeenCalledWith("Custom fallback", { duration: 6000 });
  });
});
