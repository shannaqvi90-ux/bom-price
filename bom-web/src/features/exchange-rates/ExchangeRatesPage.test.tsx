import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, waitFor, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuthStore } from "@/store/authStore";
import ExchangeRatesPage from "./ExchangeRatesPage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({
  api: { get: vi.fn(), post: vi.fn(), put: vi.fn() },
}));

function wrap(ui: ReactNode) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>{ui}</MemoryRouter>
    </QueryClientProvider>,
  );
}

const sampleRates = [
  {
    id: 1,
    currencyCode: "USD",
    currencyName: "US Dollar",
    rateToAed: 3.6725,
    effectiveDate: "2026-04-01T00:00:00Z",
    isActive: true,
    setByName: "Alice",
  },
];

beforeEach(() => {
  vi.mocked(api.get).mockReset();
  vi.mocked(api.post as ReturnType<typeof vi.fn>).mockReset();
  vi.mocked(api.put as ReturnType<typeof vi.fn>).mockReset();
  useAuthStore.getState().setSession({
    accessToken: "at",
    refreshToken: "rt",
    role: "Accountant",
    userId: 1,
    name: "Alice",
    branchId: null,
  });
});

afterEach(() => {
  useAuthStore.getState().logout();
});

describe("ExchangeRatesPage", () => {
  it("renders rate rows from API response", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRates });
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());
    expect(screen.getByText("US Dollar")).toBeInTheDocument();
    expect(screen.getByText("3.6725")).toBeInTheDocument();
    expect(screen.getByText("Alice")).toBeInTheDocument();
  });

  it("shows Add Rate button for Accountant", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRates });
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());
    expect(screen.getByRole("button", { name: /Add Rate/i })).toBeInTheDocument();
  });

  it("hides Add Rate button for non-Accountant roles", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "BomCreator",
      userId: 2,
      name: "Bob",
      branchId: 1,
    });
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRates });
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /Add Rate/i })).not.toBeInTheDocument();
  });

  it("submitting Add Rate modal calls POST with correct payload", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [] });
    vi.mocked(api.post as ReturnType<typeof vi.fn>).mockResolvedValueOnce({
      data: {
        id: 2,
        currencyCode: "EUR",
        currencyName: "Euro",
        rateToAed: 3.98,
        effectiveDate: "2026-04-01T00:00:00Z",
        isActive: true,
        setByName: "Alice",
      },
    });
    const user = userEvent.setup();
    wrap(<ExchangeRatesPage />);
    await waitFor(() =>
      expect(screen.getByRole("button", { name: /Add Rate/i })).toBeInTheDocument(),
    );

    await user.click(screen.getByRole("button", { name: /Add Rate/i }));
    await user.type(screen.getByLabelText(/Currency Code/i), "EUR");
    await user.type(screen.getByLabelText(/Currency Name/i), "Euro");
    await user.clear(screen.getByLabelText(/Rate to AED/i));
    await user.type(screen.getByLabelText(/Rate to AED/i), "3.98");
    fireEvent.change(screen.getByLabelText(/Effective Date/i), {
      target: { value: "2026-04-01" },
    });
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.post as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
        "/exchange-rates",
        expect.objectContaining({
          currencyCode: "EUR",
          currencyName: "Euro",
          rateToAed: 3.98,
        }),
      ),
    );
  });

  it("submitting Edit Rate modal calls PUT with correct payload", async () => {
    vi.mocked(api.get).mockResolvedValue({ data: sampleRates });
    vi.mocked(api.put as ReturnType<typeof vi.fn>).mockResolvedValueOnce({ status: 204 });
    const user = userEvent.setup();
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /Edit USD/i }));
    const rateInput = screen.getByLabelText(/Rate to AED/i);
    await user.clear(rateInput);
    await user.type(rateInput, "3.75");
    await user.click(screen.getByRole("button", { name: /^Save$/i }));

    await waitFor(() =>
      expect(vi.mocked(api.put as ReturnType<typeof vi.fn>)).toHaveBeenCalledWith(
        "/exchange-rates/1",
        expect.objectContaining({ rateToAed: 3.75 }),
      ),
    );
  });

  it("hides edit actions for non-Accountant roles", async () => {
    useAuthStore.getState().setSession({
      accessToken: "at",
      refreshToken: "rt",
      role: "ManagingDirector",
      userId: 3,
      name: "MD",
      branchId: null,
    });
    vi.mocked(api.get).mockResolvedValueOnce({ data: sampleRates });
    wrap(<ExchangeRatesPage />);
    await waitFor(() => expect(screen.getByText("USD")).toBeInTheDocument());
    expect(screen.queryByRole("button", { name: /Edit USD/i })).not.toBeInTheDocument();
  });
});
