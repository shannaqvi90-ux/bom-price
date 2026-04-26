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

function mockUsersGet(users: typeof sampleUsers) {
  vi.mocked(api.get).mockImplementation((url: string) => {
    if (url === "/users") return Promise.resolve({ data: users });
    if (url === "/branches") return Promise.resolve({ data: [] });
    if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
    if (/\/users\/\d+\/branches/.test(url)) return Promise.resolve({ data: [] });
    return Promise.resolve({ data: [] });
  });
}

describe("UsersPage", () => {
  it("renders user rows (name, email, role, active badge)", async () => {
    mockUsersGet(sampleUsers);
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
    mockUsersGet(sampleUsers);
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
    mockUsersGet(sampleUsers);
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
    mockUsersGet(sampleUsers);
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
    mockUsersGet(sampleUsers);
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

  it("shows error message when Deactivate fails and keeps dialog open", async () => {
    mockUsersGet(sampleUsers);
    vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Cannot deactivate last admin" } },
    });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Deactivate Alice Admin/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Confirm$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Confirm$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Cannot deactivate last admin/i)).toBeInTheDocument(),
    );
    // Dialog stays open
    expect(screen.getByText(/Are you sure you want to deactivate/i)).toBeInTheDocument();
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

  it("submits correct payload (branch-less role) and closes modal on success", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: {
        id: 10,
        name: "Charlie",
        email: "charlie@example.com",
        role: "Admin",
        branchId: null,
        branchName: null,
        isActive: true,
      },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/^Name$/i), "Charlie");
    await user.type(screen.getByLabelText(/^Email$/i), "charlie@example.com");
    await user.type(screen.getByLabelText(/^Password$/i), "password123");
    // Admin is branch-less: no branch selector should appear, payload.branchId = null.
    await user.selectOptions(screen.getByLabelText(/^Role$/i), "Admin");
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
        role: "Admin",
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
    mockUsersGet(sampleUsers);
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
    mockUsersGet(sampleUsers);
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

// ─── Branch column ────────────────────────────────────────────────────────────

const sampleBranches = [
  { id: 1, name: "Dubai Main", isActive: true },
  { id: 2, name: "Abu Dhabi", isActive: true },
];

const usersWithBranch = [
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
    id: 3,
    name: "Sara Sales",
    email: "sara@example.com",
    role: "SalesPerson",
    branchId: 1,
    branchName: "Dubai Main",
    isActive: true,
  },
  {
    id: 4,
    name: "Ana Accountant",
    email: "ana@example.com",
    role: "Accountant",
    branchId: null,
    branchName: null,
    isActive: true,
  },
];

describe("Branch column", () => {
  it("shows dash for Admin with null branchId", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: usersWithBranch });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (/\/users\/\d+\/branches/.test(url)) return Promise.resolve({ data: [] });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Alice Admin")).toBeInTheDocument());
    // Admin row should have at least one em-dash for branch column
    const dashes = screen.getAllByText("—");
    expect(dashes.length).toBeGreaterThanOrEqual(1);
  });

  it("shows branch name for SalesPerson with branchId", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: usersWithBranch });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (/\/users\/\d+\/branches/.test(url)) return Promise.resolve({ data: [] });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Sara Sales")).toBeInTheDocument());
    expect(screen.getByText("Dubai Main")).toBeInTheDocument();
  });

  it("shows comma-separated branch names for Accountant row", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: usersWithBranch });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (url === "/users/4/branches") return Promise.resolve({ data: [1, 2] });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Ana Accountant")).toBeInTheDocument());
    await waitFor(() =>
      expect(screen.getByText("Dubai Main, Abu Dhabi")).toBeInTheDocument(),
    );
  });
});

// ─── Group column ─────────────────────────────────────────────────────────────

const spUser = {
  id: 3,
  name: "Sara Sales",
  email: "sara@example.com",
  role: "SalesPerson",
  branchId: 1,
  branchName: "Dubai Main",
  isActive: true,
};

describe("Group column", () => {
  it("renders Group column header", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: [spUser] });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Sara Sales")).toBeInTheDocument());
    expect(screen.getByText("Group")).toBeInTheDocument();
  });

  it("shows group name for SP row when assigned", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: [spUser] });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (url === "/users/3/group") return Promise.resolve({ data: { groupId: 1, groupName: "North Team" } });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Sara Sales")).toBeInTheDocument());
    await waitFor(() => expect(screen.getByText("North Team")).toBeInTheDocument());
  });
});

// ─── EditUserModal — Accountant multi-branch ──────────────────────────────────

