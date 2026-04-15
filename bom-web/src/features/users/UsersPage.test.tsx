import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";
import UsersPage from "./UsersPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

const sampleUsers = [
  {
    id: 1,
    name: "Alice Admin",
    email: "alice@example.com",
    role: "Admin",
    branchId: null,
    branchName: null,
    isActive: true,
  },
  {
    id: 2,
    name: "Bob Sales",
    email: "bob@example.com",
    role: "SalesPerson",
    branchId: null,
    branchName: null,
    isActive: false,
  },
];

function loginAsAdmin() {
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role: "Admin",
    userId: 99,
    name: "Test Admin",
    branchId: null,
  });
}

function getModalPanel(headingName: string): HTMLElement {
  return screen.getByRole("heading", { name: headingName }).parentElement!;
}

beforeEach(() => {
  vi.mocked(api.get).mockReset();
  vi.mocked(api.post as ReturnType<typeof vi.fn>).mockReset();
  vi.mocked(api.put as ReturnType<typeof vi.fn>).mockReset();
  vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockReset();
  loginAsAdmin();
});

afterEach(() => {
  useAuthStore.getState().logout();
});

// ─── UsersPage ────────────────────────────────────────────────────────────────

describe("UsersPage", () => {
  it("renders user rows (name, email, role, active badge)", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());
    expect(screen.getByText("alice@example.com")).toBeInTheDocument();
    expect(screen.getByText("Admin")).toBeInTheDocument();
    expect(screen.getByText("Bob Sales")).toBeInTheDocument();
    expect(screen.getByText("bob@example.com")).toBeInTheDocument();
    expect(screen.getByText("SalesPerson")).toBeInTheDocument();
    // active badges
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("hides Deactivate button for inactive users", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Bob Sales")).toBeInTheDocument());
    // Alice (active) has Deactivate button
    expect(screen.getByRole("button", { name: /Deactivate Alice Admin/i })).toBeInTheDocument();
    // Bob (inactive) does not
    expect(screen.queryByRole("button", { name: /Deactivate Bob Sales/i })).not.toBeInTheDocument();
  });

  it("opens AddUserModal on Add User click", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add User/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add User/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add User" })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/^Name$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Email$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Password$/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^Role$/i)).toBeInTheDocument();
  });

  it("opens EditUserModal pre-populated when Edit clicked", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    expect(screen.getByDisplayValue("Alice Admin")).toBeInTheDocument();
    expect(screen.getByDisplayValue("alice@example.com")).toBeInTheDocument();
  });

  it("shows error state with Retry button on API failure", async () => {
    vi.mocked(api.get).mockRejectedValueOnce(new Error("Network error"));
    wrap(<UsersPage />);
    await waitFor(() =>
      expect(screen.getByText(/Failed to load users/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: /Retry/i })).toBeInTheDocument();
  });

  it("shows empty state when no users returned", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    wrap(<UsersPage />);
    await waitFor(() =>
      expect(screen.getByText(/No users found/i)).toBeInTheDocument(),
    );
  });

  it("Deactivate shows confirmation dialog; cancel dismisses", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Deactivate Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByText(/Are you sure you want to deactivate/i)).toBeInTheDocument(),
    );

    const modal = getModalPanel("Confirm Deactivate");
    await user.click(within(modal).getByRole("button", { name: /Cancel/i }));
    await waitFor(() =>
      expect(screen.queryByText(/Are you sure you want to deactivate/i)).not.toBeInTheDocument(),
    );
  });

  it("Deactivate confirm calls DELETE /users/:id and invalidates list", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleUsers });
    vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 200 });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Deactivate Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Confirm$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Confirm$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.delete as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith("/users/1"),
    );
  });
});

// ─── AddUserModal ─────────────────────────────────────────────────────────────

describe("AddUserModal", () => {
  async function openAddModal() {
    vi.mocked(api.get).mockResolvedValue({ data: [] });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add User/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add User/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add User" })).toBeInTheDocument(),
    );
    return user;
  }

  it("shows validation errors for empty required fields", async () => {
    const user = await openAddModal();
    await user.click(screen.getByRole("button", { name: /^Save$/i }));
    await waitFor(() =>
      expect(screen.getByText(/Name is required/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/Email is required/i)).toBeInTheDocument();
    expect(screen.getByText(/Password must be at least 8 characters/i)).toBeInTheDocument();
    expect(screen.getByText(/Role is required/i)).toBeInTheDocument();
  });

  it("submits correct payload (with branchId: null) and closes modal on success", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: {
        id: 10,
        name: "Charlie",
        email: "charlie@example.com",
        role: "BomCreator",
        branchId: null,
        branchName: null,
        isActive: true,
      },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/^Name$/i), "Charlie");
    await user.type(screen.getByLabelText(/^Email$/i), "charlie@example.com");
    await user.type(screen.getByLabelText(/^Password$/i), "password123");
    await user.selectOptions(screen.getByLabelText(/^Role$/i), "BomCreator");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add User" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.post)).toHaveBeenCalledWith(
      "/users",
      expect.objectContaining({
        name: "Charlie",
        email: "charlie@example.com",
        password: "password123",
        role: "BomCreator",
        branchId: null,
      }),
    );
  });

  it("shows server error on failure", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Email already exists" } },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/^Name$/i), "Duplicate");
    await user.type(screen.getByLabelText(/^Email$/i), "dup@example.com");
    await user.type(screen.getByLabelText(/^Password$/i), "password123");
    await user.selectOptions(screen.getByLabelText(/^Role$/i), "Admin");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Email already exists/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("heading", { name: "Add User" })).toBeInTheDocument();
  });

  it("resets form and closes modal when Cancel is clicked", async () => {
    const user = await openAddModal();
    await user.type(screen.getByLabelText(/^Name$/i), "TYPED");
    await user.click(screen.getByRole("button", { name: /Cancel/i }));
    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add User" })).not.toBeInTheDocument(),
    );
  });
});

// ─── EditUserModal ────────────────────────────────────────────────────────────

describe("EditUserModal", () => {
  async function openEditModal() {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleUsers });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    return user;
  }

  it("pre-populates fields from row data", async () => {
    await openEditModal();
    expect(screen.getByDisplayValue("Alice Admin")).toBeInTheDocument();
    expect(screen.getByDisplayValue("alice@example.com")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Admin")).toBeInTheDocument();
    // isActive checkbox checked
    expect(screen.getByLabelText(/Is Active/i)).toBeChecked();
  });

  it("submits correct payload and closes modal on success", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleUsers });
    vi.mocked(api.put as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204 });
    const user = await openEditModal();

    const nameInput = screen.getByDisplayValue("Alice Admin");
    await user.clear(nameInput);
    await user.type(nameInput, "Alice Updated");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Edit User" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
      "/users/1",
      expect.objectContaining({
        name: "Alice Updated",
        email: "alice@example.com",
        role: "Admin",
        branchId: null,
        isActive: true,
      }),
    );
  });
});
