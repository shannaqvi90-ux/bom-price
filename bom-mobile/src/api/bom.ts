import { useQuery } from "@tanstack/react-query";
import { api } from "./client";
import type { BomReviewResponse } from "@/types/api";

export const bomKeys = {
  review: (requisitionId: number) => ["bom", "review", requisitionId] as const,
};

export function useBomReview(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: bomKeys.review(requisitionId),
    queryFn: async () => {
      const res = await api.get<BomReviewResponse>(`/api/bom/${requisitionId}`);
      return res.data;
    },
    enabled: enabled && Number.isFinite(requisitionId) && requisitionId > 0,
    staleTime: 30_000,
    retry: false,
  });
}
