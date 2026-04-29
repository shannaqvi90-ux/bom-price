import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  CreateCustomerRequest,
  Customer,
  ImplicitItemResponse,
  ImportResult,
} from "@/types/api";

export const customerKeys = {
  all: ["customers"] as const,
  list: () => [...customerKeys.all, "list"] as const,
};

export function useCustomers() {
  return useQuery({
    queryKey: customerKeys.list(),
    queryFn: () => api.get<Customer[]>("/customers").then((r) => r.data),
  });
}

export function useCreateCustomer() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateCustomerRequest) =>
      api.post<Customer>("/customers", body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: customerKeys.all });
    },
  });
}

export function useImportCustomers() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (file: File) => {
      const form = new FormData();
      form.append("file", file);
      return api
        .post<ImportResult>("/customers/import", form, {
          headers: { "Content-Type": "multipart/form-data" },
        })
        .then((r) => r.data);
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: customerKeys.all });
    },
  });
}

// V3 — implicit items (i.e. items the customer has historically ordered, derived
// from past requisitions). Backend: GET /api/customers/{id}/items.
// Returns empty array when customerId is null/0 without calling the API.
export function useCustomerImplicitItems(customerId: number | null) {
  return useQuery({
    queryKey: ["customer", customerId, "implicit-items"],
    queryFn: () =>
      customerId
        ? api
            .get<ImplicitItemResponse[]>(`/customers/${customerId}/items`)
            .then((r) => r.data)
        : Promise.resolve([] as ImplicitItemResponse[]),
    enabled: !!customerId && customerId > 0,
  });
}
