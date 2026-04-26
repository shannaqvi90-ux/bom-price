import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { ResetPasswordModal } from "./ResetPasswordModal";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactElement } from "react";

const mockMutate = vi.fn();
vi.mock("@/api/admin", () => ({
  useResetPassword: () => ({ mutateAsync: mockMutate, isPending: false, error: null }),
}));

function wrap(ui: ReactElement) {
  return render(
    <QueryClientProvider client={new QueryClient()}>{ui}</QueryClientProvider>,
  );
}

describe("ResetPasswordModal", () => {
  beforeEach(() => mockMutate.mockReset());

  it("shows reason form first; reveals temp password after success", async () => {
    mockMutate.mockResolvedValue({ tempPassword: "Xy7$kQ9pM2!w" });
    wrap(<ResetPasswordModal user={{ id: 5, name: "Test User" }} onClose={() => {}} />);
    expect(screen.getByLabelText(/reason/i)).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "user locked out" } });
    fireEvent.click(screen.getByRole("button", { name: /^reset$/i }));
    await waitFor(() => expect(screen.getByText(/Xy7\$kQ9pM2!w/)).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /copy/i })).toBeInTheDocument();
  });

  it("calls mutation with id and reason", async () => {
    mockMutate.mockResolvedValue({ tempPassword: "Abc123!@#xyz" });
    wrap(<ResetPasswordModal user={{ id: 7, name: "Other" }} onClose={() => {}} />);
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "valid reason text" } });
    fireEvent.click(screen.getByRole("button", { name: /^reset$/i }));
    await waitFor(() => expect(mockMutate).toHaveBeenCalledWith({ id: 7, reason: "valid reason text" }));
  });

  it("disables Reset button when reason too short", () => {
    render(
      <QueryClientProvider client={new QueryClient()}>
        <ResetPasswordModal user={{ id: 5, name: "Test" }} onClose={() => {}} />
      </QueryClientProvider>,
    );
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "abc" } });
    expect(screen.getByRole("button", { name: /^reset$/i })).toBeDisabled();
  });

  it("close button on reveal panel calls onClose", async () => {
    mockMutate.mockResolvedValue({ tempPassword: "Xy7$kQ9pM2!w" });
    const onClose = vi.fn();
    wrap(<ResetPasswordModal user={{ id: 5, name: "Test" }} onClose={onClose} />);
    fireEvent.change(screen.getByLabelText(/reason/i), { target: { value: "user locked out" } });
    fireEvent.click(screen.getByRole("button", { name: /^reset$/i }));
    await waitFor(() => screen.getByText(/Xy7/));
    fireEvent.click(screen.getByRole("button", { name: /i've copied it|close/i }));
    expect(onClose).toHaveBeenCalled();
  });
});
