# V2.3-C Phase 2 — Brainstorm: C6 (Override Approved Prices) + C8 (Hard-Delete Customer)

**Date:** 2026-04-27
**Phase:** Brainstorm only — design questions surfaced for user decision before spec.
**Builds on:** V2.3-C Phase 1 (`docs/superpowers/specs/2026-04-26-v23c-admin-override-design.md`) — D1-D10 conventions inherited unless overridden.

---

## Inherited from V23c P1 (carry forward unchanged unless flagged)

- **Permission gate:** Admin only (`[Authorize(Roles="Admin")]`)
- **Reason field:** mandatory `string` ≥ 5 chars
- **Audit:** writes to `AdminAuditLog` via `AdminAuditLogger.Log()` (caller-owns-transaction; enums-as-string serialization)
- **Notification fan-out:** N+1 known issue (carry forward; or fix as P2 cleanup task)
- **Mobile parity:** none in P1 — confirm same for P2 (D8)
- **Web modals:** Dialog + Button primitives; reason field validated client-side
- **Controller location:** `AdminController.cs` is at 303 lines. P1 lesson 8 said "split when adding C6/C8" — proposed split into `AdminRequisitionsController` + `AdminCustomersController` + `AdminAuditLogController` (existing endpoints redistributed in same PR).

---

## C6 — Override Approved Prices

### Schema discovery (informs design)

`QuotationApproval` already has `IsSuperseded: bool` + `SupersededAt: DateTime?` — the schema author anticipated a supersede pattern. This means we don't need to invent a new pattern; we **mark the old approval superseded and create a new one**.

Recommended core flow:
```
1. Validate req status == Approved (and at least one ApprovalItem exists)
2. Snapshot the existing approval (BeforeJson)
3. Mark existing QuotationApproval.IsSuperseded = true; SupersededAt = now
4. Create NEW QuotationApproval with new ApprovalItems (copy unchanged fields, override prices)
5. Audit (BeforeJson = old approval items, AfterJson = new approval items)
6. Status remains Approved
7. Optionally re-issue PDF + email
```

This preserves full history, requires no new entities, and uses a code path the schema clearly anticipated.

### Design questions (need user input)

**Q1 — Scope of override:** which fields per `ApprovalItem` can admin change?

- **Q1a:** `SalesPricePerKgAed` only (most common case)
- **Q1b:** `SalesPricePerKgAed` + `SalesPricePerKgForeign` (currency-aware)
- **Q1c:** all 5 numeric fields including `ProfitMarginPct`, `MaterialCostPct`, `OtherCostPct` (full re-issue powers)

**Default proposed:** Q1c — full re-issue is the operational use case (price *and* margin negotiated externally).

**Q2 — Item set:** can admin override only existing items, or also add/remove items?

- **Q2a:** Only override existing items' prices (item set frozen)
- **Q2b:** Allow add/remove (large blast radius — touches RequisitionItem table too)

**Default proposed:** Q2a. Adding/removing items goes through C2 rollback → BomCreator path. Override = price-only.

**Q3 — PDF re-issue:** what happens to the PDF?

- **Q3a:** Generate new PDF + auto-send email (mirroring original approval email flow)
- **Q3b:** Generate new PDF, return URL/blob to admin, no auto-email
- **Q3c:** No PDF at all (admin handles externally)

**Default proposed:** Q3a (auto-send) — mirrors what the original MD approval did. But this means **the customer receives a 2nd email**, possibly confusingly. Alternative: gate the email on a `sendEmail: bool` flag in the request body, defaulting to true.

**Q4 — Currency / exchange rate snapshot:** the original approval has a snapshotted rate. On override:

