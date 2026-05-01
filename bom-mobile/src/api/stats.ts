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
