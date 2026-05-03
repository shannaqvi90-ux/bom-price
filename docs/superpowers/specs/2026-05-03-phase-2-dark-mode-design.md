# Phase 2 Dark Mode — Migrate Off the .dark Shim

**Date:** 2026-05-03
**Author:** Shan + Claude (Opus 4.7)
**Status:** Design — pending writing-plans

## Goal

Delete the `.dark` CSS shim introduced by PR #64 and migrate `bom-web` so dark mode "natively works" via semantic theme tokens + Tailwind `dark:` variants — the patterns the codebase should have used from day 1.

## Why now

PR #64 shipped a runtime shim that remaps ~200 hardcoded Tailwind gray/slate/white classes to theme tokens via compound `.dark .x` selectors. It works, but:

- **Brittle:** every new gray shade or status color added to the codebase silently relies on the shim being kept in sync. Devs reading a component see `text-gray-700` and assume light-mode-only.
- **Partial:** only the gray shades + status accents enumerated in `index.css` are remapped. Anything outside the enumeration silently breaks dark mode.
- **Maintenance debt:** the shim is 106 lines of CSS overrides that have to be reasoned about whenever dark mode looks wrong.

The codebase already has the right primitives — `--foreground`, `--card`, `--muted`, `--border` etc. exposed as `text-foreground`, `bg-card`, `border-border` Tailwind classes via the `@theme inline` block. Many components (StatusBadge, V3StatusBadge, MdMarginPage) already use these correctly. Phase 2 finishes the job.

## Scope

**In scope (web only):**
- All `bom-web/src/**/*.tsx` files containing hardcoded gray/slate/white classes (26 files, ~171 occurrences)
- All `bom-web/src/**/*.tsx` files containing status color usage that lacks a `dark:` variant (~95 occurrences, but most are in already-migrated centralized primitives)
- Deletion of the `.dark` CSS shim block (lines 89-193) in `bom-web/src/index.css`

