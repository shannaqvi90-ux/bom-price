# Login Lockout UX — Design Spec

**Date:** 2026-05-12
**Author:** Shan + Claude (brainstorming session)
**Status:** APPROVED — ready for implementation plan
**Scope:** Web frontend (`bom-web`) + backend (`BomPriceApproval.API`). Mobile (`bom-mobile`) deferred.

---

## Problem

Users have no visibility into account-lockout state during login on production (`https://bom-fpf.pages.dev`).

Concrete incident on 2026-05-12: a user (`a.ashqar@fujairahplastic.com`) attempted login ~30 times between 09:58–10:00 UTC, got locked at attempt 5 (`LockedUntil = 10:05:52Z`), but the web UI gave no indication of *why* further attempts kept failing nor when the lockout would clear. The user kept retrying for ~2 minutes after the lock expired before realising they could log in again.

Root cause is twofold:

1. **Backend opaqueness.** Every wrong-password response is `401 { "message": "Invalid credentials" }` regardless of how many attempts remain. The lockout response is `400 application/problem+json { detail: "Account temporarily locked...", errors: { Email: ["Account locked."] } }` — but it omits the `LockedUntil` timestamp, so the client cannot show a countdown.
2. **Frontend parser gap.** `LoginPage.tsx` reads only `response.data.message`. Since the lockout response uses `detail` (per RFC 7807 ProblemDetails), the actual reason gets dropped and the user sees the generic fallback string `"Login failed"`.

Additional secondary bug discovered during design: when the 5th wrong-password attempt triggers the lockout, the backend STILL returns the generic 401 `"Invalid credentials"` — the lockout response is only emitted on the *next* (6th) attempt. So the lockout is invisible until the user tries one more time.

---

## Decisions log (from brainstorming)

| # | Question | Decision |
|---|---|---|
| Q1 | Pre-lockout warning policy | **(d) Counter on every wrong attempt** — show `attemptsRemaining` on every failed login. Accepts the enumeration leak (attacker can distinguish "known email + wrong password" from "unknown email" because only the former carries the counter). Acceptable for an internal app with ~5 staff. |
| Q2 | Lockout message display style | **(c) Real-time countdown** — backend returns `lockoutSecondsRemaining` (integer seconds), frontend ticks down `mm:ss` every 1 s. Auto-hides banner + re-enables Sign In button at 0. |
| Q3 | Mobile parity | **(a) Web only** — backend response shape change is additive and won't break the shipped mobile APK (which already falls back to a generic "Login failed" string). Mobile UX will catch up on the next mobile rebuild cycle (D-3 carry-over). |
| Q4a | Lockout policy values | **(a) Keep as-is** — 5 wrong attempts → 15-min lockout. No change to the lockout mechanism itself; only the response shape and frontend rendering. |
| Q4b | Forgotten-password help text | **(ii) Countdown + admin-reset hint** — the lockout copy says "If you forgot your password, contact your administrator." No new self-service `/forgot-password` flow. |

---

## Approach

**Approach A — In-place augment.** Modify existing login responses to add new fields without changing the top-level error shape. Backward-compatible with the shipped mobile APK (mobile reads `data.message`); new fields are additive and consumed by the web client only.

Rejected alternatives: unifying every login error to ProblemDetails (breaking change to mobile); a companion `/api/auth/login-status` GET (doubles round-trips, easier enumeration target).

---

## API contract

### `POST /api/auth/login` — same URL, same request shape.

**Case 1 — Unknown email:** unchanged.
```http
HTTP/1.1 401 Unauthorized
Content-Type: application/json

{ "message": "Invalid credentials" }
```
No `attemptsRemaining` field. Asymmetry with Case 2 is the deliberately-accepted enumeration leak (Q1).

**Case 2 — Wrong password, attempts 1–4:** new field added.
```http
HTTP/1.1 401 Unauthorized
Content-Type: application/json

{ "message": "Invalid credentials", "attemptsRemaining": 4 }
```
`attemptsRemaining` counts down `4 → 3 → 2 → 1` as `FailedLoginAttempts` increments.

**Case 3 — Wrong password, attempt 5 (lockout just triggered):** behavioural fix + new field. Previously returned generic 401; now returns the lockout response directly.
```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "title": "One or more validation errors occurred.",
  "detail": "Account temporarily locked due to too many failed login attempts. If you forgot your password, contact your administrator.",
  "status": 400,
  "errors": { "Email": ["Account locked."] },
  "lockoutSecondsRemaining": 900
}
```

