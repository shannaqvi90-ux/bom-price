// Active V3 cache names — match src/sw.ts.
export const PWA_API_CACHE_NAMES = [
  "bom-api-list-cache-v3",
  "bom-api-detail-cache-v3",
] as const;

// Legacy V2.3 cache names — kept here so logout fully invalidates any stale
// V2.3 entries on devices upgrading from a pre-V3 PWA install.
export const PWA_API_CACHE_NAMES_LEGACY = [
  "bom-api-list-cache",
  "bom-api-detail-cache",
] as const;

export async function clearPwaApiCaches(): Promise<void> {
  if (typeof caches === "undefined") return;
  const all = [...PWA_API_CACHE_NAMES, ...PWA_API_CACHE_NAMES_LEGACY];
  await Promise.all(all.map((name) => caches.delete(name).catch(() => false)));
}
