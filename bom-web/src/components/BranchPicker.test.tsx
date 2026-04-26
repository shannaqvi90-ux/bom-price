import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BranchPicker } from "./BranchPicker";

vi.mock("@/api/branches", () => ({
  useBranches: () => ({
    data: [
      { id: 1, name: "Fujairah", isActive: true },
      { id: 2, name: "Al Ain", isActive: true },
    ],
    isPending: false,
  }),
}));

function wrap(ui: React.ReactNode) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("BranchPicker", () => {
  it("renders all active branches as options", () => {
    render(wrap(<BranchPicker value={null} onChange={() => {}} />));
    expect(screen.getByRole("option", { name: "Fujairah" })).toBeInTheDocument();
    expect(screen.getByRole("option", { name: "Al Ain" })).toBeInTheDocument();
  });

  it("calls onChange with the selected branch id", () => {
    const onChange = vi.fn();
    render(wrap(<BranchPicker value={null} onChange={onChange} />));
    fireEvent.change(screen.getByRole("combobox"), { target: { value: "2" } });
    expect(onChange).toHaveBeenCalledWith(2);
  });

  it("preselects the value prop", () => {
    render(wrap(<BranchPicker value={2} onChange={() => {}} />));
    expect((screen.getByRole("combobox") as HTMLSelectElement).value).toBe("2");
  });
});
