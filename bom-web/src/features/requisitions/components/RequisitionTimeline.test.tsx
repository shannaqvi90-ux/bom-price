import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { RequisitionTimeline } from "./RequisitionTimeline";

const createdAt = "2026-04-14T10:00:00Z";
const updatedAt = "2026-04-14T11:30:00Z";

describe("RequisitionTimeline", () => {
  it("renders all five step labels", () => {
    render(
      <RequisitionTimeline
        status="BomPending"
        createdAt={createdAt}
        updatedAt={updatedAt}
      />,
    );
    expect(screen.getByText("Submitted")).toBeInTheDocument();
    expect(screen.getByText("BOM")).toBeInTheDocument();
    expect(screen.getByText("Costing")).toBeInTheDocument();
    expect(screen.getByText("MD Review")).toBeInTheDocument();
    expect(screen.getByText("Result")).toBeInTheDocument();
  });

  it('marks the BOM step as in-progress when status is "BomPending"', () => {
    render(
      <RequisitionTimeline
        status="BomPending"
        createdAt={createdAt}
        updatedAt={updatedAt}
      />,
    );
    expect(screen.getByTestId("step-BOM")).toHaveAttribute("data-state", "in-progress");
    expect(screen.getByTestId("step-Submitted")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-Costing")).toHaveAttribute("data-state", "pending");
  });

  it('marks all prior steps completed and Result as "approved" when status is "Approved"', () => {
    render(
      <RequisitionTimeline
        status="Approved"
        createdAt={createdAt}
        updatedAt={updatedAt}
      />,
    );
    expect(screen.getByTestId("step-Submitted")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-BOM")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-Costing")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-MD Review")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-Result")).toHaveAttribute("data-state", "approved");
  });

  it('collapses middle steps to cancelled when status is "Rejected"', () => {
    render(
      <RequisitionTimeline
        status="Rejected"
        createdAt={createdAt}
        updatedAt={updatedAt}
      />,
    );
    expect(screen.getByTestId("step-Submitted")).toHaveAttribute("data-state", "completed");
    expect(screen.getByTestId("step-BOM")).toHaveAttribute("data-state", "cancelled");
    expect(screen.getByTestId("step-Costing")).toHaveAttribute("data-state", "cancelled");
    expect(screen.getByTestId("step-MD Review")).toHaveAttribute("data-state", "cancelled");
    expect(screen.getByTestId("step-Result")).toHaveAttribute("data-state", "rejected");
  });
});
