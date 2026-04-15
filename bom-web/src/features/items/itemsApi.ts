import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type {
  CreateItemRequest,
  ImportResult,
  Item,
  LedgerHeadersResponse,
  LedgerImportResult,
  UpdateItemRequest,
} from "@/types/api";

export const itemKeys = {
  all: ["items"] as const,
  list: () => [...itemKeys.all, "list"] as const,
};

export function useItems() {
  return useQuery({
    queryKey: itemKeys.list(),
    queryFn: () =>
      api.get<Item[]>("/items", { params: { includeInactive: true } }).then((r) => r.data),
  });
}

export function useCreateItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: CreateItemRequest) =>
      api.post<Item>("/items", body).then((r) => r.data),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function useUpdateItem() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, data }: { id: number; data: UpdateItemRequest }) =>
      api.put(`/items/${id}`, data),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function useUpdateItemStatus() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, isActive }: { id: number; isActive: boolean }) =>
      api.patch(`/items/${id}/status`, { isActive }),
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function useImportItems() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ file, branchId }: { file: File; branchId: number }) => {
      const fd = new FormData();
      fd.append("file", file);
      fd.append("branchId", String(branchId));
      return api
        .post<ImportResult>("/items/import", fd, {
          headers: { "Content-Type": "multipart/form-data" },
        })
        .then((r) => r.data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}

export function downloadItemTemplate() {
  return api.get("/items/import/template", { responseType: "blob" }).then((r) => {
    const url = window.URL.createObjectURL(r.data as Blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "items-template.xlsx";
    a.click();
    window.URL.revokeObjectURL(url);
  });
}

export function useLedgerHeaders() {
  return useMutation({
    mutationFn: (file: File) => {
      const fd = new FormData();
      fd.append("file", file);
      return api
        .post<LedgerHeadersResponse>("/items/import/ledger/headers", fd, {
          headers: { "Content-Type": "multipart/form-data" },
        })
        .then((r) => r.data);
    },
  });
}

export function useLedgerImport() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (args: {
      file: File;
      itemCodeColumn: string;
      dateColumn: string;
      unitPriceColumn: string;
      branchId: number;
    }) => {
      const fd = new FormData();
      fd.append("file", args.file);
      fd.append("itemCodeColumn", args.itemCodeColumn);
      fd.append("dateColumn", args.dateColumn);
      fd.append("unitPriceColumn", args.unitPriceColumn);
      fd.append("branchId", String(args.branchId));
      return api
        .post<LedgerImportResult>("/items/import/ledger", fd, {
          headers: { "Content-Type": "multipart/form-data" },
        })
        .then((r) => r.data);
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: itemKeys.all }),
  });
}
