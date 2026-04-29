import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { CreateCustomerModal } from "./CreateCustomerModal";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: { post: vi.fn(), get: vi.fn() },
}));

function renderWithProviders(ui: React.ReactElement) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={qc}>{ui}</QueryClientProvider>);
}

describe("CreateCustomerModal", () => {
  beforeEach(() => {
    vi.mocked(api.post).mockReset();
  });

  it("posts new customer and calls onCreated with returned customer", async () => {
    vi.mocked(api.post).mockResolvedValue({
      data: {
        id: 99, code: "CUST-0099", name: "Acme",
        email: "a@b.com", phoneNumber: "+1234", address: "Test Address",
        salesPersonId: 1, salesPersonName: "Sara", createdByUserId: 1,
      },
    });

    const onCreated = vi.fn();
    const onClose = vi.fn();
    renderWithProviders(<CreateCustomerModal open={true} onClose={onClose} onCreated={onCreated} />);

    await userEvent.type(screen.getByLabelText(/name/i), "Acme");
    await userEvent.type(screen.getByLabelText(/email/i), "a@b.com");
    await userEvent.type(screen.getByLabelText(/phone/i), "+1234");
    await userEvent.type(screen.getByLabelText(/address/i), "Test Address");
    await userEvent.click(screen.getByRole("button", { name: /create/i }));

    await waitFor(() =>
      expect(onCreated).toHaveBeenCalledWith(
        expect.objectContaining({ id: 99, code: "CUST-0099" }),
      ),
    );
    expect(onClose).toHaveBeenCalled();
  });

  it("shows validation error if name is empty", async () => {
    renderWithProviders(<CreateCustomerModal open={true} onClose={vi.fn()} onCreated={vi.fn()} />);
    await userEvent.click(screen.getByRole("button", { name: /create/i }));
    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
    expect(api.post).not.toHaveBeenCalled();
  });
});
