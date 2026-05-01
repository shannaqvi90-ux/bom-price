import type { V3Status } from "@/types/v3";

export type MdTab = "queue" | "in-flight" | "done" | "closed";
export type InFlightSubFilter = "all" | "customer" | "costing";

export function statusesForTab(tab: MdTab, sub: InFlightSubFilter = "all"): V3Status[] {
  switch (tab) {
    case "queue":
      return ["MdPricing", "MdFinalSign"];
    case "in-flight":
      if (sub === "customer") return ["CustomerConfirm"];
      if (sub === "costing") return ["Costing"];
      return ["CustomerConfirm", "Costing"];
    case "done":
      return ["Signed"];
    case "closed":
      return ["Rejected", "Cancelled"];
    default:
      return [];
  }
}
