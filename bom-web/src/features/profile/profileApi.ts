import { useEffect, useState } from "react";
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
// exists (404).
//
// Cache stores the immutable Blob; URL is created per-mount and revoked on
// unmount. Prior implementation cached the URL itself and revoked it on
// unmount, which poisoned the React Query cache: revisiting the page within
// the staleTime window pulled the stale (revoked) URL → <img> silently
// failed → user saw "no signature" until manual refresh.
//
// Cache policy: 1-minute stale window + refetchOnWindowFocus so a
// freshly-uploaded signature (esp. from the mobile app) becomes visible on
// the web within a minute or on the next tab focus.
export function useOwnSignatureBlobUrl() {
  const query = useQuery({
    queryKey: profileKeys.ownSignature,
    queryFn: async (): Promise<Blob | null> => {
      try {
        const r = await api.get<Blob>("/profile/signature", { responseType: "blob" });
        return r.data;
      } catch (e: unknown) {
        // 404 = no signature uploaded yet; treat as null (not error).
        const status = (e as { response?: { status?: number } } | null)?.response?.status;
        if (status === 404) return null;
        throw e;
      }
    },
    retry: false,
    staleTime: 60_000,
    refetchOnWindowFocus: true,
  });

  const [url, setUrl] = useState<string | null>(null);
  useEffect(() => {
    const blob = query.data;
    if (!blob) {
      setUrl(null);
      return;
    }
    const newUrl = URL.createObjectURL(blob);
    setUrl(newUrl);
    return () => URL.revokeObjectURL(newUrl);
  }, [query.data]);

  return {
    ...query,
    data: url,
  };
}
