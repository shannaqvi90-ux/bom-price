import { useQuery } from "@tanstack/react-query";
import { api } from "./client";

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
