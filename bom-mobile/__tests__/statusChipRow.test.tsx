import React from "react";
import { render, fireEvent } from "@testing-library/react-native";
import { StatusChipRow, CHIP_TO_STATUSES, CHIPS } from "../src/components/StatusChipRow";

describe("StatusChipRow", () => {
  it("renders all 6 chips", () => {
    const onChange = jest.fn();
    const { getByText } = render(<StatusChipRow active="MD review" onChange={onChange} />);
    for (const c of CHIPS) {
      expect(getByText(c)).toBeTruthy();
    }
  });

  it("invokes onChange with the chip label when pressed", () => {
    const onChange = jest.fn();
    const { getByText } = render(<StatusChipRow active="All" onChange={onChange} />);
    fireEvent.press(getByText("BOM"));
    expect(onChange).toHaveBeenCalledWith("BOM");
  });
});

describe("CHIP_TO_STATUSES", () => {
  it("All returns empty array (no filter)", () => {
    expect(CHIP_TO_STATUSES["All"]).toEqual([]);
  });

  it("BOM groups BomPending + BomInProgress", () => {
    expect(CHIP_TO_STATUSES["BOM"]).toEqual(["BomPending", "BomInProgress"]);
  });

  it("Costing groups CostingPending + CostingInProgress", () => {
    expect(CHIP_TO_STATUSES["Costing"]).toEqual(["CostingPending", "CostingInProgress"]);
  });

  it("MD review maps to MdReview only", () => {
    expect(CHIP_TO_STATUSES["MD review"]).toEqual(["MdReview"]);
  });
});
