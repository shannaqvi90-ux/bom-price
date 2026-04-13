import { describe, expect, it } from "vitest";
import { formatRelative } from "./date";

const base = new Date("2026-04-14T12:00:00Z");

describe("formatRelative", () => {
  it('returns "just now" for timestamps under 60 seconds ago', () => {
    const iso = new Date(base.getTime() - 30_000).toISOString();
    expect(formatRelative(iso, base)).toBe("just now");
  });

  it("returns minutes ago for timestamps under an hour", () => {
    const iso = new Date(base.getTime() - 5 * 60_000).toISOString();
    expect(formatRelative(iso, base)).toBe("5 minutes ago");
  });

  it("returns hours ago for timestamps under a day", () => {
    const iso = new Date(base.getTime() - 3 * 60 * 60_000).toISOString();
    expect(formatRelative(iso, base)).toBe("3 hours ago");
  });

  it("returns days ago for timestamps under a week", () => {
    const iso = new Date(base.getTime() - 2 * 24 * 60 * 60_000).toISOString();
    expect(formatRelative(iso, base)).toBe("2 days ago");
  });

  it("returns an absolute locale date for timestamps older than a week", () => {
    const iso = new Date(base.getTime() - 30 * 24 * 60 * 60_000).toISOString();
    const result = formatRelative(iso, base);
    // Shape check only — locale output varies, so assert it's not one of the relative forms
    expect(result).not.toMatch(/ago|just now/);
    expect(result.length).toBeGreaterThan(0);
  });
});
