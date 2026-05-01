import { computeFinalPrice } from "./finalPriceClient";

describe("computeFinalPrice", () => {
  it("AED req: salePerKgAed = salePerKg", () => {
    const result = computeFinalPrice({
      currencyCode: "AED",
      rateSnapshot: null,
      perFg: [{ requisitionItemId: 1, expectedQty: 5000, costPerKg: 3.20, marginPerKg: 1.80 }],
    });
    expect(result.totalAed).toBe(25000);
    expect(result.perFg[0].salePerKg).toBe(5);
    expect(result.perFg[0].salePerKgAed).toBe(5);
  });

  it("Foreign req uses rateSnapshot", () => {
    const result = computeFinalPrice({
      currencyCode: "USD",
      rateSnapshot: 3.6725,
      perFg: [{ requisitionItemId: 1, expectedQty: 5000, costPerKg: 1.00, marginPerKg: 1.00 }],
    });
    expect(result.perFg[0].salePerKgAed).toBeCloseTo(7.345, 4);
    expect(result.totalAed).toBeCloseTo(36725, 1);
  });

  it("Multi-FG sums correctly", () => {
    const result = computeFinalPrice({
      currencyCode: "AED",
      rateSnapshot: null,
      perFg: [
        { requisitionItemId: 1, expectedQty: 5000, costPerKg: 3, marginPerKg: 1 },
        { requisitionItemId: 2, expectedQty: 5000, costPerKg: 5, marginPerKg: 2 },
      ],
    });
    expect(result.totalAed).toBe(55000);
  });
});
