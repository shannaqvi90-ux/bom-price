import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { SearchableSelect } from "./SearchableSelect";

interface Opt {
  id: number;
  label: string;
}

const options: Opt[] = [
  { id: 1, label: "Apple" },
  { id: 2, label: "Apricot" },
  { id: 3, label: "Banana" },
];

function getLabel(o: Opt) {
  return o.label;
}
function getValue(o: Opt) {
  return o.id;
}

describe("SearchableSelect", () => {
  it("shows all options when focused with no filter", () => {
    const onChange = vi.fn();
    render(
      <SearchableSelect
        options={options}
        value={null}
        onChange={onChange}
        getLabel={getLabel}
        getValue={getValue}
        placeholder="Pick one"
      />,
    );
    fireEvent.focus(screen.getByPlaceholderText("Pick one"));
    expect(screen.getByText("Apple")).toBeInTheDocument();
    expect(screen.getByText("Apricot")).toBeInTheDocument();
    expect(screen.getByText("Banana")).toBeInTheDocument();
  });

  it("filters options by case-insensitive substring", () => {
    const onChange = vi.fn();
    render(
      <SearchableSelect
        options={options}
        value={null}
        onChange={onChange}
        getLabel={getLabel}
        getValue={getValue}
      />,
    );
    const input = screen.getByRole("combobox");
    fireEvent.focus(input);
    fireEvent.change(input, { target: { value: "ap" } });
    expect(screen.getByText("Apple")).toBeInTheDocument();
    expect(screen.getByText("Apricot")).toBeInTheDocument();
    expect(screen.queryByText("Banana")).not.toBeInTheDocument();
  });

  it("calls onChange with the selected option when a row is clicked", () => {
    const onChange = vi.fn();
    render(
      <SearchableSelect
        options={options}
        value={null}
        onChange={onChange}
        getLabel={getLabel}
        getValue={getValue}
      />,
    );
    const input = screen.getByRole("combobox");
    fireEvent.focus(input);
    fireEvent.mouseDown(screen.getByText("Banana"));
    expect(onChange).toHaveBeenCalledWith(options[2]);
  });
});
