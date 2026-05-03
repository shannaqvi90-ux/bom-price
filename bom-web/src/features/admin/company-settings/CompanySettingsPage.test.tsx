import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { CompanySettingsPage } from "./CompanySettingsPage";

vi.mock("@/api/axios", () => {
  const get = vi.fn();
  const put = vi.fn();
  return { api: { get, put } };
});

vi.mock("sonner", () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import { api } from "@/api/axios";

const seedSettings = {
  companyName: "FUJAIRAH PLASTIC FACTORY",
  address: "Fujairah, UAE",
  telephone: "",
  trn: "",
  email: "info@fpf.com",
  website: "",
  quotationValidityDays: 30,
  termsAndConditions: "Line one\nLine two",
  updatedAt: new Date("2026-05-03T00:00:00Z").toISOString(),
  updatedByName: null,
};

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <CompanySettingsPage />
    </QueryClientProvider>
  );
}

describe("CompanySettingsPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("loads and displays seeded settings", async () => {
    (api.get as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ data: seedSettings });

    renderPage();

    await waitFor(() => {
      expect(screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY")).toBeInTheDocument();
    });
    expect(screen.getByDisplayValue("30")).toBeInTheDocument();
    expect(screen.getByText("Last updated", { exact: false })).toBeInTheDocument();
  });

  it("submits PUT with form values on Save", async () => {
    (api.get as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ data: seedSettings });
    (api.put as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: { ...seedSettings, companyName: "FPF UPDATED", updatedByName: "Admin" },
    });

    renderPage();
    await waitFor(() => screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY"));

    const nameInput = screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY");
    fireEvent.change(nameInput, { target: { value: "FPF UPDATED" } });

    const reasonInput = screen.getByPlaceholderText(/Updated TRN/);
    fireEvent.change(reasonInput, { target: { value: "Test save reason" } });

    fireEvent.click(screen.getByRole("button", { name: /Save Changes/ }));

    await waitFor(() => {
      expect(api.put).toHaveBeenCalledWith(
        "/admin/company-settings",
        expect.objectContaining({
          companyName: "FPF UPDATED",
          reason: "Test save reason",
        })
      );
    });
  });

  it("Discard Changes reverts unsaved edits", async () => {
    (api.get as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ data: seedSettings });

    renderPage();
    await waitFor(() => screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY"));

    const nameInput = screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY");
    fireEvent.change(nameInput, { target: { value: "STALE EDIT" } });
    expect(screen.getByDisplayValue("STALE EDIT")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: /Discard Changes/ }));

    expect(screen.getByDisplayValue("FUJAIRAH PLASTIC FACTORY")).toBeInTheDocument();
  });
});
