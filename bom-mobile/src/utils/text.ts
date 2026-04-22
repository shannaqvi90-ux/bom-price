const TAG_RE = /<\/?[a-z][^>]*>/gi;

export function stripTags(input: string | null | undefined): string {
  if (!input) return "";
  return input.replace(TAG_RE, "").trim();
}
