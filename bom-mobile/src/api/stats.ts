import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { AccountantDashboardV3Stats } from "@/types/v3";

export function useAccountantDashboardV3() {
  return useQuery({
    queryKey: ["stats", "accountantDashboardV3"],
    queryFn: async () => {
      const res = await api.get<AccountantDashboardV3Stats>("/api/stats/accountant-dashboard");
      return res.data;
    },
    staleTime: 30_000,
  });
}

export interface MdDashboardCounts {
  toPrice: number;
  toSign: number;
  inFlight: number;
  signedToday: number;
}

export function useMdDashboard() {
  return useQuery({
    queryKey: ["stats", "v3-dashboard", "md"] as const,
    queryFn: () =>
      api.get<MdDashboardCounts>("/api/stats/v3-dashboard").then((r) => r.data),
    staleTime: 30_000,
  });
}
