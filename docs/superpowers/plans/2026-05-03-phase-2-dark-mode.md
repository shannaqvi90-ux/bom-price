# Phase 2 Dark Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `bom-web` off the PR #64 `.dark` CSS shim so dark mode works natively via semantic theme tokens + Tailwind `dark:` variants. End state: shim deleted, every component dark-mode-explicit.

**Architecture:** Three-stage migration shipped as one PR with four commits — (1) add a Node.js codemod tool that does 1:1 hardcoded-gray → semantic-token substitutions, (2) run the codemod and commit the mechanical output, (3) manually add `dark:` variants to ~95 status-color sites the codemod can't safely auto-handle, (4) delete the 105-line shim block from `index.css`. Each commit independently green via `npm test` + `npm run build`.

**Tech Stack:** Node.js 22 (codemod), TypeScript 5, React 19, Vite 8, Tailwind CSS v4 (with `@theme inline` token block), Vitest 3.

**Spec:** `docs/superpowers/specs/2026-05-03-phase-2-dark-mode-design.md` (commit `8d7e4da`)

---

## File Structure

### Created
- `bom-web/scripts/migrate-grays.mjs` — one-shot Node.js codemod. Reads all `.tsx` under `bom-web/src` (excluding `**/*.test.tsx`), regex-replaces hardcoded gray/slate/white tokens with semantic equivalents per the substitution table, supports `--dry-run`. Idempotent (skips lines already containing the target token).

### Modified
- `bom-web/src/index.css` — delete shim block (lines 89-193 in commit `0b12357`); keep `:root` / `.dark` variable blocks + `@theme inline` block + `body` + autofill rules.
- ~26 `bom-web/src/**/*.tsx` files — output of codemod (Commit 2) + manual status-color `dark:` additions (Commit 3). Touched files enumerated dynamically from grep.

### Untouched
- All `bom-web/src/**/*.test.tsx` files (excluded by codemod). If a test asserts a specific hardcoded class that the codemod replaced, update the assertion in the same commit (2 or 3) — but no proactive test changes.
- `bom-mobile/` — React Native, no Tailwind, no shim, out of scope.
- `BomPriceApproval.API/` — backend, irrelevant.

---

## Reference: Status Color → dark: Mapping

Used by Tasks 7-10 when adding `dark:` siblings to status colors. Apply consistently across all files.

| Light class | Add this dark: variant |
|---|---|
| `bg-blue-50` | `dark:bg-blue-900/30` |
| `bg-blue-100` | `dark:bg-blue-900/40` |
| `bg-green-50` / `bg-emerald-50` | `dark:bg-emerald-900/30` |
| `bg-green-100` / `bg-emerald-100` | `dark:bg-emerald-900/40` |
| `bg-amber-50` / `bg-yellow-50` | `dark:bg-amber-900/30` |
| `bg-amber-100` / `bg-yellow-100` | `dark:bg-amber-900/40` |
| `bg-red-50` | `dark:bg-red-900/30` |
| `bg-red-100` | `dark:bg-red-900/40` |
| `bg-orange-50` | `dark:bg-orange-900/30` |
| `bg-orange-100` | `dark:bg-orange-900/40` |
| `text-blue-{700,800,900}` | `dark:text-blue-300` |
| `text-green-{700,800,900}` / `text-emerald-*` | `dark:text-emerald-300` |
| `text-amber-{700,800,900}` / `text-yellow-*` | `dark:text-amber-300` |
| `text-red-{700,800,900}` | `dark:text-red-300` |
| `text-orange-{700,800,900}` | `dark:text-orange-300` |
| `border-blue-{200,300}` | `dark:border-blue-800/60` |
| `border-green-{200,300}` / `border-emerald-*` | `dark:border-emerald-800/60` |
| `border-amber-{200,300}` / `border-yellow-*` | `dark:border-amber-800/60` |
| `border-red-{200,300}` | `dark:border-red-800/60` |
| `border-orange-{200,300}` | `dark:border-orange-800/60` |

**Order convention:** append `dark:` variants at the END of the className string, in the same order as their light-mode siblings (`border` → `bg` → `text`). Keeps diffs visually parseable.

