import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import { InstallModal } from "./InstallModal";

vi.mock("@/hooks/usePwaInstall", () => ({
  usePwaInstall: vi.fn(),
}));

import { usePwaInstall } from "@/hooks/usePwaInstall";

describe("InstallModal", () => {
  it("renders when shouldShowIosModal=true", () => {
    vi.mocked(usePwaInstall).mockReturnValue({
      shouldShowIosModal: true,
      dismissIosModal: vi.fn(),
      canPromptInstall: false,
      isInstalled: false,
      promptInstall: vi.fn(),
    });
    render(<InstallModal />);
    expect(screen.getByText(/Install FPF Quotations/)).toBeInTheDocument();
    expect(screen.getByText(/Add to Home Screen/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /I'll do it later/ })).toBeInTheDocument();
  });

  it("renders nothing when shouldShowIosModal=false", () => {
    vi.mocked(usePwaInstall).mockReturnValue({
      shouldShowIosModal: false,
      dismissIosModal: vi.fn(),
      canPromptInstall: false,
      isInstalled: true,
      promptInstall: vi.fn(),
    });
    const { container } = render(<InstallModal />);
    expect(container.firstChild).toBeNull();
  });
});
