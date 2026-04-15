# Exchange Rates Management — Design

## Goal

Give all logged-in users a read-only view of current exchange rates. Give Accountants the ability to add new rates and edit existing ones (rate, effective date, active status).

## Background

The costing submission endpoint rejects submissions if no active exchange rate exists for a foreign currency. Currently there is no frontend to manage these rates — Accountants must contact a developer to insert records directly. This page closes that gap.

## Backend API

All endpoints are at `/api/exchange-rates` (no branch scoping — rates are global).

| Method | Path | Auth | Purpose |
|---|---|---|---|
| GET | `/api/exchange-rates` | Any | All rates, ordered by effective date desc, includes SetByName |
| POST | `/api/exchange-rates` | Accountant | Create a new rate |
| PUT | `/api/exchange-rates/{id}` | Accountant | Update rate, date, and active flag |

**Create payload:** `{ currencyCode, currencyName, rateToAed, effectiveDate }`
**Update payload:** `{ rateToAed, effectiveDate, isActive }`
**Response shape:** `{ id, currencyCode, currencyName, rateToAed, effectiveDate, isActive, setByName }`

Note: `currencyCode` and `currencyName` cannot be changed after creation.

## Files

| File | Purpose |
|---|---|
| `bom-web/src/features/exchange-rates/exchangeRatesApi.ts` | TanStack Query hooks: `useExchangeRates`, `useCreateRate`, `useUpdateRate` |
| `bom-web/src/features/exchange-rates/ExchangeRatesPage.tsx` | List page with DataTable, role-gated action buttons |
| `bom-web/src/features/exchange-rates/AddRateModal.tsx` | Create modal (Accountant only) |
| `bom-web/src/features/exchange-rates/EditRateModal.tsx` | Edit modal (Accountant only) |
| `bom-web/src/features/exchange-rates/ExchangeRatesPage.test.tsx` | Vitest + RTL tests |
| `bom-web/src/App.tsx` | Add `/exchange-rates` route (no role restriction) |
| `bom-web/src/components/layout/Sidebar.tsx` | Add "Exchange Rates" nav link |
| `bom-web/src/types/api.ts` | Add `ExchangeRate` type |

## Page Layout

**Header:** "Exchange Rates" heading + "Add Rate" button (Accountant only, top-right)

**Table columns:**
- Currency Code — monospace, bold
- Currency Name
- Rate to AED — 4 decimal places
- Effective Date — formatted as `YYYY-MM-DD`
- Status — Active (green badge) / Inactive (grey badge)
- Set By — name of the user who created it
- Actions — edit (pencil icon) + toggle active/inactive (ban/check icon), Accountant only

## Add Rate Modal

Fields:
- **Currency Code** — text input, required, auto-uppercased on change, e.g. `USD`
- **Currency Name** — text input, required, e.g. `US Dollar`
- **Rate to AED** — number input, step 0.0001, required, must be > 0
- **Effective Date** — date input, required

Submits POST `/api/exchange-rates`. On success: close modal, invalidate list.

## Edit Rate Modal

Pre-populated from the selected row. Fields:
- **Rate to AED** — number input, step 0.0001, required, must be > 0
- **Effective Date** — date input, required
- **Active** — checkbox

Currency Code and Currency Name are displayed as read-only text (not inputs).

Submits PUT `/api/exchange-rates/{id}`. On success: close modal, invalidate list.

## Access Control

- "Add Rate" button: visible only when `role === "Accountant"`
- Edit and toggle action column: rendered only when `role === "Accountant"`
- All other roles see the table with no action column

## Types

```typescript
// Add to src/types/api.ts
export interface ExchangeRate {
  id: number;
  currencyCode: string;
  currencyName: string;
  rateToAed: number;
  effectiveDate: string; // ISO date string
  isActive: boolean;
  setByName: string;
}

export interface CreateExchangeRateRequest {
  currencyCode: string;
  currencyName: string;
  rateToAed: number;
  effectiveDate: string;
}

export interface UpdateExchangeRateRequest {
  rateToAed: number;
  effectiveDate: string;
  isActive: boolean;
}
```

## Tests

Vitest + RTL tests covering:
1. Table renders rows from API response
2. "Add Rate" button not visible for non-Accountant roles
3. "Add Rate" button visible for Accountant
4. Submitting Add Rate modal calls POST with correct payload
5. Submitting Edit Rate modal calls PUT with correct payload
6. Toggling active status calls PUT with `isActive` flipped
