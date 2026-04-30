// bom-mobile/src/api/items.ts
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { Item, ItemKind } from "@/types/api";

export interface CreateItemPayload {
  description: string;
  type: ItemKind;
  lastPurchasePrice?: number | null;
}

export function useCreateItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateItemPayload) =>
      api.post<Item>("/api/items", payload).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["items"] }),
  });
}
