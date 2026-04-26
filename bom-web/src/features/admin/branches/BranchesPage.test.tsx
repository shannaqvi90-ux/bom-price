import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";
import BranchesPage from "./BranchesPage";
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

const sampleBranches = [
  { id: 1, name: "Dubai Main", isActive: true },
  { id: 2, name: "Abu Dhabi", isActive: false },
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

describe("BranchesPage", () => {
  it("renders branch rows (name, status badges)", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleBranches });
    wrap(<BranchesPage />);
    await waitFor(() => expect(screen.getByText("Dubai Main")).toBeInTheDocument());
    expect(screen.getByText("Abu Dhabi")).toBeInTheDocument();
    expect(screen.getByText("Active")).toBeInTheDocument();
    expect(screen.getByText("Inactive")).toBeInTheDocument();
  });

  it("shows empty state when no branches returned", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    wrap(<BranchesPage />);
    await waitFor(() =>
      expect(screen.getByText(/No branches found/i)).toBeInTheDocument(),
    );
  });

  it("shows error state with Retry button on API failure", async () => {
    vi.mocked(api.get).mockRejectedValueOnce(new Error("Network error"));
    wrap(<BranchesPage />);
    await waitFor(() =>
      expect(screen.getByText(/Failed to load branches/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("button", { name: /Retry/i })).toBeInTheDocument();
  });

  it("opens AddBranchModal on Add Branch click", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [] });
    const user = userEvent.setup();
    wrap(<BranchesPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Branch/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add Branch/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add Branch" })).toBeInTheDocument(),
    );
    expect(screen.getByLabelText(/Branch Name/i)).toBeInTheDocument();
  });

  it("opens EditBranchModal pre-populated when Edit clicked", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleBranches });
    const user = userEvent.setup();
    wrap(<BranchesPage />);
    await waitFor(() => expect(screen.getByText("Dubai Main")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Dubai Main/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit Branch" })).toBeInTheDocument(),
    );
    expect(screen.getByDisplayValue("Dubai Main")).toBeInTheDocument();
  });

  it("Delete shows confirmation dialog; cancel dismisses", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleBranches });
    const user = userEvent.setup();
    wrap(<BranchesPage />);
    await waitFor(() => expect(screen.getByText("Dubai Main")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Delete Dubai Main/i }));
    await waitFor(() =>
      expect(screen.getByText(/Are you sure you want to delete/i)).toBeInTheDocument(),
    );

    const modal = getDialogPanel("Confirm Delete");
    await user.click(within(modal).getByRole("button", { name: /Cancel/i }));
    await waitFor(() =>
      expect(screen.queryByText(/Are you sure you want to delete/i)).not.toBeInTheDocument(),
    );
  });

  it("Delete confirm calls DELETE /branches/:id and invalidates list", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleBranches });
    vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204 });
    const user = userEvent.setup();
    wrap(<BranchesPage />);
    await waitFor(() => expect(screen.getByText("Dubai Main")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Delete Dubai Main/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Confirm$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Confirm$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.delete as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith("/branches/1"),
    );
  });

  it("shows server error on Delete conflict (branch in use)", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleBranches });
    vi.mocked(api.delete as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Branch Dubai Main is in use and cannot be deleted." } },
    });
    const user = userEvent.setup();
    wrap(<BranchesPage />);
    await waitFor(() => expect(screen.getByText("Dubai Main")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Delete Dubai Main/i }));
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /^Confirm$/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /^Confirm$/i }));

    await waitFor(() =>
      expect(
        screen.getByText(/Branch Dubai Main is in use and cannot be deleted/i),
      ).toBeInTheDocument(),
    );
    // Dialog stays open
    expect(screen.getByText(/Are you sure you want to delete/i)).toBeInTheDocument();
  });
});

describe("AddBranchModal", () => {
  async function openAddModal() {
    vi.mocked(api.get).mockResolvedValue({ data: [] });
    const user = userEvent.setup();
    wrap(<BranchesPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Branch/i })).toBeInTheDocument(),
    );
    await user.click(screen.getByRole("button", { name: /Add Branch/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Add Branch" })).toBeInTheDocument(),
    );
    return user;
  }

  it("shows validation error for empty name", async () => {
    const user = await openAddModal();
    await user.click(screen.getByRole("button", { name: /^Save$/i }));
    await waitFor(() =>
      expect(screen.getByText(/Branch name is required/i)).toBeInTheDocument(),
    );
  });

  it("submits POST /branches with correct name and closes on success", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: { id: 3, name: "Sharjah", isActive: true },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/Branch Name/i), "Sharjah");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add Branch" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.post as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
      "/branches",
      expect.objectContaining({ name: "Sharjah" }),
    );
  });

  it("shows server error on POST failure", async () => {
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockRejectedValueOnce({
      response: { data: { message: "Branch already exists" } },
    });
    const user = await openAddModal();

    await user.type(screen.getByLabelText(/Branch Name/i), "Duplicate");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.getByText(/Branch already exists/i)).toBeInTheDocument(),
    );
    expect(screen.getByRole("heading", { name: "Add Branch" })).toBeInTheDocument();
  });

  it("Cancel closes the modal", async () => {
    const user = await openAddModal();
    await user.click(screen.getByRole("button", { name: /Cancel/i }));
    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Add Branch" })).not.toBeInTheDocument(),
    );
  });
});

describe("EditBranchModal", () => {
  async function openEditModal() {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleBranches });
    const user = userEvent.setup();
    wrap(<BranchesPage />);
    await waitFor(() => expect(screen.getByText("Dubai Main")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Edit Dubai Main/i }));
    await waitFor(() =>
      expect(screen.getByRole("heading", { name: "Edit Branch" })).toBeInTheDocument(),
    );
    return user;
  }

  it("pre-populates fields from row data", async () => {
    await openEditModal();
    expect(screen.getByDisplayValue("Dubai Main")).toBeInTheDocument();
    expect(screen.getByLabelText(/Is Active/i)).toBeChecked();
  });

  it("submits PUT /branches/:id with correct payload and closes on success", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleBranches });
    vi.mocked(api.put as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204 });
    const user = await openEditModal();

    const nameInput = screen.getByDisplayValue("Dubai Main");
    await user.clear(nameInput);
    await user.type(nameInput, "Dubai Updated");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(screen.queryByRole("heading", { name: "Edit Branch" })).not.toBeInTheDocument(),
    );
    expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
      "/branches/1",
      expect.objectContaining({ name: "Dubai Updated", isActive: true }),
    );
  });
});