**Example:**

Before:
```tsx
<div className="rounded border border-blue-200 bg-blue-50 p-3 text-blue-700">
```
After:
```tsx
<div className="rounded border border-blue-200 bg-blue-50 p-3 text-blue-700 dark:border-blue-800/60 dark:bg-blue-900/30 dark:text-blue-300">
```

---

## Task 1: Setup Feature Branch

**Files:** none (git-only)

- [ ] **Step 1: Verify clean working tree on master**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" status --short
git -C "D:/shan projects/BOM_Price_Approval" log --oneline -1
```
Expected: empty status output. HEAD = `8d7e4da docs(spec): phase 2 dark mode migration design`. If status is dirty, STOP and report — do NOT proceed.

- [ ] **Step 2: Create + checkout feature branch**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" checkout -b chore/dark-mode-phase-2
```
Expected: `Switched to a new branch 'chore/dark-mode-phase-2'`

- [ ] **Step 3: Verify branch state**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" branch --show-current
```
Expected: `chore/dark-mode-phase-2`

---

## Task 2: Write the Codemod Script

**Files:**
- Create: `bom-web/scripts/migrate-grays.mjs`

- [ ] **Step 1: Create the scripts directory if missing**

Run:
```bash
ls "D:/shan projects/BOM_Price_Approval/bom-web/scripts/" 2>/dev/null || mkdir -p "D:/shan projects/BOM_Price_Approval/bom-web/scripts"
```
Expected: directory exists or is created.

- [ ] **Step 2: Write the codemod script**

Create `bom-web/scripts/migrate-grays.mjs` with exactly this content:

```javascript
#!/usr/bin/env node
/**
 * One-shot codemod: replace hardcoded Tailwind gray/slate/white classes with
 * semantic theme tokens defined in bom-web/src/index.css (@theme inline block).
 *
 * Scope: bom-web/src/**\/*.tsx, excluding **\/*.test.tsx and **\/scripts/**.
 *
 * Pass --dry-run to preview without writing.
 */
import { readdirSync, readFileSync, writeFileSync, statSync } from "node:fs";
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
```

- [ ] **Step 3: Sanity-test the codemod with --dry-run**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && node scripts/migrate-grays.mjs --dry-run
```
Expected output: `[DRY RUN]` followed by ~26 file paths each with a substitution count, total in the range 150-180 substitutions across ~26 files. If the count looks wildly off (e.g., zero, or 500+) STOP — there's a regex bug.

- [ ] **Step 4: Verify dry-run did NOT modify files**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" status --short
```
Expected: only `?? bom-web/scripts/migrate-grays.mjs` (untracked) — no modified .tsx files.

---

## Task 3: Commit the Codemod Tool

**Files:**
- Add: `bom-web/scripts/migrate-grays.mjs`

- [ ] **Step 1: Stage the codemod script**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" add bom-web/scripts/migrate-grays.mjs
```

- [ ] **Step 2: Show diff summary**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" diff --cached --stat
```
Expected: `1 file changed, ~110 insertions(+)`

- [ ] **Step 3: Commit (Auto Mode — no approval pause)**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" commit -m "$(cat <<'EOF'
chore(web): add gray-to-token codemod script

Lands the one-shot Node.js codemod that will be used in the next commit
to migrate hardcoded Tailwind gray/slate/white classes to the semantic
tokens already defined in src/index.css's @theme inline block.

Idempotent (skips lines already containing target token), supports
--dry-run, excludes test files. See spec at
docs/superpowers/specs/2026-05-03-phase-2-dark-mode-design.md.

No source changes in this commit; just the tool.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```
Expected: `[chore/dark-mode-phase-2 <sha>] chore(web): add gray-to-token codemod script` + `1 file changed`.

- [ ] **Step 4: Verify commit landed**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" log --oneline -1
```
Expected: latest commit is the chore commit just made.

---

## Task 4: Run the Codemod

**Files:**
- Modify: ~26 `bom-web/src/**/*.tsx` files (full list emitted by codemod summary)

- [ ] **Step 1: Run the codemod (no --dry-run)**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && node scripts/migrate-grays.mjs
```
Expected output: `[APPLIED]` followed by per-file substitution counts. Save the total substitution number for the commit message.

