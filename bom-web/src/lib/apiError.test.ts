import { describe, it, expect } from "vitest";
import { extractApiError, extractFieldErrors } from "./apiError";

describe("extractApiError", () => {
  it("returns response.data.detail when present", () => {
    const err = { response: { data: { detail: "Bad qty" } } };
    expect(extractApiError(err)).toBe("Bad qty");
  });

  it("returns fallback when no detail", () => {
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

describe("extractFieldErrors", () => {
  it("extracts first message per field and normalizes PascalCase bracket → lowercase dot", () => {
    const err = { response: { data: { errors: { "Items[0].ExpectedQty": ["Must be > 0."] } } } };
    expect(extractFieldErrors(err)).toEqual({ "items.0.expectedqty": "Must be > 0." });
  });

  it("handles multi-field payloads", () => {
    const err = {
      response: {
        data: {
          errors: {
            "Items[1].ExpectedQty": ["A"],
            "Items[2].ItemId": ["B"],
          },
        },
      },
    };
    expect(extractFieldErrors(err)).toEqual({
      "items.1.expectedqty": "A",
      "items.2.itemid": "B",
    });
  });

  it("returns empty object when no errors field", () => {
    expect(extractFieldErrors({ response: { data: { detail: "x" } } })).toEqual({});
  });

  it("returns empty object for unknown shapes", () => {
    expect(extractFieldErrors(null)).toEqual({});
    expect(extractFieldErrors(undefined)).toEqual({});
    expect(extractFieldErrors(new Error("x"))).toEqual({});
  });
});
