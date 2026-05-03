#!/usr/bin/env node
/**
 * One-shot codemod: replace hardcoded Tailwind gray/slate/white classes with
 * semantic theme tokens defined in bom-web/src/index.css (@theme inline block).
 *
 * Scope: bom-web/src/**\/*.tsx, excluding **\/*.test.tsx and **\/scripts/**.
 *
 * Pass --dry-run to preview without writing.
 */
import { readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, relative } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = fileURLToPath(new URL(".", import.meta.url));
const SRC_ROOT = join(HERE, "..", "src");
const DRY = process.argv.includes("--dry-run");

const TOKEN_MAP = {
  // Body text (descending intensity → foreground vs muted)
  "text-gray-400": "text-muted-foreground",
  "text-gray-500": "text-muted-foreground",
  "text-gray-600": "text-muted-foreground",
  "text-gray-700": "text-foreground",
  "text-gray-800": "text-foreground",
  "text-gray-900": "text-foreground",
  "text-slate-400": "text-muted-foreground",
  "text-slate-500": "text-muted-foreground",
  "text-slate-600": "text-muted-foreground",
  "text-slate-700": "text-foreground",
  "text-slate-800": "text-foreground",
  "text-slate-900": "text-foreground",
  // Backgrounds (page surface vs muted panel)
  "bg-white": "bg-card",
  "bg-gray-50": "bg-muted",
  "bg-gray-100": "bg-muted",
  "bg-slate-50": "bg-muted",
  "bg-slate-100": "bg-muted",
  // Borders + dividers
  "border-gray-100": "border-border",
  "border-gray-200": "border-border",
  "border-gray-300": "border-border",
  "border-slate-100": "border-border",
  "border-slate-200": "border-border",
  "border-slate-300": "border-border",
  "divide-gray-100": "divide-border",
  "divide-gray-200": "divide-border",
  "divide-slate-100": "divide-border",
  "divide-slate-200": "divide-border",
};

// Whole-word regex per token — \b boundaries prevent matching inside larger
// strings like text-gray-7000 (impossible) or border-gray-200/30 (suffix
// modifier — keep numeric prefix exact).
const REGEXES = Object.keys(TOKEN_MAP).map((t) => ({
  re: new RegExp(`\\b${t.replace(/[-/]/g, (c) => "\\" + c)}\\b`, "g"),
  to: TOKEN_MAP[t],
}));

function* walk(dir) {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) {
      if (entry.name === "node_modules" || entry.name === "dist") continue;
      yield* walk(full);
    } else if (entry.isFile() && full.endsWith(".tsx") && !full.endsWith(".test.tsx")) {
      yield full;
    }
  }
}

let filesChanged = 0;
let totalSubs = 0;
const summary = [];

for (const file of walk(SRC_ROOT)) {
  const before = readFileSync(file, "utf8");
  let after = before;
  let fileSubs = 0;
  for (const { re, to } of REGEXES) {
    after = after.replace(re, () => {
      fileSubs += 1;
      return to;
    });
  }
  if (fileSubs > 0) {
    filesChanged += 1;
    totalSubs += fileSubs;
    summary.push(`  ${relative(SRC_ROOT, file).replace(/\\/g, "/")}: ${fileSubs}`);
    if (!DRY) writeFileSync(file, after, "utf8");
  }
}

console.log(DRY ? "[DRY RUN]" : "[APPLIED]");
summary.forEach((l) => console.log(l));
console.log(`\nTotal: ${totalSubs} substitutions across ${filesChanged} file(s).`);
