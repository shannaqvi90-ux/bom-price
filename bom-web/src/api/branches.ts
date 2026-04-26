import { useQuery } from "@tanstack/react-query";
import { api } from "@/api/axios";

export interface Branch {
  id: number;
  name: string;
  isActive: boolean;
}

export const branchKeys = {
  all: ["branches"] as const,
  list: () => [...branchKeys.all, "list"] as const,
};

export function useBranches() {
  return useQuery({
    queryKey: branchKeys.list(),
    queryFn: async () => (await api.get<Branch[]>("/branches")).data,
    staleTime: 5 * 60_000,
  });
}
