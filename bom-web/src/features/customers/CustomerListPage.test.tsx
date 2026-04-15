import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";
import CustomerListPage from "./CustomerListPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: { get: vi.fn(), post: vi.fn() },
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

const sampleCustomers = [
  {
    id: 1,
    code: "CUST-001",
    name: "ACME Corp",
    address: "123 Main St",
    email: "acme@example.com",
    phoneNumber: "555-1234",
    salesPersonId: 5,
    salesPersonName: "Ali",
    createdByUserId: 10,
  },
  {
    id: 2,
    code: "CUST-002",
    name: "Beta Ltd",
    address: "",
    email: "",
    phoneNumber: "",
    salesPersonId: null,
    salesPersonName: null,
    createdByUserId: 10,
  },
];

function loginAs(role: string) {
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role,
    userId: 1,
    name: "Test User",
    branchId: null,
  });
}

beforeEach(() => {
  vi.mocked(api.get).mockReset();
  vi.mocked(api.post as ReturnType<typeof vi.fn>).mockReset();
});

afterEach(() => {
  useAuthStore.getState().logout();
});

// Helper: get the inner modal panel element from a dialog heading
function getModalPanel(headingName: string): HTMLElement {
  return screen.getByRole("heading", { name: headingName }).parentElement!;
}

describe("CustomerListPage", () => {
  it("renders customer rows from API response", async () => {
    loginAs("Admin");
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleCustomers });
    wrap(<CustomerListPage />);
    await waitFor(() => expect(screen.getByText("ACME Corp")).toBeInTheDocument());
    expect(screen.getByText("CUST-001")).toBeInTheDocument();
    expect(screen.getByText("acme@example.com")).toBeInTheDocument();
    expect(screen.getByText("Ali")).toBeInTheDocument();
    expect(screen.getByText("Beta Ltd")).toBeInTheDocument();
    expect(screen.getByText("—")).toBeInTheDocument(); // null salesPersonName
  });

  it("shows Add Customer and Import buttons for Admin", async () => {
    loginAs("Admin");
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    wrap(<CustomerListPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Customer/i })).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: /^Import$/i })).toBeInTheDocument();
  });

  it("hides Add Customer and Import buttons for non-Admin", async () => {
    loginAs("SalesPerson");
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleCustomers });
    wrap(<CustomerListPage />);
    await waitFor(() => expect(screen.getByText("ACME Corp")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /Add Customer/i })).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^Import$/i })).not.toBeInTheDocument();
  });

  it("shows error state with Retry button on API failure", async () => {
    loginAs("Admin");
    vi.mocked(api.get).mockRejectedValueOnce(new Error("Network error"));
    wrap(<CustomerListPage />);
    await waitFor(() =>
      expect(screen.getByText(/Failed to load customers/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: /Retry/i })).toBeInTheDocument();
  });

  it("shows empty state when no customers returned", async () => {
    loginAs("Admin");
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    wrap(<CustomerListPage />);
    await waitFor(() =>
      expect(screen.getByText(/No customers found/i)).toBeInTheDocument(),
    );
  });

  it("opens AddCustomerModal when Add Customer button is clicked", async () => {
    loginAs("Admin");
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    const user = userEvent.setup();
    wrap(<CustomerListPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Customer/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add Customer/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add Customer" })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/^Code$/i)).toBeInTheDocument();
  });

  it("opens ImportCustomersModal when Import button is clicked", async () => {
    loginAs("Admin");
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    const user = userEvent.setup();
    wrap(<CustomerListPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Import$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Import$/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Import Customers" })).toBeInTheDocument(),
    );
    expect(screen.getByText(/Download template/i)).toBeInTheDocument();
  });
});

