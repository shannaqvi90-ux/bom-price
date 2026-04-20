import { formatShortDate } from "@/utils/dates";

test("formats an ISO string as dd MMM", () => {
  expect(formatShortDate("2026-04-17T10:30:00Z")).toBe("17 Apr");
});

test("returns '-' for null/empty/undefined", () => {
  expect(formatShortDate(null)).toBe("-");
  expect(formatShortDate(undefined)).toBe("-");
  expect(formatShortDate("")).toBe("-");
});

test("returns '-' for unparseable strings", () => {
  expect(formatShortDate("not-a-date")).toBe("-");
});
