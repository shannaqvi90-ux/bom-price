import { useEffect } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/axios";
import type { SignatureUploadResponse } from "@/types/api";

export const profileKeys = {
  ownSignature: ["profile", "own-signature"] as const,
};

export function useUploadSignature() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (file: File) => {
      const fd = new FormData();
      fd.append("file", file);
      const r = await api.post<SignatureUploadResponse>(
        "/profile/signature",
        fd,
        { headers: { "Content-Type": "multipart/form-data" } },
      );
      return r.data;
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: profileKeys.ownSignature }),
  });
}

// GET /api/profile/signature is [Authorize(Roles = "ManagingDirector")] — a bare
// <img src=...> can't authenticate (browsers don't send Authorization on img
// requests). Fetch the bytes via axios (which adds the JWT) and expose a blob
// URL the consumer can pass to <img src={url}>. Returns null if no signature
// exists (404). Caller is responsible for revoking the URL when unmounting —
// this hook handles revocation on data change + unmount.
export function useOwnSignatureBlobUrl() {
  const query = useQuery({
    queryKey: profileKeys.ownSignature,
    queryFn: async (): Promise<string | null> => {
      try {
        const r = await api.get<Blob>("/profile/signature", { responseType: "blob" });
        return URL.createObjectURL(r.data);
      } catch (e: unknown) {
        // 404 = no signature uploaded yet; treat as null (not error).
        const status = (e as { response?: { status?: number } } | null)?.response?.status;
        if (status === 404) return null;
        throw e;
      }
    },
    retry: false,
    staleTime: Infinity,
  });

  useEffect(() => {
    const url = query.data;
    return () => {
      if (url) URL.revokeObjectURL(url);
    };
  }, [query.data]);

  return query;
}
