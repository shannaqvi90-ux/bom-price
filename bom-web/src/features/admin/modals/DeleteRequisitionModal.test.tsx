import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { DeleteRequisitionModal } from "./DeleteRequisitionModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useDeleteRequisition: () => ({ mutateAsync: mockMutate, isPending: false })
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("DeleteRequisitionModal", () => {
  beforeEach(() => mockMutate.mockClear());

  it("disables confirm when reason is too short", () => {
    render(wrap(<DeleteRequisitionModal requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }} onClose={() => {}} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "abc" } });
    expect(screen.getByRole("button", { name: /^delete$/i })).toBeDisabled();
  });

  it("enables confirm when reason is at least 5 chars", () => {
    render(wrap(<DeleteRequisitionModal requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }} onClose={() => {}} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "duplicate created by SP" } });
    expect(screen.getByRole("button", { name: /^delete$/i })).not.toBeDisabled();
  });

  it("calls mutation with id and reason then closes on success", async () => {
    mockMutate.mockResolvedValueOnce(undefined);
    const onClose = vi.fn();
    render(wrap(<DeleteRequisitionModal requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }} onClose={onClose} />));
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "duplicate created by SP" } });
    fireEvent.click(screen.getByRole("button", { name: /^delete$/i }));
    await waitFor(() => expect(mockMutate).toHaveBeenCalledWith({ id: 1, reason: "duplicate created by SP" }));
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("shows the requisition refNo in the title", () => {
    render(wrap(<DeleteRequisitionModal requisition={{ id: 1, refNo: "REQ-0042", status: "BomPending" }} onClose={() => {}} />));
    expect(screen.getByText(/REQ-0042/)).toBeInTheDocument();
  });

  it("Cancel button calls onClose without mutation", () => {
    const onClose = vi.fn();
    render(wrap(<DeleteRequisitionModal requisition={{ id: 1, refNo: "REQ-1", status: "BomPending" }} onClose={onClose} />));
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(onClose).toHaveBeenCalled();
    expect(mockMutate).not.toHaveBeenCalled();
  });
});
