import { useQuery } from "@tanstack/react-query";
import { api } from "./client";

export interface SalesGroup {
  id: number;
  name: string;
  isActive: boolean;
}

export function useGroups() {
  return useQuery({
    queryKey: ["groups", "list"],
    queryFn: async () => (await api.get<SalesGroup[]>("/api/groups")).data,
    staleTime: 5 * 60_000,
  });
}
