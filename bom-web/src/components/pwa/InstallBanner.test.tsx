import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { InstallBanner } from "./InstallBanner";

vi.mock("@/hooks/usePwaInstall", () => ({
  usePwaInstall: vi.fn(),
}));

import { usePwaInstall } from "@/hooks/usePwaInstall";

describe("InstallBanner", () => {
  it("renders when canPromptInstall=true", () => {
    vi.mocked(usePwaInstall).mockReturnValue({
      canPromptInstall: true,
      shouldShowIosModal: false,
      dismissIosModal: vi.fn(),
      isInstalled: false,
      promptInstall: vi.fn(),
    });
    render(<InstallBanner />);
    expect(screen.getByText(/Install FPF Quotations/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /Install/i })).toBeInTheDocument();
  });

  it("renders nothing when canPromptInstall=false", () => {
    vi.mocked(usePwaInstall).mockReturnValue({
      canPromptInstall: false,
      shouldShowIosModal: false,
      dismissIosModal: vi.fn(),
      isInstalled: true,
      promptInstall: vi.fn(),
    });
    const { container } = render(<InstallBanner />);
    expect(container.firstChild).toBeNull();
  });

  it("hides after Dismiss clicked", () => {
    vi.mocked(usePwaInstall).mockReturnValue({
      canPromptInstall: true,
      shouldShowIosModal: false,
      dismissIosModal: vi.fn(),
      isInstalled: false,
      promptInstall: vi.fn(),
    });
    render(<InstallBanner />);
    fireEvent.click(screen.getByRole("button", { name: /Dismiss/i }));
    expect(screen.queryByText(/Install FPF Quotations/)).not.toBeInTheDocument();
  });
});