**Case 4 — Login attempt during active lockout:** new field added (otherwise unchanged).
```http
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{
  "title": "One or more validation errors occurred.",
  "detail": "Account temporarily locked due to too many failed login attempts. If you forgot your password, contact your administrator.",
  "status": 400,
  "errors": { "Email": ["Account locked."] },
  "lockoutSecondsRemaining": 712
}
```
`lockoutSecondsRemaining` is recomputed on every request from `LockedUntil - UtcNow`, always ≥ 1.

### Why `lockoutSecondsRemaining` (integer) instead of `lockedUntil` (ISO timestamp)

- Immune to client clock skew (client decrements from N, no clock comparison).
- Simpler frontend parsing (no `Date` math).
- Matches RFC 6585 `Retry-After` convention.
- Drawback: not human-readable in logs. Mitigation: backend already logs `LockedUntil` separately as a timestamp on every lockout event.

### Edge-case behaviours

| Case | Backend response |
|---|---|
| User typed correct password while locked | Lockout check runs BEFORE bcrypt verify (existing). Returns Case 4 regardless of password correctness. |
| `FailedLoginAttempts >= 5` but `LockedUntil` is null (DB anomaly) | Defensive: 401 generic `Invalid credentials`, audit warning logged. No `attemptsRemaining` field. |
| Successful login on a previously-failed account | All counters reset (`FailedLoginAttempts = 0`, `LockedUntil = null`) — existing behaviour, unchanged. |
| Admin password reset (C7) | Already clears `FailedLoginAttempts` + `LockedUntil` — existing behaviour, unchanged. |

---

## Backend implementation outline

### `BomPriceApproval.API/Infrastructure/Validation/Validation.cs`

Extend `ValidationProblemBuilder` with one method to attach RFC 7807 extension members:

```csharp
public ValidationProblemBuilder Extension(string key, object? value)
{
    _extensions[key] = value;
    return this;
}
```

`Return()` copies `_extensions` into `ValidationProblemDetails.Extensions` before returning.

### `BomPriceApproval.API/Features/Auth/AuthController.cs`

In `Login`:

```csharp
const int MaxAttempts = 5;
const int LockoutMinutes = 15;

if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
{
    logger.LogWarning("[Audit] Login rejected: account locked {UserId} {Email} LockedUntil={LockedUntil}",
        user.Id, user.Email, user.LockedUntil);
    var secondsLeft = Math.Max(1, (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalSeconds));
    return Validation
        .Detail("Account temporarily locked due to too many failed login attempts. If you forgot your password, contact your administrator.")
        .Field("Email", "Account locked.")
        .Extension("lockoutSecondsRemaining", secondsLeft)
        .Return();
}

if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
{
    user.FailedLoginAttempts++;
    if (user.FailedLoginAttempts >= MaxAttempts)
        user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
    await db.SaveChangesAsync();

    logger.LogWarning("[Audit] Login failed: wrong password {UserId} {Email} Attempts={Attempts} Locked={Locked}",
        user.Id, user.Email, user.FailedLoginAttempts, user.LockedUntil is not null);

    if (user.LockedUntil is not null)
    {
        // Attempt 5 just locked them — emit lockout response, not generic 401
        var secondsLeft = Math.Max(1, (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalSeconds));
        return Validation
            .Detail("Account temporarily locked due to too many failed login attempts. If you forgot your password, contact your administrator.")
            .Field("Email", "Account locked.")
            .Extension("lockoutSecondsRemaining", secondsLeft)
            .Return();
    }

    int remaining = Math.Max(0, MaxAttempts - user.FailedLoginAttempts);
    return Unauthorized(new { message = "Invalid credentials", attemptsRemaining = remaining });
}
```

Hardcoded `MaxAttempts = 5` and `LockoutMinutes = 15` move from the original inline literals to named constants but keep the same values (Q4a). No configuration plumbing needed.

---

## Frontend implementation outline

### Error parser (replaces single-string `serverError` in `LoginPage.tsx`)

```ts
type LoginError =
  | { kind: "credentials"; message: string; attemptsRemaining?: number }
  | { kind: "locked"; message: string; secondsRemaining: number }
  | { kind: "generic"; message: string };

function parseLoginError(error: unknown): LoginError | null {
  // axios error shape: error.response.status, error.response.data
  const resp = (error as { response?: { status?: number; data?: Record<string, unknown> } })?.response;
  if (!resp) return { kind: "generic", message: "Login failed. Please check your connection and try again." };

  const data = resp.data ?? {};

  // Lockout: 400 + lockoutSecondsRemaining present
  if (resp.status === 400 && typeof data.lockoutSecondsRemaining === "number") {
    return {
      kind: "locked",
      message: (data.detail as string) ?? "Account temporarily locked.",
      secondsRemaining: data.lockoutSecondsRemaining as number,
    };
  }

  // Credentials: 401 with message
  if (resp.status === 401 && typeof data.message === "string") {
    return {
      kind: "credentials",
      message: data.message as string,
      attemptsRemaining: typeof data.attemptsRemaining === "number" ? (data.attemptsRemaining as number) : undefined,
    };
  }

  return { kind: "generic", message: "Login failed. Please try again." };
}
```

