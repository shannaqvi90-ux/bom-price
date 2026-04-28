export const PWA_API_CACHE_NAMES = ["bom-api-list-cache", "bom-api-detail-cache"] as const;

export async function clearPwaApiCaches(): Promise<void> {
  if (typeof caches === "undefined") return;
  await Promise.all(PWA_API_CACHE_NAMES.map((name) => caches.delete(name).catch(() => false)));
}
