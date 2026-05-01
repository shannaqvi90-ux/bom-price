import type { V3Status } from "../../../types/v3";
import type { AccountantTab } from "./AccountantTabs";
import type { InFlightSubFilter } from "./InFlightSubFilterChips";

export function statusesForTab(tab: AccountantTab, sub: InFlightSubFilter = "all"): V3Status[] {
  switch (tab) {
    case "queue":
      return ["Costing"];
    case "done":
      return ["Signed"];
    case "closed":
      return ["Rejected", "Cancelled"];
    case "in-flight":
      if (sub === "md") return ["MdPricing", "MdFinalSign"];
      if (sub === "customer") return ["CustomerConfirm"];
      return ["MdPricing", "CustomerConfirm", "MdFinalSign"];
    default:
      return ["Costing"];
  }
}