describe("AddCustomerModal", () => {
  beforeEach(() => {
    loginAs("Admin");
    vi.mocked(api.get).mockResolvedValue({ data: [] });
  });

  async function openAddModal() {
    const user = userEvent.setup();
    wrap(<CustomerListPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Customer/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add Customer/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add Customer" })).toBeInTheDocument(),
    );
    return user;
  }

  it("shows validation errors when required fields are empty", async () => {
    const user = await openAddModal();
    await user.click(screen.getByRole("button", { name: /^Save$/i }));
    await waitFor(() =>
      expect(screen.getByText(/Code is required/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Name is required/i)).toBeInTheDocument();
  });

  it("submits correct payload and closes modal on success", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: {
        id: 99,
        code: "CUST-099",
        name: "New Co",
        address: "",
        email: "",
        phoneNumber: "",
        salesPersonId: null,
        salesPersonName: null,
        createdByUserId: 1,
      },
    });
    // Second get returns updated list after invalidation
    vi.mocked(api.get).mockResolvedValue({ data: [] });

    const user = await openAddModal();

    await user.type(screen.getByLabelText(/^Code$/i), "CUST-099");
    await user.type(screen.getByLabelText(/^Name$/i), "New Co");

    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add Customer" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.post)).toHaveBeenCalledWith(
      "/customers",
      expect.objectContaining({ code: "CUST-099", name: "New Co" }),
    );
  });

  it("shows server error message on submit failure", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Code already exists" } },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/^Code$/i), "CUST-001");
    await user.type(screen.getByLabelText(/^Name$/i), "Duplicate");

    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Code already exists/i)).toBeInTheDocument(),
    );
    // Modal stays open on error
    expect(screen.getByRole("heading", { name: "Add Customer" })).toBeInTheDocument();
  });

  it("resets form and closes modal when Cancel is clicked", async () => {
    const user = await openAddModal();
    await user.type(screen.getByLabelText(/^Code$/i), "TYPED");
    await user.click(screen.getByRole("button", { name: /Cancel/i }));
    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add Customer" })).not.toBeInTheDocument(),
    );
  });
});

describe("ImportCustomersModal", () => {
  beforeEach(() => {
    loginAs("Admin");
    vi.mocked(api.get).mockResolvedValue({ data: [] });
  });

  async function openImportModal() {
    const user = userEvent.setup();
    wrap(<CustomerListPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Import$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Import$/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Import Customers" })).toBeInTheDocument(),
    );
    return user;
  }

  it("Import button inside modal is disabled until a file is selected", async () => {
    await openImportModal();
    const modal = getModalPanel("Import Customers");
    // Cancel + Import buttons; Import must be disabled
    const importBtn = within(modal).getAllByRole("button").find(
      (b) => b.textContent?.trim() === "Import",
    );
    expect(importBtn).toBeDefined();
    expect(importBtn).toBeDisabled();
  });

  it("shows import results after successful upload", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: { imported: 5, skipped: 1, errors: [] },
    });
    const user = await openImportModal();

    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const file = new File(["col1,col2"], "customers.csv", { type: "text/csv" });
    await user.upload(fileInput, file);

    const modal = getModalPanel("Import Customers");
    const importBtn = within(modal)
      .getAllByRole("button")
      .find((b) => b.textContent?.trim() === "Import")!;
    await user.click(importBtn);

    await waitFor(() =>
      expect(screen.getByText(/Import complete/i)).toBeInTheDocument(),
    );
    expect(screen.getByText("5")).toBeInTheDocument();
    expect(screen.getByText("1")).toBeInTheDocument();
  });

  it("shows error message on import failure", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Invalid file format" } },
    });
    const user = await openImportModal();

    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const file = new File(["bad"], "bad.csv", { type: "text/csv" });
    await user.upload(fileInput, file);

    const modal = getModalPanel("Import Customers");
    const importBtn = within(modal)
      .getAllByRole("button")
      .find((b) => b.textContent?.trim() === "Import")!;
    await user.click(importBtn);

    await waitFor(() =>
      expect(screen.getByText(/Invalid file format/i)).toBeInTheDocument(),
    );
  });
});
