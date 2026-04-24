import React from "react";
import { render } from "@testing-library/react-native";
import { ItemPriceBlock } from "../src/components/ItemPriceBlock";

describe("ItemPriceBlock", () => {
  it("renders price per kg and line total for Approved", () => {
    const { getByText } = render(
      <ItemPriceBlock expectedQty={20} pricePerKg={125} currencyCode="AED" />
    );
    expect(getByText(/Price \/ kg/i)).toBeTruthy();
    expect(getByText("AED 125.00")).toBeTruthy();
    expect(getByText(/Line total/i)).toBeTruthy();
    expect(getByText("AED 2,500.00")).toBeTruthy();
  });

  it("renders zero price as AED 0.00", () => {
    const { getAllByText } = render(
      <ItemPriceBlock expectedQty={5} pricePerKg={0} currencyCode="AED" />
    );
    // both price/kg and line total are AED 0.00 when pricePerKg=0
    expect(getAllByText("AED 0.00").length).toBeGreaterThanOrEqual(1);
  });
});
