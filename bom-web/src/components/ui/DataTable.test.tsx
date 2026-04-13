import { render, screen, fireEvent } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { DataTable } from "./DataTable";
import type { ColumnDef } from "@tanstack/react-table";

interface Row {
  id: number;
  name: string;
  qty: number;
}

const rows: Row[] = [
  { id: 1, name: "Alpha", qty: 10 },
  { id: 2, name: "Bravo", qty: 20 },
];

const columns: ColumnDef<Row>[] = [
  { accessorKey: "name", header: "Name" },
  { accessorKey: "qty", header: "Qty" },
];

describe("DataTable", () => {
  it("renders column headers and rows", () => {
    render(<DataTable columns={columns} data={rows} />);
    expect(screen.getByText("Name")).toBeInTheDocument();
    expect(screen.getByText("Qty")).toBeInTheDocument();
    expect(screen.getByText("Alpha")).toBeInTheDocument();
    expect(screen.getByText("Bravo")).toBeInTheDocument();
  });

  it("shows skeleton rows when isLoading", () => {
    render(<DataTable columns={columns} data={[]} isLoading />);
    expect(screen.getAllByTestId("data-table-skeleton-row")).toHaveLength(5);
  });

  it("renders an empty state when data is empty and not loading", () => {
    render(
      <DataTable
        columns={columns}
        data={[]}
        emptyState={<div>no rows</div>}
      />,
    );
    expect(screen.getByText("no rows")).toBeInTheDocument();
  });

  it("fires onRowClick when a row is clicked", () => {
    const onRowClick = vi.fn();
    render(<DataTable columns={columns} data={rows} onRowClick={onRowClick} />);
    fireEvent.click(screen.getByText("Alpha"));
    expect(onRowClick).toHaveBeenCalledWith(rows[0]);
  });

  it("sorts rows when a sortable header is clicked", () => {
    render(<DataTable columns={columns} data={rows} />);
    // Click "Name" header — default ascending → Alpha, Bravo is already correct, so click twice for desc
    fireEvent.click(screen.getByText("Name"));
    fireEvent.click(screen.getByText("Name"));
    const visibleRows = screen.getAllByRole("row").slice(1); // skip header row
    expect(visibleRows[0]).toHaveTextContent("Bravo");
    expect(visibleRows[1]).toHaveTextContent("Alpha");
  });
});