**Out of scope:**
- `bom-mobile/` (React Native, doesn't use Tailwind)
- Test files (`**/*.test.tsx`) — assertions of specific class names. If a test breaks because the asserted class changed, fix the assertion in the same commit.
- `index.css` outside the shim block — `:root` + `.dark` token definitions stay; `@theme inline` block stays
- Adding new design-system tokens (`status-info`, `status-success` etc.) — overkill for current size; defer to a future Phase 3 if we ever build a real design system
- Visual regression test infra (Playwright screenshots etc.) — manual smoke + vitest is sufficient

## Approach

### Three-stage migration

1. **Codemod the mechanical 80%** — Node.js script that does pure 1:1 substitutions inside Tailwind class strings:

   | From | To |
   |---|---|
   | `text-gray-{500,600,400}` / `text-slate-{500,600,400}` | `text-muted-foreground` |
   | `text-gray-{700,800,900}` / `text-slate-{700,800,900}` | `text-foreground` |
   | `bg-white` | `bg-card` |
   | `bg-gray-{50,100}` / `bg-slate-{50,100}` | `bg-muted` |
   | `border-gray-{100,200,300}` / `border-slate-{100,200,300}` | `border-border` |
   | `divide-gray-{100,200}` / `divide-slate-{100,200}` | `divide-border` |

2. **Manually add `dark:` variants to status colors** — for each `bg-{blue,green,emerald,amber,yellow,red,orange}-{50,100}`, `text-*-{700,800,900}`, `border-*-{200,300}` occurrence that lacks a `dark:` sibling, add it. Pattern matches the shim's existing translations:

   | Light | Dark variant |
   |---|---|
   | `bg-blue-50` | `dark:bg-blue-900/30` |
   | `bg-green-50` / `bg-emerald-50` | `dark:bg-emerald-900/30` |
   | `bg-amber-50` / `bg-yellow-50` | `dark:bg-amber-900/30` |
   | `bg-red-50` | `dark:bg-red-900/30` |
   | `bg-orange-50` | `dark:bg-orange-900/30` |
   | `text-blue-700/800/900` | `dark:text-blue-300` |
   | `text-green-700/800/900` / `text-emerald-*` | `dark:text-emerald-300` |
   | `text-amber-700/800/900` / `text-yellow-*` | `dark:text-amber-300` |
   | `text-red-700/800/900` | `dark:text-red-300` |
   | `text-orange-700/800/900` | `dark:text-orange-300` |
   | `border-blue-200/300` | `dark:border-blue-800/60` |
   | `border-green-200/300` / `border-emerald-*` | `dark:border-emerald-800/60` |
   | `border-amber-200/300` / `border-yellow-*` | `dark:border-amber-800/60` |
   | `border-red-200/300` | `dark:border-red-800/60` |
   | `border-orange-200/300` | `dark:border-orange-800/60` |

3. **Delete the shim** — strip lines 89-193 from `bom-web/src/index.css`. Token definitions and `@theme inline` block remain untouched.

### Why codemod for grays only

The gray substitutions are 1:1 deterministic. A regex restricted to `className="..."`, `className={\`...\`}`, and the inside of `clsx(...)` / `cn(...)` calls is safe enough — false positives caught by `git diff` review.

Status color substitutions are not 1:1 because they need a SECOND class added (`dark:` variant) rather than a replacement. The codemod could do this, but the dark-shade choice can vary by intensity (some `bg-blue-50` might want `bg-blue-950/40` instead of `bg-blue-900/30` for visual consistency with surrounding context). Manual judgment beats auto-generation here, especially since most status colors are already centralized in `StatusBadge.tsx` / `V3StatusBadge.tsx`.

## Components

### Codemod tool

- **Language:** Node.js (~50 lines), checked into `bom-web/scripts/migrate-grays.mjs`. Cross-platform (vs PowerShell), same Node toolchain as Vite/vitest.
- **Strategy:** regex-based string substitution scoped to lines containing `className`. Skip lines that already contain the target token (idempotent).
- **Patterns matched:**
  - `className="...gray-token..."`
  - `className={\`...gray-token...\`}` (template literals)
  - Multi-line className strings (`className={\n "..." \n}`)
- **Excluded:** `**/*.test.tsx`, `**/index.css`, anything outside `bom-web/src/`
- **Output:** modifies files in place, prints summary `(N files modified, M substitutions)`. Read-only `--dry-run` flag for preview.

### Manual `dark:` migration

For each of the ~26 component files (full list discovered via `grep -rE "(bg|text|border)-(blue|green|emerald|amber|yellow|red|orange)-(50|100|200|300|700|800|900)"`):
- Read file
- For each status color usage lacking a `dark:` sibling, add the `dark:` variant per the table above
- Save

Centralized primitives (`StatusBadge.tsx`, `V3StatusBadge.tsx`) already done — confirm by grep, no action.

### Shim deletion

After steps 1+2 verified, delete lines 89-193 of `bom-web/src/index.css`. Keep:
- `:root { --background: ...; }` block
- `.dark { --background: ...; }` block
- `@theme inline { --color-background: var(--background); ... }` block
- `body` / autofill defensive overrides

## Data flow

No runtime data flow change. Build-time only:

```
.tsx files (hardcoded grays)
   ↓ Node.js codemod
.tsx files (semantic tokens)
   ↓ Vite + Tailwind v4
.css output (atomic classes from @theme inline)
   ↓ Browser
.dark on <html> → CSS vars switch → semantic classes recompute → visible dark mode
```

## Error handling

- **Codemod errors:** any file failing to parse → log + skip + non-zero exit. Manual rerun after fix.
- **Vitest failures after codemod:** indicates a test was asserting old class names. Update assertion to match new token; this is part of commit 1.
- **Visual regressions in dark mode after shim deletion:** root cause = a status color usage was missed. Rerun `grep -nE "(bg|text|border)-(blue|green|...)-(50|100|...)"` against modified files to find untreated cases.
- **Visual regressions in light mode:** should be impossible (semantic tokens resolve to identical light-mode values). If observed, root cause is most likely a typo in token name (`text-foregound`) — TS won't catch this since Tailwind classes are strings; `tsc` won't error but the page will render wrong color. Catch via manual smoke.

## Testing

### Automated
- `npm run build` (tsc + vite build) — must succeed
- `npm test` (vitest) — must pass at the same number as before (285/285 currently)

### Manual smoke (browser)
On dev server (`npm run dev` → `http://localhost:5300`):
1. Light mode default — verify no visual regression on:
   - `/dashboard` (Md/Sales/Accountant — log in as each)
   - `/requisitions` list
   - `/requisitions/:id` detail (signed + customer-confirm)
   - `/approvals/:id/margin` (with previous-attempt panel)
   - `/approvals/:id/final-sign`
   - `/requisitions/:id/customer-confirm`
2. Toggle dark mode (Topbar moon/sun) — same set of pages render correctly
3. Toggle light again — back to step 1 state

### Acceptance criteria
- Zero hardcoded `text-gray-*`, `bg-gray-*`, `border-gray-*`, `text-slate-*`, `bg-slate-*`, `border-slate-*`, `bg-white`, `divide-gray-*`, `divide-slate-*` remaining in `bom-web/src/**/*.tsx` (excluding tests)
- Every `bg-{color}-50/100`, `text-{color}-700/800/900`, `border-{color}-200/300` has a sibling `dark:` variant
- `index.css` shim block (lines 89-193) deleted
- `git diff bom-web/src/**/*.tsx` shows ONLY token replacements + dark: variant additions (no semantic logic changes)
- Manual smoke: 0 visual regressions in either mode

## Commit strategy

Single PR (`refactor(web): phase 2 dark mode — migrate off .dark shim`), 4 commits in order:

1. `chore(web): add gray-to-token codemod script (no source changes)`
   - Adds `bom-web/scripts/migrate-grays.mjs`
   - Doesn't run it; just lands the tool. Keeps PR's mechanical commit small for review.

2. `refactor(web): codemod hardcoded grays → semantic tokens`
   - Output of running the codemod from step 1
   - Pure mechanical diff; reviewer can verify against the substitution table in this spec

3. `refactor(web): add dark: variants to status colors`
   - Manual diffs across status-color usage sites
   - Smaller diff than step 2; each chunk is a judgment call (which dark shade)

4. `chore(web): delete .dark CSS shim from index.css`
   - Removes lines 89-193 of `index.css`
   - Last commit so reviewer can verify with `git diff HEAD~1` that everything still works without the shim

Each commit must be independently green (`npm test` + `npm run build`).

## Risks & mitigations

| Risk | Mitigation |
|---|---|
| Codemod regex matches outside className strings (e.g., a test assertion, a comment, a docstring) | Restrict regex to lines containing the literal `className`. Excluded `**/*.test.tsx`. Review `git diff --stat` before commit 2. |
| Status color migration misses an occurrence → dark mode shows light status badge on dark bg | Acceptance criterion: `grep -E "(bg\|text\|border)-(blue\|...)-(50\|100\|...)"` after step 3 must show ZERO results lacking a `dark:` sibling on the same line. Codify as final sanity check. |
| Tailwind purges `dark:` variants if config doesn't include them | Tailwind v4 + `@theme inline` auto-generates dark variants for theme tokens. For status colors using bracket-arbitrary values (`dark:bg-blue-900/30`), Tailwind v4 also handles these natively. Verified working in PR #69. |
| Vitest snapshots/assertions break (StatusBadge tests assert specific classes) | Update assertions in same commit (step 2 or 3) so vitest stays green. |
| Light-mode visual regression because we changed `text-gray-700` → `text-foreground` and `--foreground=#0f172a` doesn't match `text-gray-700=#374151` exactly | Token resolves to a slightly darker gray (#0f172a is gray-900-ish). User has been seeing both colors during PR #64 testing already; difference is imperceptible in practice. If specific component looks wrong, exception-handle in step 2's diff. |

## Out-of-scope follow-ups (not for this spec)

- Add ESLint rule (`eslint-plugin-tailwindcss` `no-arbitrary-value` + custom token enforcement) to prevent future hardcoded grays from sneaking back in
- Define semantic status tokens (`bg-status-info`, `text-status-success-fg`) and migrate to them — only worth it if/when the design system grows
- Migrate `bom-mobile/` to a similar token-based theme system

## Open questions

None — all 3 brainstorm-stage choices made: D (codemod for grays), B (manual `dark:` for status colors), B (single PR / 4 commits).
