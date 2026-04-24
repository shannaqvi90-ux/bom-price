import { formatCurrency, formatMoney, formatPct } from "@/utils/numbers";

test("formatMoney keeps four decimals", () => {
  expect(formatMoney(12.3456)).toBe("12.3456");
});

test("formatMoney rounds correctly", () => {
  expect(formatMoney(12.34567)).toBe("12.3457");
});

test("formatMoney returns '-' for null/undefined/NaN", () => {
  expect(formatMoney(null)).toBe("-");
  expect(formatMoney(undefined)).toBe("-");
  expect(formatMoney(Number.NaN)).toBe("-");
});

test("formatPct renders with one decimal and % suffix", () => {
  expect(formatPct(12.34)).toBe("12.3%");
});

test("formatPct returns '-' for null", () => {
  expect(formatPct(null)).toBe("-");
});

describe("formatCurrency", () => {
  it("formats AED with thousand separators and 2 decimals", () => {
    expect(formatCurrency(2500, "AED")).toBe("AED 2,500.00");
  });

  it("handles zero", () => {
    expect(formatCurrency(0, "AED")).toBe("AED 0.00");
  });

  it("handles decimal precision", () => {
    expect(formatCurrency(125.5, "USD")).toBe("USD 125.50");
  });

  it("handles null / undefined / NaN", () => {
    expect(formatCurrency(null, "AED")).toBe("-");
    expect(formatCurrency(undefined, "AED")).toBe("-");
    expect(formatCurrency(Number.NaN, "AED")).toBe("-");
  });
});
