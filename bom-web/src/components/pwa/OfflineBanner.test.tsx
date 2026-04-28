import { describe, it, expect, beforeEach } from "vitest";
import { render, screen, act } from "@testing-library/react";
import { OfflineBanner } from "./OfflineBanner";

const setOnline = (online: boolean) => {
  Object.defineProperty(navigator, "onLine", { value: online, configurable: true });
};

beforeEach(() => {
  setOnline(true);
});

describe("OfflineBanner", () => {
  it("renders nothing when online", () => {
    setOnline(true);
    const { container } = render(<OfflineBanner />);
    expect(container.firstChild).toBeNull();
  });

  it("renders banner when offline at mount", () => {
    setOnline(false);
    render(<OfflineBanner />);
    expect(screen.getByText(/Offline/)).toBeInTheDocument();
  });

  it("hides banner on online event", () => {
    setOnline(false);
    render(<OfflineBanner />);
    expect(screen.getByText(/Offline/)).toBeInTheDocument();
    setOnline(true);
    act(() => {
      window.dispatchEvent(new Event("online"));
    });
    expect(screen.queryByText(/Offline/)).not.toBeInTheDocument();
  });

  it("shows banner on offline event after starting online", () => {
    setOnline(true);
    render(<OfflineBanner />);
    expect(screen.queryByText(/Offline/)).not.toBeInTheDocument();
    setOnline(false);
    act(() => {
      window.dispatchEvent(new Event("offline"));
    });
    expect(screen.getByText(/Offline/)).toBeInTheDocument();
  });
});
