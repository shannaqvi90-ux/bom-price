import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusBadge } from "./StatusBadge";
import type { RequisitionStatus } from "@/types/api";

const cases: Array<{ status: RequisitionStatus; label: string }> = [
  // V3 statuses (current workflow):
  { status: "Draft", label: "Draft" },
  { status: "Costing", label: "Costing" },
  { status: "MdPricing", label: "MD Pricing" },
  { status: "CustomerConfirm", label: "Customer Confirm" },
  { status: "MdFinalSign", label: "MD Sign" },
  { status: "Signed", label: "Signed" },
  { status: "Cancelled", label: "Cancelled" },
  { status: "Rejected", label: "Rejected" },
  // Legacy V2.3 statuses (historical reqs):
  { status: "BomPending", label: "BOM Pending" },
  { status: "BomInProgress", label: "BOM In Progress" },
  { status: "CostingPending", label: "Costing Pending" },
  { status: "CostingInProgress", label: "Costing In Progress" },
  { status: "MdReview", label: "MD Review" },
  { status: "Approved", label: "Approved" },
];

describe("StatusBadge", () => {
  it.each(cases)("renders a badge with readable label for $status", ({ status, label }) => {
    render(<StatusBadge status={status} />);
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it("applies an amber colour class for pending statuses", () => {
    const { container } = render(<StatusBadge status="BomPending" />);
    expect(container.firstChild).toHaveClass("bg-amber-500/10");
  });
});
