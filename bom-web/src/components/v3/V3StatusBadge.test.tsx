import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { V3StatusBadge } from "./V3StatusBadge";

describe("V3StatusBadge", () => {
  it("renders Draft status with neutral styling", () => {
    render(<V3StatusBadge status="Draft" />);
    const badge = screen.getByText("Draft");
    expect(badge).toBeInTheDocument();
    expect(badge.className).toMatch(/bg-gray-/);
  });

  it("renders Signed status with success styling", () => {
    render(<V3StatusBadge status="Signed" />);
    const badge = screen.getByText("Signed");
    expect(badge.className).toMatch(/bg-green-/);
  });

  it("renders Cancelled status with red styling", () => {
    render(<V3StatusBadge status="Cancelled" />);
    expect(screen.getByText("Cancelled").className).toMatch(/bg-red-/);
  });

  it("renders legacy V2 Approved status with success styling", () => {
    render(<V3StatusBadge status="Approved" />);
    expect(screen.getByText("Approved").className).toMatch(/bg-green-/);
  });
});
