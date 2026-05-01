import { fgReadiness, type FgDraftState } from "./fgReadiness";

describe("fgReadiness", () => {
  const empty: FgDraftState = {
    requisitionItemId: 1,
    hasPrinting: false,
    rawMaterialCosts: [{ bomLineId: 10, costPerKg: "", currencyCode: "" }],
    printingCostPerKg: "",
    printingCostCurrency: "",
    fohPerKg: "",
    transportPerKg: "",
    commissionPerKg: "",
  };

  it("returns 'not_started' when no fields touched", () => {
    expect(fgReadiness(empty)).toBe("not_started");
  });

  it("returns 'in_progress' when some RM cost set but others missing", () => {
    expect(fgReadiness({ ...empty, fohPerKg: "1.5" })).toBe("in_progress");
  });

  it("returns 'ready' when all RM costs > 0 + currencies set + FOH/Transport/Commission non-empty", () => {
    const state: FgDraftState = {
      ...empty,
      rawMaterialCosts: [{ bomLineId: 10, costPerKg: "2.5", currencyCode: "AED" }],
      fohPerKg: "1.0",
      transportPerKg: "0.5",
      commissionPerKg: "0",
    };
    expect(fgReadiness(state)).toBe("ready");
  });

  it("requires printing fields when hasPrinting=true", () => {
    const stateNoPrinting: FgDraftState = {
      ...empty,
      hasPrinting: true,
      rawMaterialCosts: [{ bomLineId: 10, costPerKg: "2.5", currencyCode: "AED" }],
      fohPerKg: "1.0",
      transportPerKg: "0.5",
      commissionPerKg: "0",
    };
    expect(fgReadiness(stateNoPrinting)).toBe("in_progress");

    const statePrinting: FgDraftState = {
      ...stateNoPrinting,
      printingCostPerKg: "0.8",
      printingCostCurrency: "AED",
    };
    expect(fgReadiness(statePrinting)).toBe("ready");
  });

  it("rejects RM cost = 0 as not-yet-ready", () => {
    const state: FgDraftState = {
      ...empty,
      rawMaterialCosts: [{ bomLineId: 10, costPerKg: "0", currencyCode: "AED" }],
      fohPerKg: "1.0",
      transportPerKg: "0.5",
      commissionPerKg: "0",
    };
    expect(fgReadiness(state)).toBe("in_progress");
  });
});
