import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";

export const profileKeys = {
  all: ["profile"] as const,
  signature: () => ["profile", "signature"] as const,
};

interface UploadResult {
  path: string;
  uploadedAt: string;
}

export function useUploadSignature() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async ({ uri, mime }: { uri: string; mime: string }) => {
      const formData = new FormData();
      formData.append("file", {
        uri,
        name: "signature.png",
        type: mime,
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any);
      const r = await api.post<UploadResult>("/api/profile/signature", formData, {
        headers: { "Content-Type": "multipart/form-data" },
      });
      return r.data;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: profileKeys.signature() });
    },
  });
}

// Returns { exists: true } when signature uploaded; { exists: false } when 404.
// We don't return the blob — consumers use <Image source={{ uri, headers }}>
// pointed at the API URL with the auth token.
export function useOwnSignature() {
  return useQuery({
    queryKey: profileKeys.signature(),
    queryFn: async () => {
      try {
        await api.get("/api/profile/signature", { responseType: "blob" });
        return { exists: true };
      } catch (e) {
        if ((e as { response?: { status?: number } })?.response?.status === 404) return { exists: false };
        throw e;
      }
    },
    staleTime: 5 * 60 * 1000,
  });
}
