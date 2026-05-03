import { statusesForTab } from "./tabFilters";

describe("statusesForTab (MD)", () => {
  it("queue returns MdPricing + MdFinalSign", () => {
    expect(statusesForTab("queue", "all")).toEqual(["MdPricing", "MdFinalSign"]);
  });
  it("in-flight all returns customer + costing", () => {
    expect(statusesForTab("in-flight", "all")).toEqual(["CustomerConfirm", "Costing"]);
  });
  it("in-flight customer narrows to CustomerConfirm only", () => {
    expect(statusesForTab("in-flight", "customer")).toEqual(["CustomerConfirm"]);
  });
  it("in-flight costing narrows to Costing only", () => {
    expect(statusesForTab("in-flight", "costing")).toEqual(["Costing"]);
  });
  it("done returns Signed", () => {
    expect(statusesForTab("done", "all")).toEqual(["Signed"]);
  });
  it("closed returns Rejected + Cancelled", () => {
    expect(statusesForTab("closed", "all")).toEqual(["Rejected", "Cancelled"]);
  });
});
