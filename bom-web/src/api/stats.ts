import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/axios";

export interface MdDashboardCounts {
  toPrice: number;
  toSign: number;
  inFlight: number;
  signedToday: number;
}

export function useMdDashboardCounts() {
  return useQuery({
    queryKey: ["stats", "v3-dashboard", "md"] as const,
    queryFn: () =>
      api.get<MdDashboardCounts>("/stats/v3-dashboard").then((r) => r.data),
    staleTime: 30_000,
  });
}
