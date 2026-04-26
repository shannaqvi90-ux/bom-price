import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { ReassignSpModal } from "./ReassignSpModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useReassignSp: () => ({ mutateAsync: mockMutate, isPending: false }),
}));

vi.mock("@/features/users/usersApi", () => ({
  useUsers: () => ({
    data: [
      { id: 10, name: "Sp One", role: "SalesPerson", isActive: true },
      { id: 11, name: "Sp Two", role: "SalesPerson", isActive: true },
      { id: 12, name: "Acct", role: "Accountant", isActive: true },
      { id: 13, name: "Inactive Sp", role: "SalesPerson", isActive: false },
    ],
    isLoading: false,
  }),
}));

function wrap(ui: React.ReactElement) {
  return (
    <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>
  );
}

describe("ReassignSpModal", () => {
  beforeEach(() => mockMutate.mockClear());

  it("only lists active SalesPersons in the dropdown", () => {
    render(
      wrap(
        <ReassignSpModal
          requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }}
          onClose={() => {}}
        />,
      ),
    );
    expect(screen.getByText(/Sp One/)).toBeInTheDocument();
    expect(screen.getByText(/Sp Two/)).toBeInTheDocument();
    expect(screen.queryByText(/Acct/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Inactive Sp/)).not.toBeInTheDocument();
  });

  it("disables Reassign button when no SP selected", () => {
    render(
      wrap(
        <ReassignSpModal
          requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }}
          onClose={() => {}}
        />,
      ),
    );
    fireEvent.change(screen.getByLabelText(/reason/i), {
      target: { value: "valid reason here" },
    });
    expect(
      screen.getByRole("button", { name: /^reassign$/i }),
    ).toBeDisabled();
  });

  it("disables Reassign when reason too short", () => {
    render(
      wrap(
        <ReassignSpModal
          requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }}
          onClose={() => {}}
        />,
      ),
    );
    fireEvent.change(screen.getByLabelText(/new salesperson/i), {
      target: { value: "11" },
    });
    fireEvent.change(screen.getByLabelText(/reason/i), {
      target: { value: "abc" },
    });
    expect(
      screen.getByRole("button", { name: /^reassign$/i }),
    ).toBeDisabled();
  });

  it("submits with selected SP id and reason", async () => {
    mockMutate.mockResolvedValueOnce(undefined);
    const onClose = vi.fn();
    render(
      wrap(
        <ReassignSpModal
          requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }}
          onClose={onClose}
        />,
      ),
    );
    fireEvent.change(screen.getByLabelText(/new salesperson/i), {
      target: { value: "11" },
    });
    fireEvent.change(screen.getByLabelText(/reason/i), {
      target: { value: "Sp1 has left" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^reassign$/i }));
    await waitFor(() =>
      expect(mockMutate).toHaveBeenCalledWith({
        id: 1,
        newSalesPersonId: 11,
        reason: "Sp1 has left",
      }),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("Cancel closes without mutation", () => {
    const onClose = vi.fn();
    render(
      wrap(
        <ReassignSpModal
          requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }}
          onClose={onClose}
        />,
      ),
    );
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(onClose).toHaveBeenCalled();
    expect(mockMutate).not.toHaveBeenCalled();
  });
});
