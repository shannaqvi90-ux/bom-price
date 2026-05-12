import { act, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import LoginPage from "./LoginPage";
import { useLogin } from "./authApi";
import { useAuthStore } from "@/store/authStore";

vi.mock("./authApi", () => ({
  useLogin: vi.fn(),
}));

const mockUseLogin = vi.mocked(useLogin);

interface LoginMockState {
  error?: unknown;
  isPending?: boolean;
}

function mockLoginState({ error, isPending = false }: LoginMockState) {
  // Cast through unknown — the full UseMutationResult is unwieldy and the
  // component only consumes a small surface.
  mockUseLogin.mockReturnValue({
    error: error ?? null,
    isPending,
    isError: !!error,
    mutateAsync: vi.fn(),
    reset: vi.fn(),
  } as unknown as ReturnType<typeof useLogin>);
}

function renderPage() {
  return render(
    <MemoryRouter>
      <LoginPage />
    </MemoryRouter>,
  );
}

describe("LoginPage", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    // Ensure no leftover authed session from prior tests
    useAuthStore.getState().logout();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it("renders the sign-in form by default", () => {
    mockLoginState({});
    renderPage();

    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByLabelText("Password")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /sign in/i })).toBeEnabled();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows generic 'Invalid credentials' on 401 without attemptsRemaining", () => {
    mockLoginState({
      error: {
        response: {
          status: 401,
          data: { message: "Invalid credentials" },
        },
      },
    });
    renderPage();

    expect(screen.getByText("Invalid credentials")).toBeInTheDocument();
    // No amber warning chip (attemptsRemaining is absent)
    expect(screen.queryByText(/attempts remaining/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/attempt remaining/i)).not.toBeInTheDocument();
  });

  it("shows amber warning chip when attemptsRemaining is 2", () => {
    mockLoginState({
      error: {
        response: {
          status: 401,
          data: { message: "Invalid credentials", attemptsRemaining: 2 },
        },
      },
    });
    renderPage();

    const chip = screen.getByRole("alert");
    expect(chip).toHaveTextContent(/2 attempts remaining/i);
  });

  it("uses singular grammar when attemptsRemaining is 1", () => {
    mockLoginState({
      error: {
        response: {
          status: 401,
          data: { message: "Invalid credentials", attemptsRemaining: 1 },
        },
      },
    });
    renderPage();

    const chip = screen.getByRole("alert");
    expect(chip).toHaveTextContent(/1 attempt remaining/i);
    // Not "1 attempts" (plural)
    expect(chip).not.toHaveTextContent(/1 attempts remaining/i);
  });

  it("renders the lockout banner with mm:ss countdown when 400 ProblemDetails received", () => {
    mockLoginState({
      error: {
        response: {
          status: 400,
          data: {
            detail: "Account temporarily locked due to too many failed login attempts.",
            errors: { Email: ["Account locked."] },
            lockoutSecondsRemaining: 905, // 15:05
          },
        },
      },
    });
    renderPage();

    const banner = screen.getByRole("alert");
    expect(banner).toHaveTextContent(/account locked/i);
    expect(banner).toHaveTextContent(/try again in 15:05/i);
    expect(banner).toHaveTextContent(/contact your administrator/i);
    expect(screen.getByRole("button", { name: /sign in/i })).toBeDisabled();
  });

  it("countdown decrements every second and banner clears when timer reaches 0", () => {
    mockLoginState({
      error: {
        response: {
          status: 400,
          data: {
            detail: "Account locked.",
            errors: { Email: ["Account locked."] },
            lockoutSecondsRemaining: 3,
          },
        },
      },
    });
    renderPage();

    expect(screen.getByText(/try again in 00:03/i)).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(screen.getByText(/try again in 00:02/i)).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(2000);
    });
    // After 3 total seconds, the countdown has hit 0. The component calls
    // login.reset() in an effect — our mock doesn't actually clear the error,
    // but the banner is conditioned on !countdown.isExpired, so it unmounts.
    expect(screen.queryByText(/try again in/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /sign in/i })).toBeEnabled();
  });
});