describe("EditUserModal — Accountant multi-branch", () => {
  const accountantUser = {
    id: 4,
    name: "Ana Accountant",
    email: "ana@example.com",
    role: "Accountant",
    branchId: null,
    branchName: null,
    isActive: true,
  };

  it("renders branch checkboxes when editing an Accountant", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: [accountantUser] });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (url === "/users/4/branches") return Promise.resolve({ data: [1] });
      return Promise.resolve({ data: [] });
    });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Ana Accountant")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Ana Accountant/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText("Dubai Main")).toBeInTheDocument();
    expect(screen.getByLabelText("Abu Dhabi")).toBeInTheDocument();
  });

  it("pre-checks existing branch assignments", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: [accountantUser] });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (url === "/users/4/branches") return Promise.resolve({ data: [1] });
      return Promise.resolve({ data: [] });
    });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Ana Accountant")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Ana Accountant/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    await waitFor(() => expect(screen.getByLabelText("Dubai Main")).toBeChecked());
    expect(screen.getByLabelText("Abu Dhabi")).not.toBeChecked();
  });

  it("calls PUT /users/:id and PUT /users/:id/branches on save", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: [accountantUser] });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (url === "/users/4/branches") return Promise.resolve({ data: [1] });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    vi.mocked(api.put as ReturnType<typeof vi.fn>).mockResolvedValue({ status: 204 });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Ana Accountant")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Ana Accountant/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    // Check Abu Dhabi (id=2) in addition to the pre-checked Dubai Main (id=1)
    await waitFor(() => expect(screen.getByLabelText("Dubai Main")).toBeChecked());
    await user.click(screen.getByLabelText("Abu Dhabi"));
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
        "/users/4",
        expect.objectContaining({ role: "Accountant" }),
      ),
    );
    await waitFor(() =>
      expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
        "/users/4/branches",
        expect.objectContaining({ branchIds: expect.arrayContaining([1, 2]) }),
      ),
    );
  });
});

// ─── EditUserModal — SalesPerson group ───────────────────────────────────────

const sampleGroups = [
  { id: 1, name: "North Team", isActive: true },
  { id: 2, name: "South Team", isActive: true },
];

const spEditUser = {
  id: 3,
  name: "Sara Sales",
  email: "sara@example.com",
  role: "SalesPerson",
  branchId: 1,
  branchName: "Dubai Main",
  isActive: true,
};

const nonSpUser = {
  id: 5,
  name: "Bob Bom",
  email: "bob@example.com",
  role: "BomCreator",
  branchId: 1,
  branchName: "Dubai Main",
  isActive: true,
};

describe("EditUserModal — SalesPerson group", () => {
  it("Group dropdown is visible only when role is SalesPerson", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: [spEditUser] });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (url === "/groups") return Promise.resolve({ data: sampleGroups });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Sara Sales")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Sara Sales/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/Group/i)).toBeInTheDocument();
  });

  it("Group dropdown is hidden for non-SalesPerson roles", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: [nonSpUser] });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (url === "/groups") return Promise.resolve({ data: sampleGroups });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Bob Bom")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Bob Bom/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    expect(screen.queryByLabelText(/^Group$/i)).not.toBeInTheDocument();
  });

  it("Save calls PUT /users/:id and PUT /users/:id/group with selected groupId", async () => {
    vi.mocked(api.get).mockImplementation((url: string) => {
      if (url === "/users") return Promise.resolve({ data: [spEditUser] });
      if (url === "/branches") return Promise.resolve({ data: sampleBranches });
      if (url === "/groups") return Promise.resolve({ data: sampleGroups });
      if (/\/users\/\d+\/group/.test(url)) return Promise.resolve({ data: { groupId: null, groupName: null } });
      return Promise.resolve({ data: [] });
    });
    vi.mocked(api.put as ReturnType<typeof vi.fn>).mockResolvedValue({ status: 204 });
    const user = userEvent.setup();
    wrap(<UsersPage />);
    await waitFor(() => expect(screen.getByText("Sara Sales")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Sara Sales/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit User" })).toBeInTheDocument(),
    );
    // Select group North Team (id=1)
    await waitFor(() => expect(screen.getByLabelText(/Group/i)).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText(/Group/i), "1");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
        "/users/3",
        expect.objectContaining({ role: "SalesPerson" }),
      ),
    );
    await waitFor(() =>
      expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
        "/users/3/group",
        expect.objectContaining({ groupId: 1 }),
      ),
    );
  });
});