- [ ] **Step 2: Verify expected files were modified**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" status --short
```
Expected: ~26 modified `.tsx` files under `bom-web/src/`. If a file outside `bom-web/src/` was modified, STOP — codemod scope bug.

- [ ] **Step 3: Spot-check the diff on one file**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" diff bom-web/src/components/v3/BomEditorTable.tsx
```
Expected: lines like `<thead className="bg-gray-50">` → `<thead className="bg-muted">`, `text-gray-700` → `text-foreground`, `divide-gray-100` → `divide-border`, etc. NO changes outside `className=` strings.

- [ ] **Step 4: Run vitest**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm test -- --run
```
Expected: all tests pass at the same count as before the codemod (was 285+ pre-PR-#64; current count noted at start of session). If any test fails because it asserts a specific hardcoded class, update the assertion to the new token (in this same commit) and re-run.

- [ ] **Step 5: Run TypeScript build**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm run build
```
Expected: `tsc -b` passes + Vite build succeeds. Tailwind warnings about new tokens are EXPECTED to be absent (tokens are already wired via `@theme inline`).

- [ ] **Step 6: Manual smoke — start dev server**

Run (in a background terminal):
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm run dev
```
Expected: `http://localhost:5300` ready.

- [ ] **Step 7: Browser smoke (light mode)**

Open `http://localhost:5300/login` in a browser. Log in as MD. Visit:
- `/dashboard`
- `/requisitions`
- Any `/requisitions/:id` detail page (existing data)
- Any `/approvals/:id/margin` page

Verify each page renders the SAME as before the codemod (text colors, panel backgrounds, borders all visually identical).

- [ ] **Step 8: Browser smoke (dark mode)**

Toggle dark mode via Topbar moon icon. Re-visit the same pages. Verify each renders correctly in dark mode (text readable on dark background, no light-on-light or dark-on-dark panels).

- [ ] **Step 9: Stop dev server**

Kill the background dev-server terminal (Ctrl+C).

---

## Task 5: Commit the Codemod Output

**Files:**
- Modified: ~26 `.tsx` files (already modified by Task 4)

- [ ] **Step 1: Stage modified files**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" add bom-web/src
```
NOTE: do NOT use `git add -A` — `bom-web/src` is the precise scope; nothing else should change.

- [ ] **Step 2: Show diff summary**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" diff --cached --stat
```
Expected: ~26 files changed, hundreds of `+/-` insertions/deletions (the per-line className edits).

- [ ] **Step 3: Commit (Auto Mode — no approval pause)**

Run, replacing `<N>` with the substitution total from Task 4 Step 1:
```bash
git -C "D:/shan projects/BOM_Price_Approval" commit -m "$(cat <<'EOF'
refactor(web): codemod hardcoded grays → semantic tokens

Mechanical output of running scripts/migrate-grays.mjs:
- text-gray-{400..900} / text-slate-* → text-foreground or
  text-muted-foreground
- bg-white → bg-card; bg-gray-{50,100} / bg-slate-* → bg-muted
- border-gray-{100,200,300} / border-slate-* → border-border
- divide-gray-* / divide-slate-* → divide-border

These tokens were already defined in src/index.css's @theme inline
block (introduced by PR #64) but never adopted at the component level.
Visual smoke confirms light + dark mode render identical to pre-codemod.

Status color migrations (bg-blue-50 etc. + dark: variants) follow in
the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Verify commit + tests still green**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" log --oneline -2
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm test -- --run 2>&1 | tail -5
```
Expected: latest 2 commits = codemod-output + codemod-tool. Vitest passes.

---

## Task 6: Audit Status Color Sites Needing dark: Variants

**Files:** none (read-only audit)

- [ ] **Step 1: Generate the audit list (count)**

