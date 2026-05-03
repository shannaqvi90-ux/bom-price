import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";

export interface CompanySettings {
  companyName: string;
  address: string;
  telephone: string;
  trn: string;
  email: string;
  website: string;
  quotationValidityDays: number;
  termsAndConditions: string;
  updatedAt: string;
  updatedByName: string | null;
}

export interface UpdateCompanySettingsRequest {
  companyName: string;
  address: string;
  telephone: string;
  trn: string;
  email: string;
  website: string;
  quotationValidityDays: number;
  termsAndConditions: string;
  reason: string;
}

const QUERY_KEY = ["admin", "company-settings"] as const;

export function useCompanySettings() {
  return useQuery({
    queryKey: QUERY_KEY,
    queryFn: async () => {
      const { data } = await api.get<CompanySettings>("/admin/company-settings");
      return data;
    },
  });
}

export function useUpdateCompanySettings() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (body: UpdateCompanySettingsRequest) => {
      const { data } = await api.put<CompanySettings>("/admin/company-settings", body);
      return data;
    },
    onSuccess: (data) => {
      qc.setQueryData(QUERY_KEY, data);
    },
  });
}
