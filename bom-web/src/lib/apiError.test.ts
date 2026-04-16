import { describe, it, expect } from "vitest";
import { extractApiError } from "./apiError";

describe("extractApiError", () => {
  it("returns response.data.message when present", () => {
    const err = { response: { data: { message: "Bad qty" } } };
    expect(extractApiError(err)).toBe("Bad qty");
  });

  it("returns fallback when no message", () => {
    const err = { response: { data: {} } };
    expect(extractApiError(err, "fallback")).toBe("fallback");
  });

  it("returns default fallback when no response", () => {
    expect(extractApiError(new Error("boom"))).toBe("Something went wrong");
  });

  it("handles unknown shapes safely", () => {
    expect(extractApiError(null)).toBe("Something went wrong");
    expect(extractApiError(undefined)).toBe("Something went wrong");
    expect(extractApiError("string")).toBe("Something went wrong");
  });
});
