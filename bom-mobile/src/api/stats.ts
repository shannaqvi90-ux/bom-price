import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { AccountantDashboardStats } from "@/types/api";

interface CountResponse {
  count: number;
}

export function useMdPendingCount() {
  return useQuery({
    queryKey: ["md", "count", "mdReview"],
    queryFn: async () => {
      const res = await api.get<CountResponse>("/api/requisitions/count", {
        params: { status: "MdReview" },
      });
      return res.data.count;
    },
    staleTime: 30_000,
  });
}

export function useAccountantDashboardStats() {
  return useQuery({
    queryKey: ["stats", "accountantDashboard"],
    queryFn: async () => {
      const res = await api.get<AccountantDashboardStats>("/api/stats/accountant-dashboard");
      return res.data;
    },
    staleTime: 30_000,
  });
}
