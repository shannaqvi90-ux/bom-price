# PDF Redesign (Letterhead Classic) + Admin Company Settings

**Date:** 2026-05-03
**Author:** Shan + Claude (Opus 4.7)
**Status:** Design — pending writing-plans

## Goal

Replace the current "Modern Corporate" quotation PDF (shipped via PR #87) with a traditional **Letterhead Classic** style, and move all hardcoded company-level fields (name, address, telephone, TRN, email, website, validity, T&C) into a new **Admin → Company Settings** page so the Admin can edit them without a redeploy.

## Why now

Two problems with the current PDF:

1. **Visual style** — Shan does not like the current Modern Corporate look. After reviewing 8 design directions (A-H), he selected **Option E (Letterhead Classic)** with refinements: no logo box, sales person name + email shown top-right next to Bill To, single MD signature only, no footer.
2. **Hardcoded content** — `info@fujairahplastic.com`, the 5-item Terms & Conditions block, and the 30-day validity window are all hardcoded in `BomPriceApproval.API/Infrastructure/Services/PdfService.cs`. Every wording change requires a code commit + Fly redeploy.

Moving these to a singleton `CompanySettings` table lets the Admin update them from the web UI in seconds.

## Scope

**In scope:**

- Backend: new `CompanySettings` entity (singleton), EF migration with seed, `AdminCompanySettingsController` (GET + PUT), `AdminActionType.UpdateCompanySettings` audit value.
- Backend: `PdfService.GenerateQuotationAsync` rewritten to (a) read from `CompanySettings` and (b) render the Letterhead Classic layout.
- Frontend: new `/admin/company-settings` page (Admin role only), reachable from the sidebar.
- **Two distinct emails** in the new PDF:
  1. **Salesperson email** — top-right "Sales Representative" block, sourced from `User.Email` of the requisition's `SalesPerson` (per-quote contact). Replaces the old footer's `info@` line.
  2. **Company email** — letterhead contact strip (Tel · TRN · Email · Web), sourced from `CompanySettings.Email` (company-level info@ address).
- Notes section removed from PDF.
- Tests: backend integration tests for GET/PUT + PDF regeneration smoke (existing `PdfService` callers continue to work).

**Out of scope:**

- Mobile UI for company-settings (web-only — admin tasks already gated to web per project decision).
- Per-branch overrides (V3 is Alain-only; singleton suffices).
- T&C placeholder substitution (e.g. auto-rewriting "30 days" when validity changes) — admin manages T&C wording manually.
- Logo upload / image rendering (mockup intentionally has no logo; user explicitly removed it).
- Salesperson signature image (user explicitly removed; only MD signature remains).
- Fields beyond the locked list (no Address line 2, no fax, no IBAN, etc.) — add later if needed.
- Versioning of `CompanySettings` history beyond the single `UpdatedAt`/`UpdatedByUserId` audit columns + the `AdminAuditLog` row written on each save.

## Approach

### High level

A new `CompanySettings` table with exactly **one row** (id = 1, enforced by seed + check constraint). `PdfService` reads this row at the start of `GenerateQuotationAsync` and uses its values for letterhead, validity computation, and T&C rendering. The admin endpoints PUT-update the row in place (no insert/delete from the API layer).

### Data model

Single new entity, single new table, single seeded row.

```csharp
public class CompanySettings
{
    public int Id { get; set; }                       // always 1
    public string CompanyName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Telephone { get; set; } = string.Empty;
    public string Trn { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Website { get; set; } = string.Empty;
    public int QuotationValidityDays { get; set; } = 30;
    public string TermsAndConditions { get; set; } = string.Empty; // multi-line
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
}
```

EF mapping:

- `Id` is the PK; **no auto-generation** (`ValueGeneratedNever()`) — seeded as 1.
- All string columns use default text (no length cap; T&C can be long).
- `QuotationValidityDays` ≥ 1 (validated in PUT handler, not a DB check constraint — keep migration simple).
- `UpdatedByUserId` FK to `Users` with `OnDelete(DeleteBehavior.SetNull)` — admin-user deletion shouldn't break settings.

### Migration & seed

Single EF migration `AddCompanySettings`:

1. `CREATE TABLE "CompanySettings"` with the columns above.
2. Seed one row (id = 1) with the values currently hardcoded in `PdfService.cs`:
   - `CompanyName = "FUJAIRAH PLASTIC FACTORY"`
   - `Address = "Fujairah, United Arab Emirates"` (current PdfService line 101; user can enrich via UI after deploy — V3 is Alain-only but address text is admin-editable)
   - `Telephone = ""` (currently absent in PDF; user fills in via UI)
   - `Trn = ""`
   - `Email = "info@fujairahplastic.com"` (current PdfService line 332)
   - `Website = ""`
   - `QuotationValidityDays = 30` (current PdfService line 121)
   - `TermsAndConditions =` the 5 lines currently hardcoded at lines 278-285, joined with `\n`.
   - `UpdatedAt = DateTime.UtcNow`, `UpdatedByUserId = NULL`.

This guarantees the **first deploy is a visible style change but a content no-op** for the data fields — admin can then edit at leisure.

### API endpoints

New `AdminCompanySettingsController` (route `[Route("api/admin/company-settings")] [Authorize(Roles = "Admin")]`):

| Method | Route | Body | Returns | Audit |
|---|---|---|---|---|
| `GET` | `/api/admin/company-settings` | — | full settings DTO | no |
| `PUT` | `/api/admin/company-settings` | `UpdateCompanySettingsRequest` | updated settings DTO | yes — `AdminActionType.UpdateCompanySettings`, EntityType `"CompanySettings"`, EntityId 1, BeforeJson + AfterJson |

DTOs:

```csharp
public record CompanySettingsDto(
    string CompanyName, string Address, string Telephone, string Trn,
    string Email, string Website, int QuotationValidityDays,
    string TermsAndConditions, DateTime UpdatedAt, string? UpdatedByName);

public record UpdateCompanySettingsRequest(
    string CompanyName, string Address, string Telephone, string Trn,
    string Email, string Website, int QuotationValidityDays,
    string TermsAndConditions, string Reason);
```

Validation in PUT handler:

- `Reason` ≥ 5 chars (matches existing admin-action convention).
- `CompanyName` not empty.
- `QuotationValidityDays` between 1 and 365.
- `Email` if non-empty must contain `@`.
- All string fields trimmed; `TermsAndConditions` line endings normalized to `\n`.

PUT updates the row in place (no concurrency token; admin is single-tenant + low-frequency edits — last write wins is fine).

### Audit

New enum value `AdminActionType.UpdateCompanySettings` appended to existing list. Logged via existing `AdminAuditLogger.Log(...)` pattern (caller owns the SaveChanges transaction — same as `ResetPassword`).

Audit row visible in existing `/admin/audit-log` page (already supports filtering by ActionType; new value will appear in the dropdown after frontend types regenerate).

### PDF rendering changes (`PdfService.cs`)

Wholesale rewrite of `GenerateQuotationAsync`. Key structural changes from current implementation:

1. **Load `CompanySettings`** at top of the method (`db.CompanySettings.FirstOrDefaultAsync(s => s.Id == 1)`); fall back to a hardcoded defensive default if for any reason the row is missing (shouldn't happen post-migration but defensive coding for V3 cutover-style scenarios).
2. **Replace QuestPDF tree** with the Letterhead Classic layout (see "PDF layout" below).
3. **Salesperson email** sourced from `fullReq.SalesPerson?.Email` — already loaded by the existing `Include(r => r.SalesPerson)` chain.
4. **Validity** = `approval.ApprovedAt.AddDays(settings.QuotationValidityDays)` (replaces hardcoded `AddDays(30)`).
5. **Terms** rendered by splitting `settings.TermsAndConditions` on `\n`, trimming, dropping blank lines, prefixing each with `"{i}. "`.
6. **Notes section deleted** — `approval.Notes` is no longer rendered (per user instruction). The DB column stays (no migration to drop), but PDF ignores it.
7. **Footer deleted** — page layout no longer has a `page.Footer()`. Page numbers also dropped (sample mockup is single-page; multi-page quotes are extremely rare in this app — PDFService didn't even paginate the items table previously).

The V3 `ComputeV3PricePerKg` helper and the V2.3 fallback price logic stay untouched — only the cosmetic rendering changes.

### PDF layout (Letterhead Classic)

Single A4 page (margins ~32px horizontal, 36px vertical), font family "Times New Roman", default font size 10pt:

```
┌─────────────────────────────────────────────────┐
│            FUJAIRAH PLASTIC FACTORY             │  ← 22pt brand-blue, centered
│   P.O. Box 12345, Industrial Area, Alain, UAE   │  ← 10pt muted, centered
│   Tel: ... · TRN: ... · email · website         │  ← 9.5pt muted, centered
│ ─────────────────────────────────────────────── │  ← 2px brand-blue rule
│                                                 │
│              ── SALES QUOTATION ──              │  ← 14pt centered, underlined
│                                                 │
│ Ref: REQ-…  Date: 03 May 2026  Valid: 02 Jun…  │  ← single dotted-rule strip
│ ............................................... │
│                                                 │
│ ┌─ Bill To ────────┐  ┌─ Sales Representative ─┐│
│ │ ABC Plastics LLC │  │ Ahmed Khan              ││  ← grid 2-col
│ │ CUST-0042        │  │ Sales Department        ││
│ │ PO Box, Dubai    │  │ ahmed.khan@fpf.com      ││
│ │ orders@…         │  │ +971 …                  ││
│ └──────────────────┘  └─────────────────────────┘│
│                                                 │
│ Dear Sir/Madam, with reference to your enquiry, │
│ we are pleased to submit our quotation as below:│
│                                                 │
│ ──────────────────────────────────────────────  │  ← table top rule
│ S# │ Description       │ Qty │ Rate │ Amount    │  ← table headers
│ ──────────────────────────────────────────────  │
│ 1  │ HDPE 5L Container │ 5k  │ 8.45 │ 42,250.00 │  ← dotted row separators
│ 2  │ PP Cap            │ 5k  │ 0.68 │  3,400.00 │
│ 3  │ PE Sleeve         │ 5k  │ 0.42 │  2,100.00 │
│ ──────────────────────────────────────────────  │
│ TOTAL AMOUNT                  AED 47,750.00     │  ← double-rule, 12pt brand-blue
│ ──────────────────────────────────────────────  │
│                                                 │
│ Terms & Conditions:                             │  ← underlined heading
│  1. This quotation is valid for 30 days …       │
│  2. Prices are subject to change …              │
│  3. Payment terms: 50% advance, 50% delivery    │
│  4. Delivery: Ex-Works Alain …                  │
│  5. UAE jurisdiction.                           │
│                                                 │
│                       ┌────── (signature) ──────┐│  ← right-aligned, single sig
│                       │   For Fujairah Plastic  ││
│                       │   Managing Director     ││
│                       └─────────────────────────┘│
└─────────────────────────────────────────────────┘
```

Brand colors retained: `#1e3a8a` (BrandDark) for headline + rules, `#0f172a` (Text), `#475569` (muted), `#94a3b8` (faint dotted dividers). No `BrandSoft` card backgrounds — this style is rule-based, not card-based.

Currency display: same as current — `Currency: AED` in the meta strip, `AED 47,750.00` in total. Non-AED quotes show the existing exchange-rate disclosure line above the T&C section (line 256 of current PdfService).

### Web UI — `/admin/company-settings`

New page in `bom-web/src/features/admin/`:

- Single form, two visual sections ("Letterhead", "Quotation Defaults", "Terms & Conditions" — three actually).
- Letterhead section: Company Name (full-width input), Address (full-width input), then a 2-column grid for Telephone / TRN / Email / Website.
- Quotation Defaults section: single number input for `QuotationValidityDays` (right-aligned, 1-365 range hint).
- T&C section: full-width `<textarea rows={8}>` with monospace font and a one-line hint "Each non-empty line becomes a numbered point".
- Bottom: "Discard Changes" (secondary, reverts the form to last-fetched server values — purely client-side, no API call), "Save Changes" (primary, calls PUT).
- Save success → toast "Company settings updated"; failure → existing per-field error envelope from `Validation.Detail(...)` rendered next to the relevant input.
- Reason field for the audit row: surfaced as a small textarea labeled "Reason for change" right above the Save button (≥ 5 chars validation, matches other admin actions).
- Sidebar nav: new entry "Company Settings" placed next to the existing `/admin/audit-log` link, gated by Admin role (using whatever role-gate primitive that link already uses — verify in implementation).

Routing: lazy-loaded route `/admin/company-settings`, registered next to the existing `/admin/audit-log` route.

API hooks: new `useCompanySettings()` (GET) + `useUpdateCompanySettings()` (PUT mutation) in `bom-web/src/features/admin/api/`. TanStack Query cache key `["admin", "company-settings"]`. Mutation invalidates the same key on success.

### Tests

Backend (`BomPriceApproval.Tests/Admin/`):

- `CompanySettingsTests.Get_ReturnsSeededRow` — fresh DB → GET returns the 9 seeded fields.
- `CompanySettingsTests.Put_AsAdmin_UpdatesAndAudits` — admin PUT; verify row updated, `UpdatedByUserId` set, `AdminAuditLog` row written with `ActionType=UpdateCompanySettings`, BeforeJson/AfterJson snapshots present.
- `CompanySettingsTests.Put_AsNonAdmin_Returns403` — sales/accountant/MD all denied.
- `CompanySettingsTests.Put_InvalidValidityDays_Returns400` — 0, -1, 366 all rejected; per-field error.
- `CompanySettingsTests.Put_EmptyReason_Returns400`.
- `CompanySettingsTests.Put_EmptyCompanyName_Returns400`.

Backend PDF smoke (existing test surface — no new file):

- Existing `PdfService` callers (Approve flow, OverridePrices flow) continue to compile + emit a non-empty byte array. No visual regression test infra; manual verification on prod.

Web (`bom-web/src/features/admin/`):

- Component tests if existing convention; otherwise manual smoke on the dev server.

## Risks & open questions

| Risk | Mitigation |
|---|---|
| Singleton ID enforcement bypassed via direct EF insert | Admin endpoints only PUT (no POST/DELETE); seeded row's existence is the contract. |
| Old approvals (pre-deploy) reload with new settings if PDF is regenerated → "Valid Until" might shift | Acceptable — the only way to "regenerate" an old PDF is via Admin C6 OverridePrices, which already creates a new approval row with its own ApprovedAt. New PDFs respect the current settings. |
| T&C wording becomes stale (e.g. validity changed but text still says "30 days") | Documented in admin-page hint; no auto-substitution. |
| Long T&C overflows single page | QuestPDF auto-paginates the column flow; signature block may end up on page 2 — acceptable, current PDF has the same behavior. Verify with manual smoke. |
| Salesperson with no email → footer/header email blank | Existing `User.Email` is non-nullable string in the entity; safe. |
| Non-Alain branch reactivated post-V3 → letterhead still says "Alain" if address hardcoded | Address is now fully admin-editable text; admin updates it. Out of scope for this spec. |

## Definition of done

- New `CompanySettings` table exists in dev + Neon production with the seeded row.
- `/api/admin/company-settings` GET + PUT endpoints implemented, audited, tested.
- `PdfService` rewritten to read from `CompanySettings`; Letterhead Classic layout shipped; Notes block + footer removed; salesperson email used in place of company email where shown next to customer.
- `/admin/company-settings` page reachable, renders form with seeded values, saves successfully.
- Backend tests green; manual web smoke passes; manual PDF download from a real approved req on prod looks like the mockup.

## Cutover notes

- No data migration beyond the seed insert. Existing `QuotationApproval.Notes` rows stay in DB (PDF just stops reading them).
- Single deployment: ship migration + backend + frontend together. Admin can edit values immediately after first login post-deploy.
- Tag commit `pdf-redesign-letterhead-classic` for traceability.
