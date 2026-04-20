import { formatMoney, formatPct } from "@/utils/numbers";

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
