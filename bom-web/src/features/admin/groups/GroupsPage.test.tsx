import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";
import GroupsPage from "./GroupsPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

const sampleGroups = [
  { id: 1, name: "North Sales Team", isActive: true },
  { id: 2, name: "South Sales Team", isActive: false },
];

function loginAsAdmin() {
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role: "Admin",
    userId: 1,
    name: "Test Admin",
    branchId: null,
  });
}

function loginAsAccountant() {
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role: "Accountant",
    userId: 2,
    name: "Test Accountant",
    branchId: null,
  });
}

function getDialogPanel(headingName: string): HTMLElement {
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

describe("GroupsPage", () => {
  it("renders group rows (name, status badges)", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleGroups });
    wrap(<GroupsPage />);
    await waitFor(() => expect(screen.getByText("North Sales Team")).toBeInTheDocument());
    expect(screen.getByText("South Sales Team")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows empty state when no groups returned", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    wrap(<GroupsPage />);
    await waitFor(() =>
      expect(screen.getByText(/No groups found/i)).toBeInTheDocument(),
    );
  });

  it("shows error state with Retry button on API failure", async () => {
    vi.mocked(api.get).mockRejectedValueOnce(new Error("Network error"));
    wrap(<GroupsPage />);
    await waitFor(() =>
      expect(screen.getByText(/Failed to load groups/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: /Retry/i })).toBeInTheDocument();
  });

  it("opens AddGroupModal on Add Group click", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    const user = userEvent.setup();
    wrap(<GroupsPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Group/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add Group/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add Group" })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/Group Name/i)).toBeInTheDocument();
  });

  it("opens EditGroupModal pre-populated when Edit clicked", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleGroups });
    const user = userEvent.setup();
    wrap(<GroupsPage />);
    await waitFor(() => expect(screen.getByText("North Sales Team")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit North Sales Team/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit Group" })).toBeInTheDocument(),
    );
    expect(screen.getByDisplayValue("North Sales Team")).toBeInTheDocument();
  });

  it("Delete shows confirmation dialog; cancel dismisses", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleGroups });
    const user = userEvent.setup();
    wrap(<GroupsPage />);
    await waitFor(() => expect(screen.getByText("North Sales Team")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Delete North Sales Team/i }));
    await waitFor(() =>
      expect(screen.getByText(/Are you sure you want to delete/i)).toBeInTheDocument(),
    );

    const modal = getDialogPanel("Confirm Delete");
    await user.click(within(modal).getByRole("button", { name: /Cancel/i }));
    await waitFor(() =>
      expect(screen.queryByText(/Are you sure you want to delete/i)).not.toBeInTheDocument(),
    );
  });

  it("Delete confirm calls DELETE /groups/:id and invalidates list", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleGroups });
    vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204 });
    const user = userEvent.setup();
    wrap(<GroupsPage />);
    await waitFor(() => expect(screen.getByText("North Sales Team")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Delete North Sales Team/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Confirm$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Confirm$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.delete as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith("/groups/1"),
    );
  });

  it("shows server error on Delete conflict (group in use)", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleGroups });
    vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Group North Sales Team is in use and cannot be deleted." } },
    });
    const user = userEvent.setup();
    wrap(<GroupsPage />);
    await waitFor(() => expect(screen.getByText("North Sales Team")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Delete North Sales Team/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Confirm$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Confirm$/i }));

    await waitFor(() =>
      expect(
        screen.getByText(/Group North Sales Team is in use and cannot be deleted/i),
      ).toBeInTheDocument(),
    );
    // Dialog stays open
    expect(screen.getByText(/Are you sure you want to delete/i)).toBeInTheDocument();
  });

  it("renders page for Accountant role", async () => {
    loginAsAccountant();
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleGroups });
    wrap(<GroupsPage />);
    await waitFor(() => expect(screen.getByText("North Sales Team")).toBeInTheDocument());
    expect(screen.getByText("Groups")).toBeInTheDocument();
  });
});

describe("AddGroupModal", () => {
  async function openAddModal() {
    vi.mocked(api.get).mockResolvedValue({ data: [] });
    const user = userEvent.setup();
    wrap(<GroupsPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Group/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add Group/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add Group" })).toBeInTheDocument(),
    );
    return user;
  }

  it("shows validation error for empty name", async () => {
    const user = await openAddModal();
    await user.click(screen.getByRole("button", { name: /^Save$/i }));
    await waitFor(() =>
      expect(screen.getByText(/Group name is required/i)).toBeInTheDocument(),
    );
  });

  it("submits POST /groups with correct name and closes on success", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: { id: 3, name: "East Team", isActive: true },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/Group Name/i), "East Team");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add Group" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.post as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
      "/groups",
      expect.objectContaining({ name: "East Team" }),
    );
  });

  it("shows server error on POST failure", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Group already exists" } },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/Group Name/i), "Duplicate");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Group already exists/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("heading", { name: "Add Group" })).toBeInTheDocument();
  });

  it("Cancel closes the modal", async () => {
    const user = await openAddModal();
    await user.click(screen.getByRole("button", { name: /Cancel/i }));
    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add Group" })).not.toBeInTheDocument(),
    );
  });
});

describe("EditGroupModal", () => {
  async function openEditModal() {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleGroups });
    const user = userEvent.setup();
    wrap(<GroupsPage />);
    await waitFor(() => expect(screen.getByText("North Sales Team")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit North Sales Team/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit Group" })).toBeInTheDocument(),
    );
    return user;
  }

  it("pre-populates fields from row data", async () => {
    await openEditModal();
    expect(screen.getByDisplayValue("North Sales Team")).toBeInTheDocument();
    expect(screen.getByLabelText(/Is Active/i)).toBeChecked();
  });

  it("submits PUT /groups/:id with correct payload and closes on success", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleGroups });
    vi.mocked(api.put as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204 });
    const user = await openEditModal();

    const nameInput = screen.getByDisplayValue("North Sales Team");
    await user.clear(nameInput);
    await user.type(nameInput, "North Updated");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Edit Group" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
      "/groups/1",
      expect.objectContaining({ name: "North Updated", isActive: true }),
    );
  });
});
