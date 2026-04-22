const SCRIPT_STYLE_RE = /<(script|style)\b[^>]*>[\s\S]*?<\/\1\s*>/gi;
const TAG_RE = /<\/?[a-z][^>]*>/gi;
const ENTITY_RE = /&(?:amp|lt|gt|quot|#39|nbsp);/gi;
const ENTITY_MAP: Record<string, string> = {
  "&amp;": "&",
  "&lt;": "<",
  "&gt;": ">",
  "&quot;": '"',
  "&#39;": "'",
  "&nbsp;": " ",
};

export function stripTags(input: string | null | undefined): string {
  if (!input) return "";
  return input
    .replace(SCRIPT_STYLE_RE, "")
    .replace(TAG_RE, "")
    .replace(ENTITY_RE, (m) => ENTITY_MAP[m.toLowerCase()] ?? m)
    .replace(/\s+/g, " ")
    .trim();
}