Run:
```bash
grep -rn -E "\b(bg|text|border)-(blue|green|emerald|amber|yellow|red|orange|indigo|purple)-(50|100|200|300|700|800|900)\b" "D:/shan projects/BOM_Price_Approval/bom-web/src" --include="*.tsx" 2>/dev/null | grep -v "dark:" | grep -v "\.test\." | wc -l
```
Expected: ~95 (matches spec estimate). Memorize this number — it's the manual workload for Tasks 7-10.

- [ ] **Step 2: Group by file**

Run:
```bash
grep -rn -E "\b(bg|text|border)-(blue|green|emerald|amber|yellow|red|orange|indigo|purple)-(50|100|200|300|700|800|900)\b" "D:/shan projects/BOM_Price_Approval/bom-web/src" --include="*.tsx" 2>/dev/null | grep -v "dark:" | grep -v "\.test\." | cut -d: -f1 | sort | uniq -c | sort -rn
```
Expected output: list of files with count of status-color lines lacking `dark:` variant. Highest-count files become the priority order for Task 7-10.

- [ ] **Step 3: Save grouping for reference**

Note the top files mentally (don't write to disk — the audit is just to scope work). Expected high-count files (per spec):
- `MdFgPricingCard.tsx`
- `MdFinalSignPage.tsx` (partial — already has 5 dark: variants)
- `RequisitionDetailPage.tsx`
- `CustomerConfirmPage.tsx`
- `FinalPriceSummary.tsx`
- Various modals and admin pages

Centralized primitives (`StatusBadge.tsx`, `V3StatusBadge.tsx`) should already be done — verify they're absent from the audit list.

---

## Task 7: Add dark: Variants to MD Pricing/Approval Pages

**Files (apply pattern below to each — add `dark:` siblings per Reference Mapping at top of plan):**
- Modify: `bom-web/src/features/approvals/MdFgPricingCard.tsx`
- Modify: `bom-web/src/features/approvals/MdMarginPage.tsx`
- Modify: `bom-web/src/features/approvals/MdFinalSignPage.tsx`
- Modify: `bom-web/src/features/approvals/FinalPriceSummary.tsx`
- Modify: `bom-web/src/features/approvals/RejectReqModal.tsx`

### Per-file pattern (same for all files in Tasks 7-10)

- [ ] **Step 1: For each file in the list above, grep for untreated status colors**

Run (substituting file path):
```bash
grep -n -E "\b(bg|text|border)-(blue|green|emerald|amber|yellow|red|orange|indigo|purple)-(50|100|200|300|700|800|900)\b" bom-web/src/features/approvals/MdFgPricingCard.tsx | grep -v "dark:"
```
Expected: emit lines that need attention. If empty for a file, that file is already done — move to the next.

- [ ] **Step 2: For each line emitted, use the Edit tool to add `dark:` siblings per the Reference Mapping at top of plan**

Append `dark:` variants at the END of the className string in the order: `border` → `bg` → `text`. See the Example block in the Reference Mapping section.

- [ ] **Step 3: Re-grep each file to verify zero untreated sites**

For each file modified, repeat Step 1's grep — should return ZERO lines.

- [ ] **Step 4: Run vitest after this batch**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm test -- --run 2>&1 | tail -5
```
Expected: all tests still green.

---

## Task 8: Add dark: Variants to Detail/Customer-Confirm Pages

**Files:**
- Modify: `bom-web/src/features/requisitions/RequisitionDetailPage.tsx`
- Modify: `bom-web/src/features/requisitions/CustomerConfirmPage.tsx`
- Modify: `bom-web/src/features/requisitions/SignedQuotationViewer.tsx`
- Modify: `bom-web/src/features/requisitions/components/RequisitionTimeline.tsx`

- [ ] **Step 1: For each file above, repeat the grep + edit pattern from Task 7 (Steps 1-3) using the same status-color → dark: mapping table.**

- [ ] **Step 2: Run vitest after this batch**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm test -- --run 2>&1 | tail -5
```
Expected: green.

---

## Task 9: Add dark: Variants to Admin + Modal Pages

**Files:**
- Modify: `bom-web/src/features/admin/AdminActionsCard.tsx`
- Modify: `bom-web/src/features/admin/modals/OverridePricesModal.tsx`
- Modify: `bom-web/src/features/admin/modals/DeleteCustomerModal.tsx`
- Modify: `bom-web/src/features/admin/modals/DeleteRequisitionModal.tsx`
- Modify: `bom-web/src/features/admin/modals/RollbackStatusModal.tsx`
- Modify: `bom-web/src/features/admin/audit-log/DiffPanel.tsx`
- Modify: `bom-web/src/features/users/ResetPasswordModal.tsx`

- [ ] **Step 1: For each file above, repeat the grep + edit pattern from Task 7 (Steps 1-3) using the same status-color → dark: mapping table.**

- [ ] **Step 2: Run vitest after this batch**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm test -- --run 2>&1 | tail -5
```
Expected: green.

---

## Task 10: Add dark: Variants to Sales/Accountant + Misc Pages

**Files (all remaining files containing untreated status colors per Task 6 audit):**
- Modify: `bom-web/src/features/requisitions/NewRequisitionPage.tsx`
- Modify: `bom-web/src/features/costing/CostingEntryV3Page.tsx`
- Modify: `bom-web/src/features/profile/ProfileSignaturePage.tsx`
- Modify: `bom-web/src/features/dashboard/MdDashboard.tsx`
- Modify: `bom-web/src/features/md/MdListPage.tsx`
- Modify: `bom-web/src/features/accountant/AccountantListPage.tsx`
- Modify: `bom-web/src/components/v3/BomEditorTable.tsx`
- Modify: `bom-web/src/components/v3/CreateCustomerModal.tsx`
- Modify: `bom-web/src/components/v3/CreateFinishedGoodModal.tsx`
- Modify: `bom-web/src/components/v3/CreateRawMaterialModal.tsx`
- Modify: `bom-web/src/components/pwa/InstallBanner.tsx`
- Modify: `bom-web/src/components/pwa/InstallModal.tsx`
- Modify: `bom-web/src/components/OwnedByBadge.tsx`

- [ ] **Step 1: For each file above, repeat the grep + edit pattern from Task 7 (Steps 1-3) using the same status-color → dark: mapping table.**

- [ ] **Step 2: Run vitest after this batch**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm test -- --run 2>&1 | tail -5
```
Expected: green.

---

## Task 11: Final Audit + Browser Smoke Before Commit 3

**Files:** none (verification only)

- [ ] **Step 1: Re-run the audit query — should return zero**

Run:
```bash
grep -rn -E "\b(bg|text|border)-(blue|green|emerald|amber|yellow|red|orange|indigo|purple)-(50|100|200|300|700|800|900)\b" "D:/shan projects/BOM_Price_Approval/bom-web/src" --include="*.tsx" 2>/dev/null | grep -v "dark:" | grep -v "\.test\." | wc -l
```
Expected: `0`. If non-zero, STOP and treat the remaining lines per Task 7 Step 2 mapping. Common cause: `indigo` or `purple` color used somewhere not covered by Tasks 7-10 — handle in place.

- [ ] **Step 2: Run TypeScript build**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm run build 2>&1 | tail -20
```
Expected: `tsc -b` passes + Vite build succeeds.

- [ ] **Step 3: Browser smoke — restart dev server**

Run (background):
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm run dev
```

- [ ] **Step 4: Light-mode visual sweep**

Browser → `http://localhost:5300/login`. Log in as MD. Visit each page below and verify status badges + accent panels render in their expected light colors:
- `/dashboard`
- `/requisitions` (list with status badges)
- `/requisitions/:id` (detail with status banner)
- `/approvals/:id/margin` (with previous-attempt amber panel — IF a CustomerConfirm-rejected req exists)
- `/approvals/:id/final-sign`

- [ ] **Step 5: Dark-mode visual sweep**

Toggle dark mode (Topbar). Re-visit the same pages. Verify status badges + accent panels render in their dark counterparts (e.g., amber panel in dark mode should have dark amber background + light amber text — NOT illegible gray-on-gray).

- [ ] **Step 6: Stop dev server**

Kill the background terminal (Ctrl+C).

---

## Task 12: Commit the Status-Color dark: Variants

**Files:** modified across Tasks 7-10 (~14-17 files)

- [ ] **Step 1: Stage modified files**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" add bom-web/src
```

- [ ] **Step 2: Show diff summary**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" diff --cached --stat
```
Expected: ~14-17 files changed, all under `bom-web/src/`.

- [ ] **Step 3: Commit (Auto Mode — no approval pause)**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" commit -m "$(cat <<'EOF'
refactor(web): add dark: variants to status colors

Adds explicit Tailwind dark: variants to every bg-{blue,green,emerald,
amber,yellow,red,orange}-{50,100}, text-*-{700,800,900}, and
border-*-{200,300} usage that didn't already have one. Mappings follow
the spec's table — 30%-opacity 900-shade backgrounds + 300-shade text +
60%-opacity 800-shade borders for visual parity with the .dark shim
PR #64 introduced.

Centralized primitives (StatusBadge, V3StatusBadge, MdMarginPage)
already had dark: variants from prior PRs and were not touched.

After this commit, the .dark shim block in src/index.css is no longer
load-bearing for any component; deletion follows in the next commit.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Verify commit landed**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" log --oneline -3
```
Expected: latest 3 commits = status-color dark variants → codemod output → codemod tool.

---

## Task 13: Delete the Shim Block

**Files:**
- Modify: `bom-web/src/index.css` (delete lines 89-193 of current revision)

- [ ] **Step 1: Read current index.css to confirm shim location**

Use the Read tool on `bom-web/src/index.css`. Confirm that lines 89-193 contain the comment block starting `/* ──────────────────────────────────────────────────────────────────────────` followed by `DARK MODE COMPATIBILITY SHIM` and ending after the `border-orange-200, .dark .border-orange-300 { border-color: rgb(154 52 18 / 0.6); }` rule.

- [ ] **Step 2: Use Edit tool to delete the shim block**

Edit `bom-web/src/index.css` to delete the entire block from the `/* ──────────────────────────────────────────────────────────────────────────` line that introduces "DARK MODE COMPATIBILITY SHIM" through the closing `}` of the last `.dark .border-orange-300` rule. Replace with: nothing (the file ends after the autofill defensive overrides).

`old_string` for the Edit (full shim block):
```
/* ──────────────────────────────────────────────────────────────────────────
   DARK MODE COMPATIBILITY SHIM
   ──────────────────────────────────────────────────────────────────────────
   The codebase has ~300 hardcoded Tailwind gray classes (text-gray-*,
   bg-white, bg-gray-*, border-gray-*) that were written assuming
   light-mode-only. When the user toggles dark mode (Topbar moon/sun icon),
   those hardcoded classes don't respect the .dark CSS variables and the
   page renders illegibly (gray text on dark gray bg).

   This block remaps the most-used hardcoded classes to theme tokens when
   the .dark class is present on <html>. Compound selector specificity
   (.dark .x = 0,0,2,0) beats Tailwind's atomic (.x = 0,0,1,0) so no
   !important is needed.

   This is a temporary shim — the long-term fix is to migrate components
   to use semantic tokens (text-foreground, bg-card, border-border, etc.)
   directly. See the dark-mode migration plan in the PR description.
   ────────────────────────────────────────────────────────────────────── */
```
… and ALL the `.dark .*` rules below it through `.dark .border-orange-300 { border-color: rgb(154 52 18 / 0.6); }`.

`new_string`: empty string.

If the Edit tool requires a non-empty replacement, replace with a single newline.

- [ ] **Step 3: Verify file ends cleanly**

Read `bom-web/src/index.css` lines 80-100 (after deletion). Expected: file ends after the `transition: background-color 5000s ease-in-out 0s; }` autofill rule — no orphaned `.dark .*` rules.

- [ ] **Step 4: Run vitest + tsc + Vite build**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm test -- --run 2>&1 | tail -5 && npm run build 2>&1 | tail -10
```
Expected: tests + build all green.

- [ ] **Step 5: Final browser smoke (most critical step in plan)**

Restart dev server:
```bash
cd "D:/shan projects/BOM_Price_Approval/bom-web" && npm run dev
```

In browser, verify ALL these pages render correctly in BOTH light and dark mode:
- `/login`
- `/dashboard` (each role)
- `/requisitions` (list with multi-status badges)
- `/requisitions/:id` (detail page — both finished good cards AND status banner AND quote summary)
- `/approvals/:id/margin` (margin entry with previous-attempt panel if any)
- `/approvals/:id/final-sign`
- `/profile/signature`

For each page in dark mode: confirm NO `.dark .*` shim is needed (text readable, panels colored correctly, status badges legible). If ANY page looks broken in dark mode → root cause is a missed status-color site → grep that page's source for `bg-(blue|green|...)-50` lines without `dark:` siblings, fix per Task 7 mapping, retry.

- [ ] **Step 6: Stop dev server**

Kill the background terminal (Ctrl+C).

---

## Task 14: Commit the Shim Deletion

**Files:**
- Modified: `bom-web/src/index.css`

- [ ] **Step 1: Stage the index.css change**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" add bom-web/src/index.css
```

- [ ] **Step 2: Show diff summary**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" diff --cached --stat
git -C "D:/shan projects/BOM_Price_Approval" diff --cached bom-web/src/index.css | head -10
```
Expected: 1 file changed, ~105 deletions (the shim block) and 0 insertions. First line of diff shows the file path; deletion preview confirms the right block was removed.

- [ ] **Step 3: Commit (Auto Mode — no approval pause)**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" commit -m "$(cat <<'EOF'
chore(web): delete .dark CSS shim from index.css

Removes the 105-line compatibility shim PR #64 introduced. Every
component now uses semantic theme tokens (Tasks 1-2 of this PR) +
explicit Tailwind dark: variants for status colors (Task 3 of this
PR), so the shim is no longer load-bearing.

End state: dark mode "natively works" via the @theme inline tokens +
dark: variants on every status accent. New components added to the
codebase will need to follow the same pattern (no shim to fall back
on), which is what we want for long-term maintainability.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Verify all 4 commits present**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" log --oneline -5
```
Expected (top to bottom):
1. `chore(web): delete .dark CSS shim from index.css`
2. `refactor(web): add dark: variants to status colors`
3. `refactor(web): codemod hardcoded grays → semantic tokens`
4. `chore(web): add gray-to-token codemod script`
5. `docs(spec): phase 2 dark mode migration design` (the spec from earlier)

---

## Task 15: Push Branch + Open PR

**Files:** none (git/gh only)

- [ ] **Step 1: Push the feature branch**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" push -u origin chore/dark-mode-phase-2
```
Expected: branch published, tracking origin set.

- [ ] **Step 2: Verify CI is configured (skip if no CI)**

Run:
```bash
gh -R shannaqvi90-ux/bom-price pr list --head chore/dark-mode-phase-2 --json number 2>/dev/null
```
If output shows existing PR, skip Step 3. If empty, proceed to Step 3.

- [ ] **Step 3: Open the PR (Auto Mode — open immediately, report URL)**

Run from the project root (so `gh` picks up repo context):
```bash
cd "D:/shan projects/BOM_Price_Approval" && gh pr create --base master --head chore/dark-mode-phase-2 --title "refactor(web): phase 2 dark mode — migrate off .dark shim" --body "$(cat <<'EOF'
## Summary

Migrates `bom-web` off the PR #64 \`.dark\` CSS shim. Every component now uses semantic theme tokens + explicit Tailwind \`dark:\` variants. End state: shim deleted, dark mode "natively works" everywhere.

## Approach

4 commits:
1. \`chore(web): add gray-to-token codemod script\` — Node.js one-shot tool
2. \`refactor(web): codemod hardcoded grays → semantic tokens\` — mechanical output of running the tool
3. \`refactor(web): add dark: variants to status colors\` — manual additions for blue/green/amber/red/orange status accents
4. \`chore(web): delete .dark CSS shim from index.css\` — removes the now-dead 105-line shim block

Each commit is independently green via \`npm test\` + \`npm run build\`.

## Spec

\`docs/superpowers/specs/2026-05-03-phase-2-dark-mode-design.md\`

## Test plan

- [ ] CI vitest green on this branch
- [ ] CI build green on this branch
- [ ] Manual smoke: light mode + dark mode on /dashboard, /requisitions, /requisitions/:id, /approvals/:id/margin, /approvals/:id/final-sign, /profile/signature

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```
Expected: PR URL printed. Note URL for the chat report.

- [ ] **Step 4: Report PR URL to user**

In chat, report the PR URL so the user can click through.

---

## Task 16: Optional — Wait for CI + Merge (Auto Mode pre-flight)

**Files:** none (git/gh only)

- [ ] **Step 1: Watch CI status**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval" && gh pr checks --watch
```
Expected: all checks pass (vitest + build). If a check fails, STOP and diagnose — do NOT proceed.

- [ ] **Step 2: Pre-flight merge checks (per CLAUDE.md)**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval" && gh pr view --json baseRefName,headRefName,labels,comments
```
Verify:
- baseRefName = `master`
- headRefName = `chore/dark-mode-phase-2`
- No `hold` label
- No "do not merge" comment

If ALL pre-flight pass, Auto Mode allows merge per CLAUDE.md. Otherwise STOP.

- [ ] **Step 3: Squash-merge the PR**

Run:
```bash
cd "D:/shan projects/BOM_Price_Approval" && gh pr merge --squash --delete-branch
```
Expected: merge succeeds, branch deleted both locally and on origin.

- [ ] **Step 4: Pull master + verify**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" checkout master && git pull origin master && git log --oneline -3
```
Expected: master HEAD is the squash-merge commit. Local repo back on master, clean.

- [ ] **Step 5: Verify worktree state still clean per CLAUDE.md**

Run:
```bash
git -C "D:/shan projects/BOM_Price_Approval" worktree list && git -C "D:/shan projects/BOM_Price_Approval" status --short
```
Expected: only main worktree, clean status. No orphans.

- [ ] **Step 6: Report completion to user**

In chat, summarize: PR merged, master SHA, total commits squashed, files changed, browser smoke results.

---

## Plan Summary

- **Tasks:** 16 (1 setup, 4 commit-phase tasks, 5 manual dark: tasks, 2 audit/smoke tasks, 2 commit-only tasks, 2 PR/merge tasks)
- **Commits in PR:** 4 (codemod tool, codemod output, dark: variants, shim delete)
- **Estimated effort:** 2-3 hours total — codemod authoring 30 min, codemod run + smoke 30 min, manual dark: variants 60-90 min, shim delete + final smoke 20 min, PR + merge 10 min
- **Risk profile:** LOW — every commit independently green, manual smoke gates progression, full revert is `git reset --hard` to spec commit `8d7e4da`

## Acceptance Criteria (mirrors spec)

- ✅ Zero hardcoded `text-gray-*` / `text-slate-*` / `bg-gray-*` / `bg-slate-*` / `border-gray-*` / `border-slate-*` / `bg-white` / `divide-gray-*` / `divide-slate-*` in `bom-web/src/**/*.tsx` (excluding tests)
- ✅ Every `bg-{color}-{50,100}` / `text-{color}-{700,800,900}` / `border-{color}-{200,300}` in `bom-web/src/**/*.tsx` has a sibling `dark:` variant on the same line
- ✅ `index.css` shim block (the 105-line `DARK MODE COMPATIBILITY SHIM` comment + all `.dark .*` rules below it) deleted; `:root` / `.dark` token blocks + `@theme inline` block + `body` + autofill rules retained
- ✅ Vitest passes at the same count as pre-PR
- ✅ `npm run build` (tsc + Vite) passes
- ✅ Manual browser smoke confirms identical light-mode rendering + correct dark-mode rendering on 6 priority pages

## Out-of-scope follow-ups (deferred per spec)

- ESLint rule preventing future hardcoded grays
- Semantic status tokens (`bg-status-info`, etc.)
- Mobile dark mode parity
