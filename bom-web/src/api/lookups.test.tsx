import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

vi.mock("@/api/axios", () => {
  const get = vi.fn();
  return { api: { get } };
});

import { api } from "@/api/axios";
import { useCustomers, useItems, useActiveExchangeRates } from "./lookups";

function wrapper(client: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

function freshClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

describe("lookup hooks", () => {
  beforeEach(() => {
    vi.mocked(api.get).mockReset();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it("useCustomers fetches /customers", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [{ id: 1, name: "ACME" }] });
    const client = freshClient();
    const { result } = renderHook(() => useCustomers(), { wrapper: wrapper(client) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.get).toHaveBeenCalledWith("/customers");
    expect(result.current.data).toEqual([{ id: 1, name: "ACME" }]);
  });

  it("useItems fetches /items", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({ data: [{ id: 2, description: "Widget" }] });
    const client = freshClient();
    const { result } = renderHook(() => useItems(), { wrapper: wrapper(client) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.get).toHaveBeenCalledWith("/items");
    expect(result.current.data).toEqual([{ id: 2, description: "Widget" }]);
  });

  it("useActiveExchangeRates fetches /exchange-rates/active", async () => {
    vi.mocked(api.get).mockResolvedValueOnce({
      data: [{ id: 3, currencyCode: "USD", rateToAed: 3.67 }],
    });
    const client = freshClient();
    const { result } = renderHook(() => useActiveExchangeRates(), {
      wrapper: wrapper(client),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.get).toHaveBeenCalledWith("/exchange-rates/active");
  });
});
