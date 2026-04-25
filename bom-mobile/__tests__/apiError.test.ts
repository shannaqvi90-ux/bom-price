import { extractApiError, extractFieldErrors } from "@/utils/apiError";

describe("extractApiError", () => {
  it("returns response.data.detail when present", () => {
    const err = { response: { data: { detail: "boom" } } };
    expect(extractApiError(err)).toBe("boom");
  });

  it("returns fallback when detail missing", () => {
    expect(extractApiError(null)).toBe("Something went wrong");
    expect(extractApiError({ response: { data: {} } }, "fb")).toBe("fb");
  });
});

describe("extractFieldErrors", () => {
  it("flattens ASP.NET ValidationProblemDetails errors map", () => {
    const err = { response: { data: { errors: { "Items[0].ExpectedQty": ["Must be > 0."] } } } };
    expect(extractFieldErrors(err)).toEqual({ "items.0.expectedQty": "Must be > 0." });
  });

  it("camelCases segment heads, keeps numeric segments untouched", () => {
    const err = {
      response: {
        data: {
          errors: {
            "Lines[2].CostPerKg": ["required"],
            "LandedCostValue": ["must be >= 0"],
          },
        },
      },
    };
    expect(extractFieldErrors(err)).toEqual({
      "lines.2.costPerKg": "required",
      "landedCostValue": "must be >= 0",
    });
  });

  it("returns {} when no errors", () => {
    expect(extractFieldErrors({ response: { data: { detail: "x" } } })).toEqual({});
    expect(extractFieldErrors(null)).toEqual({});
  });
});
