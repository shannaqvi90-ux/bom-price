import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "./client";
import type { Customer, CreateCustomerRequest } from "@/types/api";

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCustomerRequest) =>
      api.post<Customer>("/api/customers", body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ["customers"] });
    },
  });
}
