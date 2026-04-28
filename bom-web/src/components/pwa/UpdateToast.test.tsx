import { describe, it, expect, vi, beforeEach } from "vitest";
import { render } from "@testing-library/react";
import { UpdateToast } from "./UpdateToast";

vi.mock("sonner", () => ({ toast: vi.fn() }));
vi.mock("@/hooks/useServiceWorker", () => ({ useServiceWorker: vi.fn() }));

import { toast } from "sonner";
import { useServiceWorker } from "@/hooks/useServiceWorker";

beforeEach(() => {
  vi.clearAllMocks();
});

describe("UpdateToast", () => {
  it("does NOT show toast when updateAvailable=false", () => {
    vi.mocked(useServiceWorker).mockReturnValue({
      updateAvailable: false,
      applyUpdate: vi.fn(),
    });
    render(<UpdateToast />);
    expect(toast).not.toHaveBeenCalled();
  });

  it("shows toast when updateAvailable=true", () => {
    vi.mocked(useServiceWorker).mockReturnValue({
      updateAvailable: true,
      applyUpdate: vi.fn(),
    });
    render(<UpdateToast />);
    expect(toast).toHaveBeenCalledWith(
      "Naya version available",
      expect.objectContaining({
        action: expect.objectContaining({ label: "Refresh now" }),
      })
    );
  });
});
