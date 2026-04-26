import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { UnlockBomModal } from "./UnlockBomModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useUnlockBom: () => ({ mutateAsync: mockMutate, isPending: false }),
}));

function wrap(ui: React.ReactElement) {
  return <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>;
}

describe("UnlockBomModal", () => {
  beforeEach(() => mockMutate.mockClear());

  it("disables Unlock BOM button when reason is too short", () => {
    render(
      wrap(
        <UnlockBomModal
          requisition={{ id: 1, refNo: "REQ-1", status: "MdReview" }}
          onClose={() => {}}
        />,
      ),
    );
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "abc" } });
    expect(screen.getByRole("button", { name: /unlock bom/i })).toBeDisabled();
  });

  it("submits with reason and closes on success", async () => {
    mockMutate.mockResolvedValueOnce(undefined);
    const onClose = vi.fn();
    render(
      wrap(
        <UnlockBomModal
          requisition={{ id: 1, refNo: "REQ-1", status: "MdReview" }}
          onClose={onClose}
        />,
      ),
    );
    fireEvent.change(screen.getByLabelText(/reason/i), {
      target: { value: "wastage corrected" },
    });
    fireEvent.click(screen.getByRole("button", { name: /unlock bom/i }));
    await waitFor(() =>
      expect(mockMutate).toHaveBeenCalledWith({ id: 1, reason: "wastage corrected" }),
    );
    await waitFor(() => expect(onClose).toHaveBeenCalled());
  });

  it("shows requisition refNo in title", () => {
    render(
      wrap(
        <UnlockBomModal
          requisition={{ id: 1, refNo: "REQ-0042", status: "MdReview" }}
          onClose={() => {}}
        />,
      ),
    );
    expect(screen.getByText(/REQ-0042/)).toBeInTheDocument();
  });

  it("Cancel closes without mutation", () => {
    const onClose = vi.fn();
    render(
      wrap(
        <UnlockBomModal
          requisition={{ id: 1, refNo: "REQ-1", status: "MdReview" }}
          onClose={onClose}
        />,
      ),
    );
    fireEvent.click(screen.getByRole("button", { name: /cancel/i }));
    expect(onClose).toHaveBeenCalled();
    expect(mockMutate).not.toHaveBeenCalled();
  });
});
