import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { RollbackStatusModal } from "./RollbackStatusModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useRollbackStatus: () => ({ mutateAsync: mockMutate, isPending: false }),
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("RollbackStatusModal", () => {
  beforeEach(() => mockMutate.mockClear());

  it("shows the (only) whitelisted target for current status", () => {
    render(
      wrap(
        <RollbackStatusModal
          requisition={{ id: 1, refNo: "REQ-1", status: "Approved" }}
          onClose={() => {}}
        />,
      ),
    );
    expect(screen.getByText(/MdReview/)).toBeInTheDocument();
    expect(screen.queryByText(/BomPending/)).not.toBeInTheDocument();
  });

  it("shows 'no rollback target' when current status has no whitelist entry", () => {
    render(
      wrap(
        <RollbackStatusModal
          requisition={{ id: 1, refNo: "REQ-1", status: "Rejected" }}
          onClose={() => {}}
        />,
      ),
    );
    expect(screen.getByText(/no rollback target/i)).toBeInTheDocument();
  });

  it("disables Rollback button when reason is too short", () => {
    render(
      wrap(
        <RollbackStatusModal
          requisition={{ id: 1, refNo: "REQ-1", status: "Approved" }}
          onClose={() => {}}
        />,
      ),
    );
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "abc" } });
    expect(screen.getByRole("button", { name: /^rollback$/i })).toBeDisabled();
  });

  it("submits rollback with target and reason then closes", async () => {
    mockMutate.mockResolvedValueOnce(undefined);
    const onClose = vi.fn();
    render(
      wrap(
        <RollbackStatusModal
          requisition={{ id: 1, refNo: "REQ-1", status: "Approved" }}
          onClose={onClose}
        />,
      ),
    );
    fireEvent.change(screen.getByLabelText(/reason/i), {
      target: { value: "MD approved by mistake" },
    });
    fireEvent.click(screen.getByRole("button", { name: /^rollback$/i }));
    await waitFor(() =>
      expect(mockMutate).toHaveBeenCalledWith({
        id: 1,
        targetStatus: "MdReview",
        reason: "MD approved by mistake",
      }),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("Cancel button closes without mutation", () => {
    const onClose = vi.fn();
    render(
      wrap(
        <RollbackStatusModal
          requisition={{ id: 1, refNo: "REQ-1", status: "Approved" }}
          onClose={onClose}
        />,
      ),
    );
    fireEvent.click(screen.getByRole("button", { name: /cancel|close/i }));
    expect(onClose).toHaveBeenCalled();
    expect(mockMutate).not.toHaveBeenCalled();
  });
});
