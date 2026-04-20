import { createRequisitionSchema } from "@/utils/validation";

const base = {
  customerId: 1,
  currencyCode: "AED",
  items: [{ itemId: 10, expectedQty: 5 }],
};

test("accepts a valid payload", () => {
  expect(createRequisitionSchema.safeParse(base).success).toBe(true);
});

test("rejects when customerId is missing or zero", () => {
  expect(createRequisitionSchema.safeParse({ ...base, customerId: 0 }).success).toBe(false);
});

test("rejects when currency is empty", () => {
  expect(createRequisitionSchema.safeParse({ ...base, currencyCode: "" }).success).toBe(false);
});

test("rejects when items array is empty", () => {
  expect(createRequisitionSchema.safeParse({ ...base, items: [] }).success).toBe(false);
});

test("rejects when an item has itemId=0", () => {
  expect(
    createRequisitionSchema.safeParse({
      ...base,
      items: [{ itemId: 0, expectedQty: 5 }],
    }).success
  ).toBe(false);
});

test("rejects when an item has expectedQty <= 0", () => {
  expect(
    createRequisitionSchema.safeParse({
      ...base,
      items: [{ itemId: 10, expectedQty: 0 }],
    }).success
  ).toBe(false);
  expect(
    createRequisitionSchema.safeParse({
      ...base,
      items: [{ itemId: 10, expectedQty: -1 }],
    }).success
  ).toBe(false);
});
