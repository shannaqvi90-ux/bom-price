import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";

export interface CustomerLite {
  id: number; code: string; name: string;
  email?: string | null; phone?: string | null; address?: string | null;
  isDeleted: boolean;
}

export interface CreateCustomerPayload {
  name: string; email?: string; phone?: string; address?: string;
}

export const customerKeys = {
  all: ["customers"] as const,
  list: (search?: string) => [...customerKeys.all, "list", search] as const,
};

export function useCustomers(search?: string) {
  return useQuery({
    queryKey: customerKeys.list(search),
    queryFn: () => api.get<CustomerLite[]>("/api/customers", {
      params: search ? { search } : undefined,
    }).then((r) => r.data.filter((c) => !c.isDeleted)),
    staleTime: 30_000,
  });
}

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (payload: CreateCustomerPayload) =>
      api.post<CustomerLite>("/api/customers", payload).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: customerKeys.all }),
  });
}
