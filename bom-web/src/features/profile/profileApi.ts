import { useMutation } from "@tanstack/react-query";
import { api, API_BASE_URL } from "@/api/axios";
import type { SignatureUploadResponse } from "@/types/api";

export function useUploadSignature() {
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
  });
}

// Plain helper (not a hook) — <img> tags need a string URL. Backend resolves
// the user from the auth token and serves the signature bytes.
//
// Why API_BASE_URL: in dev VITE_API_BASE_URL is "", so this returns
// "/api/profile/signature" (Vite proxy → localhost:7300). In prod it's the
// Fly.io origin, so it returns "https://bom-fpf-api.fly.dev/api/profile/signature".
// A bare "/api/..." string would not reach the API in prod.
export function getOwnSignatureUrl(): string {
  return `${API_BASE_URL}/profile/signature`;
}