### `useLockoutCountdown` hook (new file `bom-web/src/features/auth/useLockoutCountdown.ts`)

```ts
export function useLockoutCountdown(initialSeconds: number | null): {
  remaining: number;
  isExpired: boolean;
  formatted: string; // "mm:ss"
} {
  // - If initialSeconds is null → { remaining: 0, isExpired: true, formatted: "00:00" }
  // - Otherwise: useState(initialSeconds), useEffect with setInterval(1s) decrement, clear on 0
  // - Re-initialise when initialSeconds value changes (new lockout while already counting)
  // - Cleanup interval on unmount
}
```

`formatted` returns `"mm:ss"` (zero-padded both fields). Numerator never exceeds `99:59` for our policy.

### UI rendering in `LoginPage.tsx`

**Generic state:**
```jsx
<p className="text-sm text-destructive">{error.message}</p>
```

**Credentials, attemptsRemaining >= 3 or undefined:**
```jsx
<p className="text-sm text-destructive">Invalid credentials.</p>
```

**Credentials, attemptsRemaining <= 2:**
```jsx
<div className="rounded-md border border-amber-300 bg-amber-50 dark:bg-amber-950/30 px-3 py-2 text-sm text-amber-900 dark:text-amber-200" role="alert">
  ⚠️ Invalid credentials. <strong>{attemptsRemaining} {attemptsRemaining === 1 ? "attempt" : "attempts"} remaining</strong> before lockout.
</div>
```

**Locked state — full-width banner above the form:**
```jsx
<div
  className="rounded-md border border-red-300 bg-red-50 dark:bg-red-950/30 px-4 py-3"
  role="alert"
  aria-live="polite"
>
  <div className="flex items-center gap-2 text-red-900 dark:text-red-200 font-medium">
    <LockKeyhole className="size-4" />
    Account locked
  </div>
  <p className="text-sm text-red-800 dark:text-red-300 mt-1">
    Too many failed login attempts.
  </p>
  <p className="text-2xl font-mono tabular-nums text-red-900 dark:text-red-200 mt-2">
    Try again in {formatted}
  </p>
  <p className="text-xs text-red-700 dark:text-red-300 mt-2">
    If you forgot your password, contact your administrator.
  </p>
</div>
```

**Form-state interactions:**
- Sign In button is disabled while `error.kind === "locked" && !isExpired`. Email/password inputs stay editable.
- When `isExpired` becomes `true` (countdown hit 0), `login.reset()` is called inside an effect to drop the stale mutation error → banner unmounts, button re-enables.
- New failed login response while already counting → `parseLoginError` returns fresh `secondsRemaining` → `useLockoutCountdown` re-initialises.

### Accessibility

- Both warning chip (`attemptsRemaining ≤ 2`) and lockout banner wrap in `role="alert"` so screen readers announce the change.
- The lockout banner uses `aria-live="polite"` (not `"assertive"`) — countdown updates every second would be too chatty for screen readers; per-minute announcements only via a coarser-grain text alternative (e.g., `<span className="sr-only">{`${Math.ceil(remaining / 60)} minutes remaining`}</span>` updated when the minute changes).

---

## Testing

### Backend — extend `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`

| # | Test | Asserts |
|---|---|---|
| T1 | `WrongPassword_FirstAttempt_Returns401WithAttemptsRemaining4` | 401, `message=="Invalid credentials"`, `attemptsRemaining==4` |
| T2 | `WrongPassword_FourthAttempt_Returns401WithAttemptsRemaining1` | 401, `attemptsRemaining==1` |
| T3 | `WrongPassword_FifthAttempt_Returns400LockoutResponse` | 400, content-type `application/problem+json`, `lockoutSecondsRemaining` between 895–900 (allows a few seconds for test latency), `errors.Email == ["Account locked."]`. **Catches the existing behaviour bug.** |
| T4 | `LoginDuringActiveLockout_Returns400WithSecondsRemaining` | Pre-seed user with `LockedUntil = UtcNow + 10min`; any login attempt → 400 + `lockoutSecondsRemaining` between 595–600 |
| T5 | `UnknownEmail_Returns401WithoutAttemptsRemainingField` | 401, response JSON does NOT contain the `attemptsRemaining` key — documents the anti-enumeration tradeoff |

All tests use `WebApplicationFactory<Program>` (existing fixture) and Guid-isolated throwaway users to avoid cross-test pollution.

**Regression check before commit:** grep `LoginLockoutTests.cs` for any existing assertion that "5th wrong attempt returns 401" and update accordingly. The contract change is otherwise additive.

