import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { ProfileSignaturePage } from "./ProfileSignaturePage";
import { api } from "@/api/axios";

vi.mock("@/api/axios", () => ({ api: { get: vi.fn(), post: vi.fn() } }));

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ProfileSignaturePage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe("ProfileSignaturePage", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
    vi.mocked(api.post).mockReset();
  });

  it("uploads file via multipart and shows success", async () => {
    // No existing signature (404 from GET).
    vi.mocked(api.get).mockRejectedValue({ response: { status: 404 } });
    vi.mocked(api.post).mockResolvedValue({
      data: { path: "/data/signatures/5.png", uploadedAt: "2026-04-29T00:00:00Z" },
    });

    renderPage();

    const file = new File(["png-bytes"], "sig.png", { type: "image/png" });
    const input = screen.getByLabelText(/upload signature/i) as HTMLInputElement;
    await userEvent.upload(input, file);
    await userEvent.click(screen.getByRole("button", { name: /upload/i }));

    await waitFor(() =>
      expect(api.post).toHaveBeenCalledWith(
        "/profile/signature",
        expect.any(FormData),
        expect.objectContaining({
          headers: expect.objectContaining({ "Content-Type": "multipart/form-data" }),
        }),
      ),
    );
  });
});