- **Q4a:** Re-snap (use today's rate)
- **Q4b:** Preserve original rate (override only the AED price, keep foreign-currency conversion intact)

**Default proposed:** Q4a — typically a price override happens because the market moved; new approval needs current rate.

**Q5 — Notification scope:**

- Original SP: yes (their approved quote was changed)
- Original MD (approver): yes (their decision was overridden — accountability)
- Accountant: yes (book-keeping notice)
- Customer: indirectly via email (Q3a) but **no SignalR notif**

**Default proposed:** SP + MD + Accountant in `Notifications` table; SignalR push to all 3.

**Q6 — Reversibility:** can a C6 override be undone (admin reverts to prior approval)?

- **Q6a:** No — irreversible by design (audit log is the trail). Reverting requires another C6 with the prior values.
- **Q6b:** Yes — special endpoint that toggles `IsSuperseded` back. (Complicates schema reasoning.)

**Default proposed:** Q6a. A second C6 with old values *is* the revert.

**Q7 — Status during override:** does the req leave Approved?

- **Q7a:** No — stays Approved throughout. New QuotationApproval immediately superseding the old.
- **Q7b:** Yes — momentarily moves to a special "Overriding" status, then back to Approved.

**Default proposed:** Q7a. Atomic transaction; no intermediate status.

---

## C8 — Hard-Delete Customer

### Schema discovery

`Customer` is referenced by:
- `QuotationRequest.CustomerId` (FK, NOT nullable, RESTRICT by default)
- `CustomerChangeHistory.OldCustomerId` + `NewCustomerId` (audit log of branch swaps' parallel customer-swap log)

No other direct FK. So cascade scope is bounded.

### Design questions (need user input)

**Q8 — FK strategy:**

- **Q8a:** Block delete if ANY req references this customer (return 409 Conflict + list of referencing req-IDs). Mirrors V23a Branch DELETE behavior (soft-delete with in-use guard). Safest. **But this means high-traffic customers can never be deleted — defeats the purpose.**
- **Q8b:** Cascade-delete reqs (massive blast radius — destroys financial history). **STRONGLY DISCOURAGED.**
- **Q8c:** Anonymize-in-place: replace customer Name/Email/Phone/Address with `<deleted>` markers, keep the row + PK so reqs still resolve. GDPR-compliant for "right to erasure." Audit trail preserved.
- **Q8d:** Hard-delete + null-out FK on referencing reqs (requires schema change to make `CustomerId` nullable + cascade-set-null). Reqs become "orphan" but survive.

**Default proposed:** **Q8c (anonymize-in-place)**. Reasoning:
1. Preserves financial reporting/audit (req history intact)
2. Achieves GDPR-style erasure of PII (Name/Email/Phone/Address are the regulated fields)
3. No schema migrations needed (existing `Customer` columns rewritten)
4. Customer's *PK* stays — needed for FK integrity in reqs
5. Soft-delete semantics: introduce `Customer.IsDeleted: bool` + `Customer.DeletedAt: DateTime?` so the customer hides from `GET /api/customers` and pickers, but historical reqs still display "(deleted customer)"

**Caveat for Q8:** if user wants TRUE hard-delete (Q8d) for strong GDPR compliance, that's a bigger schema change (migration to make `CustomerId` nullable on `QuotationRequest`, `CustomerChangeHistory.Old/NewCustomerId`). The migration is non-trivial because existing data must be preserved.

**Q9 — Anonymization marker text** (only relevant if Q8c chosen):

- Name → `"<deleted-customer-#${id}>"` or `"<redacted>"` or empty string?
- Email → empty / `"deleted@example.invalid"` / `"<redacted>"`?
- Phone → empty / `"<redacted>"`?
- Address → empty / `"<redacted>"`?

**Default proposed:** All four → empty string `""`, plus a synthetic `Name = "[Deleted on YYYY-MM-DD]"` so listing screens can surface it without breaking layouts.

**Q10 — Block conditions:** even with Q8c, should we block deletion in certain situations?

- Customer has Approved reqs in the last 90 days (warranty / dispute window)?
- Customer has any pending workflow (BomPending / CostingPending / MdReview)?
- Active SP relationship (`Customer.SalesPersonId` is set)?

**Default proposed:** block on **active workflow only** (BomPending / BomInProgress / CostingPending / CostingInProgress / MdReview). Approved/Rejected reqs are historical and don't block. Return 409 Conflict + list of in-flight reqs that must be resolved first.

**Q11 — Notification scope:**

- SPs who own customer? Yes (their customer is gone)
- All members of SP's group? Yes — peer-pool members lose visibility too
- Accountant? Optional — they care about customer history for audit
- MD? Optional

**Default proposed:** SP + group peers (V23b) + Accountant. SignalR push.

**Q12 — Audit BeforeJson scope:**

- Just the Customer row?
- Customer row + full list of referenced req-IDs (so audit can answer "what did this customer have")?

**Default proposed:** Customer row + array of referenced req-IDs (lightweight; full reqs not needed since they're still in DB).

**Q13 — Reversibility (Q8c only):** can admin "undelete" / re-anonymize?

- **Q13a:** No — admin must use C7-style customer-create flow with new PK if customer needs to come back.
- **Q13b:** Yes — admin can edit customer fields back, but `IsDeleted` flag stays true unless we add a separate "restore" endpoint.

**Default proposed:** Q13a. Hard-delete (even soft) is one-way for simplicity.

---

## Cross-cutting design questions

**Q14 — Mobile parity:** P1 was web-only. Do C6 and C8 also stay web-only?

**Default proposed:** Yes, web-only. Consistent with P1 scope. Mobile UI is a P3 if ever needed.

**Q15 — Controller split:** when adding C6 + C8, do we split `AdminController.cs` into `AdminRequisitionsController` + `AdminCustomersController` + `AdminUsersController` + `AdminAuditLogController`?

**Default proposed:** Yes. Carry the existing 7 P1 endpoints to their new homes in the same PR. `AdminController` becomes a thin shell or is deleted entirely. Naming is consistent with existing feature-slice convention.

**Q16 — N+1 notification cleanup:** while we're touching admin endpoints, do we extract `NotificationService.SendToUsersAsync(IEnumerable<int>, …)` and migrate the 7 P1 endpoints + 2 new P2 endpoints to use it?

**Default proposed:** Yes — small additive, eliminates the N+1 in-flight technical debt.

**Q17 — Audit-log filter UI gaps:** P1 audit log page lacks `adminUserId` and `entityId` filter inputs. Patch as part of P2?

**Default proposed:** Yes (~30 lines of UI work, native fit).

**Q18 — Implementation cadence:** subagent-driven-development like P1?

**Default proposed:** Yes for foundation work (entities, migrations, service, audit, base endpoints). Follow-up cosmetic work (modals, page integration) can use lighter single-reviewer dispatch per P1 lesson 2.

---

## Estimated scope

| Slice | Tasks | Time |
|---|---|---|
| Schema (Customer.IsDeleted/DeletedAt; no new entities for C6 — uses existing IsSuperseded) | 1 migration | 30 min |
| `AdminCustomersController.HardDeleteCustomer` (C8) — anonymize + audit + notif | 1 endpoint + tests | ~90 min |
| `AdminRequisitionsController.OverridePrices` (C6) — supersede + new approval + audit + notif + PDF re-issue | 1 endpoint + tests + PDF reuse | ~3 hr |
| Controller split refactor | code-only move | ~45 min |
| `NotificationService.SendToUsersAsync` extraction + migrate 9 endpoints | refactor + tests | ~60 min |
| `AdminAuditLogger` consumers updated for 2 new ActionTypes (`OverridePrices`, `HardDeleteCustomer`) | enum + audit infra | ~15 min |
| `NotificationType` enum: 2 new values (`PricesOverridden`, `CustomerDeleted`) | enum | ~10 min |
| Web hooks: `useOverridePrices`, `useHardDeleteCustomer` (typed React Query) | hooks | ~30 min |
| Web modals: `OverridePricesModal` (item table editor), `DeleteCustomerModal` (confirmation + req list) | UI | ~2 hr |
| Wire `AdminActionsCard` for C6 (Approved-only button); `CustomersPage` row action for C8 | integration | ~30 min |
| Audit log filter UI gap fix (Q17) | UI | ~30 min |
| `/change-password` + ForceChangePasswordGuard sanity (no changes expected; verify) | check | ~10 min |
| **Total** | ~ 22 tasks | **~ 9-10 hr at P1 cadence** |

This is **roughly 2× the V23c P1 scope**. Subagent-driven-development with strict review = high quality but slow (P1 was ~5 hr / 25 tasks). At P1 cadence, P2 = 9-10 hr.

---

## Decision request

Please confirm/override these defaults and we'll proceed to spec. The defaults marked above are reasonable starting positions, not commitments.

**Compact decision form** (paste back with your picks):

```
Q1 — Override scope:        [a / b / c]   default c
Q2 — Item set:               [a / b]      default a
Q3 — PDF re-issue:           [a / b / c]  default a (with sendEmail flag)
Q4 — Exchange rate:          [a / b]      default a
Q5 — Notif scope:            [confirm SP+MD+Acc+SignalR] / [override]
Q6 — Reversibility:          [a / b]      default a
Q7 — Intermediate status:    [a / b]      default a
Q8 — FK strategy:            [a / b / c / d]  default c (anonymize-in-place + IsDeleted flag)
Q9 — Anonymization markers:  [confirm "[Deleted YYYY-MM-DD]" + empty contacts] / [override]
Q10 — Block conditions:       [confirm active-workflow-only] / [override]
Q11 — Notif scope:            [confirm SP+peers+Acc] / [override]
Q12 — Audit BeforeJson:       [confirm row + ref req-IDs] / [override]
Q13 — Reversibility (C8):     [a / b]      default a
Q14 — Mobile parity:          [yes / no]   default yes (no mobile)
Q15 — Controller split:       [yes / no]   default yes
Q16 — N+1 cleanup bundled:    [yes / no]   default yes
Q17 — Audit filter UI patch:  [yes / no]   default yes
Q18 — Subagent cadence:       [strict P1 / streamlined / hybrid]   default hybrid
```

Once decisions land, I'll write `docs/superpowers/specs/2026-04-27-v23c-p2-c6-c8-design.md` with locked D1-Dn and detailed entity/endpoint definitions.