### Frontend — new file `bom-web/src/features/auth/LoginPage.test.tsx`

Stack: vitest + `@testing-library/react`. Mock `useLogin` mutation hook directly (no MSW needed).

| # | Test | Asserts |
|---|---|---|
| F1 | `renders sign-in form by default` | smoke — email, password, button visible |
| F2 | `shows generic message on 401 without attemptsRemaining` | "Invalid credentials" visible, no warning chip |
| F3 | `shows amber warning chip when attemptsRemaining is 2` | "2 attempts remaining" visible, `role="alert"` |
| F4 | `shows amber warning chip with singular grammar when attemptsRemaining is 1` | "1 attempt remaining" (singular) visible |
| F5 | `renders lockout banner with countdown when 400 ProblemDetails returned` | banner visible, mm:ss matches initial seconds, Sign In disabled |
| F6 | `countdown decrements and banner clears at 0` | `vi.useFakeTimers()` → advance 60s → updated mm:ss; advance to full duration → banner hidden, Sign In re-enabled |

### Hook unit test — new file `bom-web/src/features/auth/useLockoutCountdown.test.ts`

Stack: vitest + fake timers.

| # | Test | Asserts |
|---|---|---|
| H1 | `returns expired immediately when initial is null` | `{ remaining: 0, isExpired: true }` |
| H2 | `decrements every second and emits isExpired at 0` | advance timers, observe state transitions |
| H3 | `re-initialises when initial value changes` | new lockout while already counting → fresh countdown from new value |

### Manual smoke (post-deploy)

1. Throwaway account → 3 wrong-password attempts → 3rd response shows amber "2 attempts remaining".
2. 5th wrong attempt → red lockout banner with `mm:ss` countdown ticking down.
3. Watch countdown for ~10 s, reload page → countdown resets to the fresh server-computed value.
4. Wait for the timer to reach 0 (or admin C7 reset to clear the lock) → banner clears, Sign In re-enables.

---

## Out of scope

- **Mobile app (`bom-mobile/`):** intentionally deferred. Mobile will continue to render `"Login failed"` fallback until the next mobile rebuild cycle pulls in equivalent UI changes.
- **`/forgot-password` self-service flow:** does not exist in the codebase. The admin C7 reset remains the only recovery path.
- **Lockout-policy tuning** (5 attempts / 15 min thresholds): left untouched.
- **Lockout escalation** (progressive lockouts beyond 15 min): YAGNI.

---

## File-by-file deltas

| File | Change |
|---|---|
| `BomPriceApproval.API/Infrastructure/Validation/Validation.cs` | Add `Extension(string key, object? value)` method to `ValidationProblemBuilder`; thread extensions into `ValidationProblemDetails.Extensions` in `Return()`. |
| `BomPriceApproval.API/Features/Auth/AuthController.cs` | Modify `Login`: (a) include `attemptsRemaining` in wrong-password 401 response; (b) detect lockout-just-triggered after the 5th wrong attempt and emit lockout response inside the same request; (c) include `lockoutSecondsRemaining` extension on every lockout response (existing-lock + fresh-lock); (d) update lockout `detail` copy to mention "contact your administrator". |
| `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs` | Add T1–T5 (new tests). Update any pre-existing assertion that 5th wrong attempt returns 401 → 400. |
| `bom-web/src/features/auth/LoginPage.tsx` | Replace single-string `serverError` with `parseLoginError(login.error)`; render three new UI states (warning chip, lockout banner, generic). Disable Sign In button while locked. Call `login.reset()` when countdown expires. |
| `bom-web/src/features/auth/useLockoutCountdown.ts` | **NEW** — countdown hook (see implementation outline). |
| `bom-web/src/features/auth/useLockoutCountdown.test.ts` | **NEW** — 3 hook unit tests. |
| `bom-web/src/features/auth/LoginPage.test.tsx` | **NEW** — 6 page-level UI tests. |

Estimated diff: ~80 lines added net, ~10 deleted. Single PR.

---

## Acceptance criteria

The change is complete when:

1. All existing `LoginLockoutTests` + `AuthTests` keep passing (only the 5th-attempt status-code assertion is updated).
2. New backend tests T1–T5 pass.
3. New frontend tests F1–F6 + H1–H3 pass.
4. `dotnet build` clean. `npm run build` in `bom-web` clean. `npx tsc -b` clean.
5. Manual smoke on `localhost` confirms: warning chip on the 3rd wrong attempt, lockout banner with live `mm:ss` countdown on the 5th, banner auto-clears at 0, Sign In re-enables.
6. After merge + Cloudflare deploy, smoke on `https://bom-fpf.pages.dev` matches local behaviour.

Mobile is NOT a gate.
