import { approveSchema } from "@/utils/validation";

const base = {
  items: [{ requisitionItemId: 10, salesPricePerKgAed: 5 }],
  notes: "Looks good",
};

test("accepts a valid payload", () => {
  expect(approveSchema.safeParse(base).success).toBe(true);
});

test("accepts when notes is omitted", () => {
  expect(approveSchema.safeParse({ items: base.items }).success).toBe(true);
});

test("rejects when items is empty", () => {
  expect(approveSchema.safeParse({ items: [] }).success).toBe(false);
});

test("rejects when salesPricePerKgAed <= 0", () => {
  expect(
    approveSchema.safeParse({
      items: [{ requisitionItemId: 10, salesPricePerKgAed: 0 }],
    }).success
  ).toBe(false);
  expect(
    approveSchema.safeParse({
      items: [{ requisitionItemId: 10, salesPricePerKgAed: -1 }],
    }).success
  ).toBe(false);
});

test("rejects when requisitionItemId is missing", () => {
  expect(
    approveSchema.safeParse({
      items: [{ salesPricePerKgAed: 5 }],
    }).success
  ).toBe(false);
});

test("rejects when notes exceeds 2000 chars", () => {
  expect(
    approveSchema.safeParse({
      items: base.items,
      notes: "x".repeat(2001),
    }).success
  ).toBe(false);
});
