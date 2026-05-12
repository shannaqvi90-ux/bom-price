# Login Lockout UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface account-lockout state on the login screen — show "X attempts remaining" warnings, a real-time mm:ss countdown banner during lockout, and an admin-reset hint. Backend exposes the necessary fields via additive response shape changes.

**Architecture:** In-place augmentation of `POST /api/auth/login` responses. Backend adds `attemptsRemaining` to 401 responses and `lockoutSecondsRemaining` (RFC 7807 extension member) to 400 lockout responses. Backend also emits the lockout response inline when the 5th wrong attempt triggers the lock — fixes a latent bug where the lockout was invisible until the 6th attempt. Frontend parses both response shapes, renders an amber warning chip when ≤2 attempts remain, and a red lockout banner with a live countdown that auto-clears at 0 and re-enables the Sign In button.

**Tech Stack:** ASP.NET Core 8 + EF Core + xUnit + FluentAssertions on the backend. React 19 + Vite + TanStack Query + vitest + Testing Library on the frontend. lucide-react for icons.

**Reference spec:** `docs/superpowers/specs/2026-05-12-login-lockout-ux-design.md`

**Branch:** `feat/login-lockout-ux` (already created and pushed; spec already committed)

---

## File Structure

**Backend — modify:**
- `BomPriceApproval.API/Infrastructure/Validation/Validation.cs` — add `Extension(string, object?)` method to `ValidationProblemBuilder` so callers can attach RFC 7807 extension members like `lockoutSecondsRemaining`.
- `BomPriceApproval.API/Features/Auth/AuthController.cs` — modify the `Login` action: (a) add `attemptsRemaining` to wrong-password 401 responses, (b) detect when the 5th wrong attempt just triggered the lock and emit the lockout response inline, (c) include `lockoutSecondsRemaining` on every lockout response, (d) update lockout `detail` copy to mention "contact your administrator". Introduce three private constants `MaxAttempts`, `LockoutMinutes`, `LockoutDetail`.
- `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs` — update the existing `Login_After5FailedAttempts_IsLocked` assertion (5th attempt now returns 400 instead of 401), add 5 new tests T1–T5, and add a small private record `CredentialsErrorBody` for deserialising the new 401 shape.

**Frontend — create:**
- `bom-web/src/features/auth/useLockoutCountdown.ts` — countdown hook (state + interval + cleanup + mm:ss formatter).
- `bom-web/src/features/auth/useLockoutCountdown.test.ts` — 3 unit tests using `vi.useFakeTimers()`.
- `bom-web/src/features/auth/LoginPage.test.tsx` — 6 page-level UI tests mocking `useLogin`.

**Frontend — modify:**
- `bom-web/src/features/auth/LoginPage.tsx` — add a top-level `parseLoginError` helper, replace the single-string `serverError` with the discriminated union derived from the parser, render three new UI states (generic / credentials-with-counter / locked-with-countdown), disable the Sign In button while locked, and call `login.reset()` when the countdown expires.

---

## Tasks

### Task 1: Add `Extension()` helper to `ValidationProblemBuilder`

**Files:**
- Modify: `BomPriceApproval.API/Infrastructure/Validation/Validation.cs`

This is a foundation method — no dedicated unit test. It will be exercised by the AuthController tests in Task 4 and Task 5. The helper attaches arbitrary key/value pairs to the `Extensions` dictionary of the emitted `ValidationProblemDetails`, which System.Text.Json serialises as additional JSON properties on the response body. RFC 7807 explicitly allows this.

- [ ] **Step 1.1: Add the `_extensions` field and `Extension(...)` method**

Open `BomPriceApproval.API/Infrastructure/Validation/Validation.cs` and modify the `ValidationProblemBuilder` class. The full file should look like this after editing:

```csharp
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BomPriceApproval.API.Infrastructure.Validation;

public static class Validation
{
    /// <summary>
    /// Start building a 400 ValidationProblemDetails with the given human-readable summary.
    /// </summary>
    public static ValidationProblemBuilder Detail(string detail) => new(detail);
}

public sealed class ValidationProblemBuilder
{
    private readonly string _detail;
    private readonly ModelStateDictionary _errors = new();
    private readonly Dictionary<string, object?> _extensions = new();
    private int _status = StatusCodes.Status400BadRequest;

    internal ValidationProblemBuilder(string detail)
    {
        _detail = detail;
    }

    /// <summary>
    /// Add a field-level error. Field keys use bracket notation for arrays
    /// (e.g. "Items[0].ExpectedQty"). Call once per offending field.
    /// </summary>
    public ValidationProblemBuilder Field(string field, string message)
    {
        _errors.AddModelError(field, message);
        return this;
    }

    /// <summary>
    /// Attach an RFC 7807 extension member that will be serialised as a top-level
    /// JSON property on the response body (e.g. "lockoutSecondsRemaining").
    /// </summary>
    public ValidationProblemBuilder Extension(string key, object? value)
    {
        _extensions[key] = value;
        return this;
    }

    /// <summary>
    /// Override the response status code. Default is 400. Use 409 for Conflict
    /// (e.g. business-rule violations: "already exists", "in-use", etc.).
    /// </summary>
    public ValidationProblemBuilder Status(int statusCode)
    {
        _status = statusCode;
        return this;
    }

    /// <summary>
    /// Build the ActionResult with Content-Type application/problem+json.
    /// </summary>
    public ActionResult Return()
    {
        var problem = new ValidationProblemDetails(_errors)
        {
            Detail = _detail,
            Status = _status,
        };
        foreach (var (k, v) in _extensions)
            problem.Extensions[k] = v;
        return new ObjectResult(problem)
        {
            StatusCode = _status,
            ContentTypes = { "application/problem+json" },
        };
    }
}
```

- [ ] **Step 1.2: Verify build is clean**

Run: `dotnet build --nologo -v q`
Expected: build succeeds with zero errors and zero warnings.

- [ ] **Step 1.3: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Validation/Validation.cs
git commit -m "feat(api): add Extension() helper to ValidationProblemBuilder

Allows attaching RFC 7807 extension members (top-level JSON properties)
to a ProblemDetails response. Will be used by Login to attach
lockoutSecondsRemaining."
```

---

### Task 2: Backend T1 + T2 — `attemptsRemaining` counter on wrong-password 401

**Files:**
- Modify: `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`
- Modify: `BomPriceApproval.API/Features/Auth/AuthController.cs`

TDD: write the failing assertion that the wrong-password response now includes `attemptsRemaining`, then add the field in the controller. T1 covers the first attempt (counter = 4); T2 covers the fourth attempt (counter = 1, singular grammar boundary on the frontend).

- [ ] **Step 2.1: Add the `CredentialsErrorBody` deserialisation record**

Open `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`. At the bottom of the class, where `ValidationProblemResult` is defined (currently line ~142), add a new record above it:

```csharp
private record CredentialsErrorBody(string Message, int? AttemptsRemaining);
private record LoginResult(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
private record ValidationProblemResult(string Detail, Dictionary<string, string[]> Errors);
```

(Note: `ReadFromJsonAsync` uses case-insensitive web defaults, so `Message` deserialises from `"message"` and `AttemptsRemaining` from `"attemptsRemaining"`.)

- [ ] **Step 2.2: Add T1 + T2 tests**

In the same file, add these two tests inside the test class (after the existing `Login_SuccessResets_FailedAttempts` test, before the private DTOs):

```csharp
[Fact]
public async Task WrongPassword_FirstAttempt_Returns401WithAttemptsRemaining4()
{
    var email = $"lck-r4-{Guid.NewGuid():N}"[..24] + "@t.com";
    await CreateUserViaApiAsync(email);

    var resp = await _client.PostAsJsonAsync("/api/auth/login",
        new { Email = email, Password = "Wrong@999" });

    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    var body = await resp.Content.ReadFromJsonAsync<CredentialsErrorBody>();
    body!.Message.Should().Be("Invalid credentials");
    body.AttemptsRemaining.Should().Be(4);
}

[Fact]
public async Task WrongPassword_FourthAttempt_Returns401WithAttemptsRemaining1()
{
    var email = $"lck-r1-{Guid.NewGuid():N}"[..24] + "@t.com";
    await CreateUserViaApiAsync(email);

    // 3 prior wrong attempts
    for (int i = 0; i < 3; i++)
        await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "Wrong@999" });

    // 4th wrong attempt
    var resp = await _client.PostAsJsonAsync("/api/auth/login",
        new { Email = email, Password = "Wrong@999" });

    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    var body = await resp.Content.ReadFromJsonAsync<CredentialsErrorBody>();
    body!.Message.Should().Be("Invalid credentials");
    body.AttemptsRemaining.Should().Be(1);
}
```

- [ ] **Step 2.3: Run the new tests to verify they FAIL**

Run: `dotnet test --filter "FullyQualifiedName~WrongPassword_FirstAttempt_Returns401WithAttemptsRemaining4|FullyQualifiedName~WrongPassword_FourthAttempt_Returns401WithAttemptsRemaining1" --nologo`
Expected: both tests fail with `Expected body.AttemptsRemaining to be 4, but found null` (or similar) — the field doesn't exist in the response yet.

- [ ] **Step 2.4: Add the constants + emit `attemptsRemaining` from AuthController**

Open `BomPriceApproval.API/Features/Auth/AuthController.cs`. Add three private constants near the top of the class (above the constructor or the first action — wherever the class member declarations are):

```csharp
private const int MaxAttempts = 5;
private const int LockoutMinutes = 15;
private const string LockoutDetail = "Account temporarily locked due to too many failed login attempts. If you forgot your password, contact your administrator.";
```

Then locate the wrong-password branch in `Login` (currently around line 53–62):

```csharp
if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
{
    user.FailedLoginAttempts++;
    if (user.FailedLoginAttempts >= 5)
        user.LockedUntil = DateTime.UtcNow.AddMinutes(15);
    await db.SaveChangesAsync();
    logger.LogWarning("[Audit] Login failed: wrong password {UserId} {Email} Attempts={Attempts} Locked={Locked}",
        user.Id, user.Email, user.FailedLoginAttempts, user.LockedUntil is not null);
    return Unauthorized(new { message = "Invalid credentials" });
}
```

Replace it with this version (uses constants + adds `attemptsRemaining`):

```csharp
if (!BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
{
    user.FailedLoginAttempts++;
    if (user.FailedLoginAttempts >= MaxAttempts)
        user.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
    await db.SaveChangesAsync();
    logger.LogWarning("[Audit] Login failed: wrong password {UserId} {Email} Attempts={Attempts} Locked={Locked}",
        user.Id, user.Email, user.FailedLoginAttempts, user.LockedUntil is not null);

    int remaining = Math.Max(0, MaxAttempts - user.FailedLoginAttempts);
    return Unauthorized(new { message = "Invalid credentials", attemptsRemaining = remaining });
}
```

(Task 3 will further modify this branch to return the lockout response when the 5th wrong attempt triggers the lock. For now we still return 401 with `attemptsRemaining = 0` on the 5th attempt.)

- [ ] **Step 2.5: Run the new tests to verify they PASS**

Run: `dotnet test --filter "FullyQualifiedName~WrongPassword_FirstAttempt_Returns401WithAttemptsRemaining4|FullyQualifiedName~WrongPassword_FourthAttempt_Returns401WithAttemptsRemaining1" --nologo`
Expected: both pass.

- [ ] **Step 2.6: Commit**

```bash
git add BomPriceApproval.Tests/Auth/LoginLockoutTests.cs BomPriceApproval.API/Features/Auth/AuthController.cs
git commit -m "feat(auth): expose attemptsRemaining on wrong-password 401 responses

Adds the integer counter to the existing 401 \"Invalid credentials\" body
so clients can warn users before the lockout threshold is hit. Counts
down 4 -> 3 -> 2 -> 1 as FailedLoginAttempts increments."
```

---

### Task 3: Backend T3 — 5th wrong attempt emits lockout response inline

**Files:**
- Modify: `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`
- Modify: `BomPriceApproval.API/Features/Auth/AuthController.cs`

Fixes the latent bug where the 5th wrong attempt returns generic 401 "Invalid credentials" — the lockout response only appeared on the 6th attempt. Also updates the existing regression `Login_After5FailedAttempts_IsLocked` whose loop currently expects 5 consecutive 401s.

- [ ] **Step 3.1: Update the existing `Login_After5FailedAttempts_IsLocked` regression**

In `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`, locate the existing test (around line 69–91). The loop currently asserts `r.StatusCode.Should().Be(HttpStatusCode.Unauthorized);` for all 5 attempts. Change the loop so the first 4 attempts are 401 and the 5th is 400 (lockout). Replace the test body with:

```csharp
[Fact]
public async Task Login_After5FailedAttempts_IsLocked()
{
    var email = $"lck-{Guid.NewGuid():N}"[..28] + "@t.com";
    await CreateUserViaApiAsync(email);

    // 4 wrong attempts -> 401
    for (int i = 0; i < 4; i++)
    {
        var r = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "Wrong@999" });
        r.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            $"attempt {i + 1} should still be 401 (under threshold)");
    }

    // 5th wrong attempt -> lockout response (400)
    var fifth = await _client.PostAsJsonAsync("/api/auth/login",
        new { Email = email, Password = "Wrong@999" });
    fifth.StatusCode.Should().Be(HttpStatusCode.BadRequest,
        "5th wrong attempt should emit lockout response inline");

    // 6th attempt with correct password should also be 400 locked
    var locked = await _client.PostAsJsonAsync("/api/auth/login",
        new { Email = email, Password = "Test@1234" });
    locked.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    var body = await locked.Content.ReadFromJsonAsync<ValidationProblemResult>();
    body!.Detail.Should().Contain("locked");
    body.Errors.Should().ContainKey("Email");
}
```

- [ ] **Step 3.2: Add T3 — explicit assertion that the 5th-attempt body carries `lockoutSecondsRemaining`**

In the same file, add the T3 test (alongside T1/T2 added in Task 2). Place it just before the private DTOs section:

```csharp
[Fact]
public async Task WrongPassword_FifthAttempt_Returns400LockoutResponse()
{
    var email = $"lck-r0-{Guid.NewGuid():N}"[..24] + "@t.com";
    await CreateUserViaApiAsync(email);

    // 4 prior wrong attempts
    for (int i = 0; i < 4; i++)
        await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = "Wrong@999" });

    // 5th wrong attempt -> lockout response inline (NOT generic 401)
    var resp = await _client.PostAsJsonAsync("/api/auth/login",
        new { Email = email, Password = "Wrong@999" });

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    var body = await resp.Content.ReadFromJsonAsync<LockoutErrorBody>();
    body!.Detail.Should().Contain("locked");
    body.Detail.Should().Contain("administrator");
    body.Errors.Should().ContainKey("Email");
    body.LockoutSecondsRemaining.Should().BeInRange(895, 900,
        "15-minute lockout window minus a few seconds of test latency");
}
```

This test needs a new deserialisation record. Add it to the private records section at the bottom of the class (alongside `ValidationProblemResult`):

```csharp
private record LockoutErrorBody(string Detail, Dictionary<string, string[]> Errors, int LockoutSecondsRemaining);
```

- [ ] **Step 3.3: Run the new + updated tests to verify they FAIL**

Run: `dotnet test --filter "FullyQualifiedName~Login_After5FailedAttempts_IsLocked|FullyQualifiedName~WrongPassword_FifthAttempt_Returns400LockoutResponse" --nologo`
Expected: both fail. The updated regression fails on "5th attempt should emit lockout response" — current implementation returns 401. The T3 test fails on the same assertion plus the missing `LockoutSecondsRemaining` field.

- [ ] **Step 3.4: Modify AuthController to detect just-locked and emit lockout response inline**

In `BomPriceApproval.API/Features/Auth/AuthController.cs`, locate the wrong-password branch you modified in Task 2. Replace it with this version that returns the lockout response inline when the 5th attempt triggers the lock:

```csharp
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
        // This attempt just triggered the lock — emit lockout response inline
        // (otherwise the user wouldn't see the lockout until their next attempt).
        var secondsLeft = Math.Max(1, (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalSeconds));
        return Validation
            .Detail(LockoutDetail)
            .Field("Email", "Account locked.")
            .Extension("lockoutSecondsRemaining", secondsLeft)
            .Return();
    }

    int remaining = Math.Max(0, MaxAttempts - user.FailedLoginAttempts);
    return Unauthorized(new { message = "Invalid credentials", attemptsRemaining = remaining });
}
```

- [ ] **Step 3.5: Run the tests to verify they PASS**

Run: `dotnet test --filter "FullyQualifiedName~Login_After5FailedAttempts_IsLocked|FullyQualifiedName~WrongPassword_FifthAttempt_Returns400LockoutResponse" --nologo`
Expected: both pass.

- [ ] **Step 3.6: Commit**

```bash
git add BomPriceApproval.Tests/Auth/LoginLockoutTests.cs BomPriceApproval.API/Features/Auth/AuthController.cs
git commit -m "fix(auth): emit lockout response on the 5th wrong attempt inline

Previously the 5th wrong-password attempt returned generic 401 'Invalid
credentials' and the lockout response only appeared on the 6th attempt -
making the lockout itself invisible until the user tried once more.
Now the lockout response (with lockoutSecondsRemaining) is returned on
the same request that triggers the lock."
```

---

### Task 4: Backend T4 — active-lockout response includes `lockoutSecondsRemaining`

**Files:**
- Modify: `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`
- Modify: `BomPriceApproval.API/Features/Auth/AuthController.cs`

The existing `if (user.LockedUntil > UtcNow)` branch currently returns a ProblemDetails without the seconds-remaining field. Add the field, and the matching test.

- [ ] **Step 4.1: Add T4 test**

In `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`, add this test after T3:

```csharp
[Fact]
public async Task LoginDuringActiveLockout_Returns400WithSecondsRemaining()
{
    var email = $"lck-act-{Guid.NewGuid():N}"[..24] + "@t.com";
    await CreateUserViaApiAsync(email);

    // Pre-seed the lockout state directly: locked for 10 more minutes
    using (var scope = factory.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = db.Users.First(u => u.Email == email);
        user.FailedLoginAttempts = 5;
        user.LockedUntil = DateTime.UtcNow.AddMinutes(10);
        db.SaveChanges();
    }

    // Attempt login with correct password — should still be blocked
    var resp = await _client.PostAsJsonAsync("/api/auth/login",
        new { Email = email, Password = "Test@1234" });

    resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    var body = await resp.Content.ReadFromJsonAsync<LockoutErrorBody>();
    body!.Detail.Should().Contain("locked");
    body.LockoutSecondsRemaining.Should().BeInRange(595, 600,
        "10-minute pre-seeded lockout window minus a few seconds of test latency");
}
```

- [ ] **Step 4.2: Run T4 to verify it FAILS**

Run: `dotnet test --filter "FullyQualifiedName~LoginDuringActiveLockout_Returns400WithSecondsRemaining" --nologo`
Expected: fails on `body.LockoutSecondsRemaining.Should().BeInRange(595, 600)` — the field is currently `0` or missing because the existing lockout branch doesn't set it.

- [ ] **Step 4.3: Modify AuthController existing-lockout branch**

In `BomPriceApproval.API/Features/Auth/AuthController.cs`, locate the existing lockout branch (currently around line 43–51):

```csharp
if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
{
    logger.LogWarning("[Audit] Login rejected: account locked {UserId} {Email} LockedUntil={LockedUntil}",
        user.Id, user.Email, user.LockedUntil);
    return Validation
        .Detail("Account temporarily locked due to too many failed login attempts. Try again later.")
        .Field("Email", "Account locked.")
        .Return();
}
```

Replace with:

```csharp
if (user.LockedUntil is not null && user.LockedUntil > DateTime.UtcNow)
{
    logger.LogWarning("[Audit] Login rejected: account locked {UserId} {Email} LockedUntil={LockedUntil}",
        user.Id, user.Email, user.LockedUntil);
    var secondsLeft = Math.Max(1, (int)Math.Ceiling((user.LockedUntil.Value - DateTime.UtcNow).TotalSeconds));
    return Validation
        .Detail(LockoutDetail)
        .Field("Email", "Account locked.")
        .Extension("lockoutSecondsRemaining", secondsLeft)
        .Return();
}
```

(`LockoutDetail` is the constant added in Task 2 — same copy as the just-locked branch in Task 3, including the "contact your administrator" sentence.)

- [ ] **Step 4.4: Run T4 to verify it PASSES**

Run: `dotnet test --filter "FullyQualifiedName~LoginDuringActiveLockout_Returns400WithSecondsRemaining" --nologo`
Expected: passes.

- [ ] **Step 4.5: Commit**

```bash
git add BomPriceApproval.Tests/Auth/LoginLockoutTests.cs BomPriceApproval.API/Features/Auth/AuthController.cs
git commit -m "feat(auth): include lockoutSecondsRemaining on active-lockout responses

The existing-lock branch now uses the shared LockoutDetail constant
(mentions admin reset) and attaches lockoutSecondsRemaining so the
frontend can render a live countdown."
```

---

### Task 5: Backend T5 — anti-enumeration documentation test

**Files:**
- Modify: `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`

This test documents the explicitly-accepted enumeration leak: unknown-email responses must NOT carry the `attemptsRemaining` field, even though known-wrong-password responses do. Helps prevent future "consistency cleanup" PRs from accidentally adding the field and changing the security posture.

- [ ] **Step 5.1: Add T5**

In `BomPriceApproval.Tests/Auth/LoginLockoutTests.cs`, add the test after T4:

```csharp
[Fact]
public async Task UnknownEmail_Returns401WithoutAttemptsRemainingField()
{
    var unknown = $"nobody-{Guid.NewGuid():N}"[..28] + "@t.com";

    var resp = await _client.PostAsJsonAsync("/api/auth/login",
        new { Email = unknown, Password = "Wrong@999" });

    resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

    // Anti-enumeration tradeoff documented in the design spec:
    // unknown-email responses must NOT include attemptsRemaining (no user
    // record to count against). The asymmetry IS the accepted leak.
    var raw = await resp.Content.ReadAsStringAsync();
    raw.Should().NotContain("attemptsRemaining",
        "unknown-email responses must not carry the counter field");

    var body = await resp.Content.ReadFromJsonAsync<CredentialsErrorBody>();
    body!.Message.Should().Be("Invalid credentials");
    body.AttemptsRemaining.Should().BeNull();
}
```

- [ ] **Step 5.2: Run T5 to verify it PASSES**

Run: `dotnet test --filter "FullyQualifiedName~UnknownEmail_Returns401WithoutAttemptsRemainingField" --nologo`
Expected: passes immediately — the unknown-email branch in `AuthController.Login` was never modified, so it still returns the original `new { message = "Invalid credentials" }` with no extra fields.

- [ ] **Step 5.3: Commit**

```bash
git add BomPriceApproval.Tests/Auth/LoginLockoutTests.cs
git commit -m "test(auth): document anti-enumeration tradeoff with regression test

Asserts that unknown-email responses do NOT include attemptsRemaining.
The asymmetry between known-wrong-password (with counter) and
unknown-email (without) is the deliberately-accepted enumeration leak
per the design spec."
```

---

### Task 6: Verify backend slice + run full suite

**Files:** none modified.

- [ ] **Step 6.1: Full backend build**

Run: `dotnet build --nologo -v q`
Expected: zero errors, zero warnings.

- [ ] **Step 6.2: Run all Auth tests**

Run: `dotnet test --filter "FullyQualifiedName~Auth" --nologo`
Expected: all green (existing AuthTests, LoginMustChangePasswordTests, RefreshTokenRaceTests, LoginLockoutTests). If anything fails outside the lockout class, investigate — the contract change should be additive for those paths.

- [ ] **Step 6.3: Run the full test suite as a safety net**

Run: `dotnet test --nologo`
Expected: all green. If a test outside the Auth slice fails, it's almost certainly a flaky integration test (e.g. AuthTests timing test per CLAUDE.md) — retry the failing test by class name. Real regressions in non-auth tests are unlikely given the contract change is narrow.

No commit for this task — purely verification.

---

### Task 7: `useLockoutCountdown` hook + 3 unit tests

**Files:**
- Create: `bom-web/src/features/auth/useLockoutCountdown.ts`
- Create: `bom-web/src/features/auth/useLockoutCountdown.test.ts`

TDD: write the 3 tests first, then implement the hook.

- [ ] **Step 7.1: Create the test file with 3 failing tests**

Create `bom-web/src/features/auth/useLockoutCountdown.test.ts` with this content:

```ts
import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useLockoutCountdown } from "./useLockoutCountdown";

describe("useLockoutCountdown", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("returns expired immediately when initial is null", () => {
    const { result } = renderHook(() => useLockoutCountdown(null));
    expect(result.current.remaining).toBe(0);
    expect(result.current.isExpired).toBe(true);
    expect(result.current.formatted).toBe("00:00");
  });

  it("decrements every second and emits isExpired at 0", () => {
    const { result } = renderHook(() => useLockoutCountdown(3));

    expect(result.current.remaining).toBe(3);
    expect(result.current.isExpired).toBe(false);
    expect(result.current.formatted).toBe("00:03");

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(result.current.remaining).toBe(2);
    expect(result.current.formatted).toBe("00:02");

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(result.current.remaining).toBe(1);

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(result.current.remaining).toBe(0);
    expect(result.current.isExpired).toBe(true);
    expect(result.current.formatted).toBe("00:00");
  });

  it("re-initialises when initial value changes (new lockout while counting)", () => {
    let initial: number | null = 5;
    const { result, rerender } = renderHook(() => useLockoutCountdown(initial));

    expect(result.current.remaining).toBe(5);

    act(() => {
      vi.advanceTimersByTime(2000);
    });
    expect(result.current.remaining).toBe(3);

    // Fresh lockout response from backend with a larger value
    initial = 900;
    rerender();
    expect(result.current.remaining).toBe(900);
    expect(result.current.isExpired).toBe(false);
    expect(result.current.formatted).toBe("15:00");
  });
});
```

- [ ] **Step 7.2: Run the tests to verify they FAIL with "cannot find module"**

Run: `npx vitest run src/features/auth/useLockoutCountdown.test.ts` (from inside `bom-web/`)
Expected: tests fail because `./useLockoutCountdown` doesn't exist yet.

- [ ] **Step 7.3: Create the hook**

Create `bom-web/src/features/auth/useLockoutCountdown.ts` with this content:

```ts
import { useEffect, useState } from "react";

export interface LockoutCountdown {
  remaining: number;
  isExpired: boolean;
  formatted: string; // mm:ss
}

function formatMmSs(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const minutes = Math.floor(safe / 60);
  const seconds = safe % 60;
  return `${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
}

/**
 * Countdown timer for the login lockout banner.
 *
 * - When `initialSeconds` is null, the hook is in the expired state.
 * - When `initialSeconds` is a positive number, the hook starts at that value
 *   and decrements every second until it reaches 0.
 * - When `initialSeconds` changes (e.g. a fresh lockout response arrives while
 *   already counting), the countdown re-initialises from the new value.
 */
export function useLockoutCountdown(initialSeconds: number | null): LockoutCountdown {
  const [remaining, setRemaining] = useState<number>(initialSeconds ?? 0);

  // Re-initialise when initialSeconds changes (covers null -> N and N -> M).
  useEffect(() => {
    setRemaining(initialSeconds ?? 0);
  }, [initialSeconds]);

  // Tick down every second while remaining > 0.
  useEffect(() => {
    if (remaining <= 0) return;
    const id = window.setInterval(() => {
      setRemaining((r) => (r > 0 ? r - 1 : 0));
    }, 1000);
    return () => window.clearInterval(id);
  }, [remaining]);

  return {
    remaining,
    isExpired: remaining <= 0,
    formatted: formatMmSs(remaining),
  };
}
```

- [ ] **Step 7.4: Run the tests to verify they PASS**

Run: `npx vitest run src/features/auth/useLockoutCountdown.test.ts` (from inside `bom-web/`)
Expected: all 3 tests pass.

- [ ] **Step 7.5: Commit**

```bash
git add bom-web/src/features/auth/useLockoutCountdown.ts bom-web/src/features/auth/useLockoutCountdown.test.ts
git commit -m "feat(web): add useLockoutCountdown hook

mm:ss countdown for the login lockout banner. Re-initialises when the
initial value changes (so a fresh backend lockout response while already
counting picks up the new value)."
```

---

### Task 8: Refactor `LoginPage.tsx` — parser + rendering states

**Files:**
- Modify: `bom-web/src/features/auth/LoginPage.tsx`

Replace the single-string `serverError` with a discriminated union derived from a `parseLoginError` helper. Render three states: credentials with optional warning chip, lockout banner with countdown, generic fallback. Disable Sign In button while locked. Call `login.reset()` when the countdown expires.

- [ ] **Step 8.1: Rewrite `LoginPage.tsx`**

Replace the entire contents of `bom-web/src/features/auth/LoginPage.tsx` with:

```tsx
import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { useNavigate, Navigate } from "react-router-dom";
import { LockKeyhole } from "lucide-react";
import { useLogin } from "./authApi";
import { useAuthStore } from "@/store/authStore";
import { useLockoutCountdown } from "./useLockoutCountdown";
import { Button } from "@/components/ui/Button";
import { Input } from "@/components/ui/Input";
import { Label } from "@/components/ui/Label";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/Card";

const schema = z.object({
  email: z.string().email("Enter a valid email"),
  password: z.string().min(1, "Password is required"),
});

type FormValues = z.infer<typeof schema>;

type LoginError =
  | { kind: "credentials"; message: string; attemptsRemaining?: number }
  | { kind: "locked"; message: string; secondsRemaining: number }
  | { kind: "generic"; message: string };

function parseLoginError(error: unknown): LoginError {
  const resp = (error as {
    response?: { status?: number; data?: Record<string, unknown> };
  })?.response;

  if (!resp) {
    return {
      kind: "generic",
      message: "Login failed. Please check your connection and try again.",
    };
  }

  const data = resp.data ?? {};

  if (resp.status === 400 && typeof data.lockoutSecondsRemaining === "number") {
    return {
      kind: "locked",
      message:
        typeof data.detail === "string" ? data.detail : "Account temporarily locked.",
      secondsRemaining: data.lockoutSecondsRemaining as number,
    };
  }

  if (resp.status === 401 && typeof data.message === "string") {
    return {
      kind: "credentials",
      message: data.message as string,
      attemptsRemaining:
        typeof data.attemptsRemaining === "number"
          ? (data.attemptsRemaining as number)
          : undefined,
    };
  }

  return { kind: "generic", message: "Login failed. Please try again." };
}

export default function LoginPage() {
  const isAuthed = useAuthStore((s) => s.isAuthenticated());
  const navigate = useNavigate();
  const login = useLogin();

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { email: "", password: "" },
  });

  const error: LoginError | null = login.error ? parseLoginError(login.error) : null;
  const lockoutSeconds = error?.kind === "locked" ? error.secondsRemaining : null;
  const countdown = useLockoutCountdown(lockoutSeconds);
  const isLocked = error?.kind === "locked" && !countdown.isExpired;

  // Drop the stale lockout error once the countdown reaches 0 so the banner
  // unmounts and the Sign In button re-enables.
  useEffect(() => {
    if (error?.kind === "locked" && countdown.isExpired) {
      login.reset();
    }
  }, [error?.kind, countdown.isExpired, login]);

  if (isAuthed) return <Navigate to="/dashboard" replace />;

  const onSubmit = handleSubmit(async (values) => {
    try {
      await login.mutateAsync(values);
      navigate("/dashboard", { replace: true });
    } catch {
      // error surfaced via login.error below
    }
  });

  return (
    <div className="flex min-h-screen items-center justify-center bg-background px-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>Sign in</CardTitle>
          <p className="text-sm text-muted-foreground mt-1">
            BOM &amp; Price Approval
          </p>
        </CardHeader>
        <CardContent>
          {error?.kind === "locked" && !countdown.isExpired && (
            <div
              className="mb-4 rounded-md border border-red-300 bg-red-50 px-4 py-3 dark:border-red-900 dark:bg-red-950/30"
              role="alert"
              aria-live="polite"
            >
              <div className="flex items-center gap-2 font-medium text-red-900 dark:text-red-200">
                <LockKeyhole className="size-4" aria-hidden="true" />
                Account locked
              </div>
              <p className="mt-1 text-sm text-red-800 dark:text-red-300">
                Too many failed login attempts.
              </p>
              <p
                className="mt-2 font-mono text-2xl tabular-nums text-red-900 dark:text-red-200"
                aria-label={`Try again in ${countdown.formatted}`}
              >
                Try again in {countdown.formatted}
              </p>
              <p className="mt-2 text-xs text-red-700 dark:text-red-300">
                If you forgot your password, contact your administrator.
              </p>
            </div>
          )}

          <form onSubmit={onSubmit} className="space-y-4" noValidate>
            <div className="space-y-2">
              <Label htmlFor="email">Email</Label>
              <Input
                id="email"
                type="email"
                autoComplete="email"
                {...register("email")}
              />
              {errors.email && (
                <p className="text-xs text-destructive">{errors.email.message}</p>
              )}
            </div>
            <div className="space-y-2">
              <Label htmlFor="password">Password</Label>
              <Input
                id="password"
                type="password"
                autoComplete="current-password"
                {...register("password")}
              />
              {errors.password && (
                <p className="text-xs text-destructive">{errors.password.message}</p>
              )}
            </div>

            {error?.kind === "credentials" &&
              (typeof error.attemptsRemaining === "number" &&
              error.attemptsRemaining <= 2 ? (
                <div
                  className="rounded-md border border-amber-300 bg-amber-50 px-3 py-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-950/30 dark:text-amber-200"
                  role="alert"
                >
                  Invalid credentials.{" "}
                  <strong>
                    {error.attemptsRemaining}{" "}
                    {error.attemptsRemaining === 1 ? "attempt" : "attempts"} remaining
                  </strong>{" "}
                  before lockout.
                </div>
              ) : (
                <p className="text-sm text-destructive">{error.message}</p>
              ))}

            {error?.kind === "generic" && (
              <p className="text-sm text-destructive">{error.message}</p>
            )}

            <Button
              type="submit"
              className="w-full"
              disabled={isSubmitting || login.isPending || isLocked}
            >
              {login.isPending ? "Signing in…" : "Sign in"}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 8.2: Verify TypeScript compiles**

Run: `npx tsc -b` (from inside `bom-web/`)
Expected: zero errors.

- [ ] **Step 8.3: Verify existing ForceChangePasswordGuard tests still pass**

Run: `npx vitest run src/features/auth` (from inside `bom-web/`)
Expected: existing `ForceChangePasswordGuard.test.tsx` cases (3) still pass, plus the 3 `useLockoutCountdown` cases from Task 7. `LoginPage.test.tsx` doesn't exist yet — that's Task 9.

- [ ] **Step 8.4: Commit**

```bash
git add bom-web/src/features/auth/LoginPage.tsx
git commit -m "feat(web): render attempts-remaining warning and lockout countdown

Replaces the single-string serverError with parseLoginError, which
returns a discriminated union of credentials/locked/generic states.

- attemptsRemaining <= 2 shows an amber warning chip with grammar-correct
  copy ('1 attempt' vs 'N attempts')
- Locked state shows a red banner with a live mm:ss countdown above the
  form; Sign In button disabled
- When countdown hits 0, login.reset() clears the mutation error so the
  banner unmounts and Sign In re-enables"
```

---

### Task 9: `LoginPage.test.tsx` — 6 UI tests

**Files:**
- Create: `bom-web/src/features/auth/LoginPage.test.tsx`

- [ ] **Step 9.1: Create the test file**

Create `bom-web/src/features/auth/LoginPage.test.tsx` with this content:

```tsx
import { act, render, screen } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import LoginPage from "./LoginPage";
import { useLogin } from "./authApi";
import { useAuthStore } from "@/store/authStore";

vi.mock("./authApi", () => ({
  useLogin: vi.fn(),
}));

const mockUseLogin = vi.mocked(useLogin);

interface LoginMockState {
  error?: unknown;
  isPending?: boolean;
}

function mockLoginState({ error, isPending = false }: LoginMockState) {
  // Cast through unknown — the full UseMutationResult is unwieldy and the
  // component only consumes a small surface.
  mockUseLogin.mockReturnValue({
    error: error ?? null,
    isPending,
    isError: !!error,
    mutateAsync: vi.fn(),
    reset: vi.fn(),
  } as unknown as ReturnType<typeof useLogin>);
}

function renderPage() {
  return render(
    <MemoryRouter>
      <LoginPage />
    </MemoryRouter>,
  );
}

describe("LoginPage", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    // Ensure no leftover authed session from prior tests
    useAuthStore.getState().logout();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it("renders the sign-in form by default", () => {
    mockLoginState({});
    renderPage();

    expect(screen.getByLabelText("Email")).toBeInTheDocument();
    expect(screen.getByLabelText("Password")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /sign in/i })).toBeEnabled();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows generic 'Invalid credentials' on 401 without attemptsRemaining", () => {
    mockLoginState({
      error: {
        response: {
          status: 401,
          data: { message: "Invalid credentials" },
        },
      },
    });
    renderPage();

    expect(screen.getByText("Invalid credentials")).toBeInTheDocument();
    // No amber warning chip (attemptsRemaining is absent)
    expect(screen.queryByText(/attempts remaining/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/attempt remaining/i)).not.toBeInTheDocument();
  });

  it("shows amber warning chip when attemptsRemaining is 2", () => {
    mockLoginState({
      error: {
        response: {
          status: 401,
          data: { message: "Invalid credentials", attemptsRemaining: 2 },
        },
      },
    });
    renderPage();

    const chip = screen.getByRole("alert");
    expect(chip).toHaveTextContent(/2 attempts remaining/i);
  });

  it("uses singular grammar when attemptsRemaining is 1", () => {
    mockLoginState({
      error: {
        response: {
          status: 401,
          data: { message: "Invalid credentials", attemptsRemaining: 1 },
        },
      },
    });
    renderPage();

    const chip = screen.getByRole("alert");
    expect(chip).toHaveTextContent(/1 attempt remaining/i);
    // Not "1 attempts" (plural)
    expect(chip).not.toHaveTextContent(/1 attempts remaining/i);
  });

  it("renders the lockout banner with mm:ss countdown when 400 ProblemDetails received", () => {
    mockLoginState({
      error: {
        response: {
          status: 400,
          data: {
            detail: "Account temporarily locked due to too many failed login attempts.",
            errors: { Email: ["Account locked."] },
            lockoutSecondsRemaining: 905, // 15:05
          },
        },
      },
    });
    renderPage();

    const banner = screen.getByRole("alert");
    expect(banner).toHaveTextContent(/account locked/i);
    expect(banner).toHaveTextContent(/try again in 15:05/i);
    expect(banner).toHaveTextContent(/contact your administrator/i);
    expect(screen.getByRole("button", { name: /sign in/i })).toBeDisabled();
  });

  it("countdown decrements every second and banner clears when timer reaches 0", () => {
    mockLoginState({
      error: {
        response: {
          status: 400,
          data: {
            detail: "Account locked.",
            errors: { Email: ["Account locked."] },
            lockoutSecondsRemaining: 3,
          },
        },
      },
    });
    renderPage();

    expect(screen.getByText(/try again in 00:03/i)).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(1000);
    });
    expect(screen.getByText(/try again in 00:02/i)).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(2000);
    });
    // After 3 total seconds, the countdown has hit 0. The component calls
    // login.reset() in an effect — our mock doesn't actually clear the error,
    // but the banner is conditioned on !countdown.isExpired, so it unmounts.
    expect(screen.queryByText(/try again in/i)).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: /sign in/i })).toBeEnabled();
  });
});
```

- [ ] **Step 9.2: Run the new tests to verify they PASS**

Run: `npx vitest run src/features/auth/LoginPage.test.tsx` (from inside `bom-web/`)
Expected: all 6 tests pass.

- [ ] **Step 9.3: Run the full auth test suite as a sanity check**

Run: `npx vitest run src/features/auth` (from inside `bom-web/`)
Expected: 12 tests pass (3 ForceChangePasswordGuard + 3 useLockoutCountdown + 6 LoginPage).

- [ ] **Step 9.4: Commit**

```bash
git add bom-web/src/features/auth/LoginPage.test.tsx
git commit -m "test(web): LoginPage UI states for attempts warning + lockout countdown

6 tests covering: default form, generic 401 without counter, amber chip
at attemptsRemaining<=2, singular grammar at 1, lockout banner with
countdown, countdown decrement + banner auto-clear at 0."
```

---

### Task 10: Full-stack verification + manual smoke on localhost

**Files:** none modified.

- [ ] **Step 10.1: Backend full build + test**

Run from repo root:

```bash
dotnet build --nologo -v q
dotnet test --nologo
```

Expected: build clean, all tests green. The AuthTests timing test in `BomPriceApproval.Tests/Auth/AuthTests.cs` is documented-flaky in CLAUDE.md — if it fails sporadically under load, re-run that one test (`dotnet test --filter "FullyQualifiedName~AuthTests"`).

- [ ] **Step 10.2: Frontend full build + test**

Run from `bom-web/`:

```bash
npm run test
npx tsc -b
npm run build
```

Expected: vitest green (existing + new), tsc no errors, vite build emits `dist/` without warnings about unused imports or missing types.

- [ ] **Step 10.3: Manual smoke on localhost**

Start the backend if it isn't running:

```bash
# In one terminal, from repo root:
curl -s http://localhost:7300/swagger/index.html >/dev/null || dotnet run --project BomPriceApproval.API
```

Wait for the API to be ready (Swagger UI loads at `http://localhost:7300/swagger`).

Start the frontend in another terminal:

```bash
cd bom-web && npm run dev
```

Open `http://localhost:5300/login` in a browser. Walk through this checklist using a seeded test account (`ali@test.com` / `Test@1234` is the seeded SalesPerson per memory). To get clean state, use the Admin reset endpoint (`POST /api/admin/users/{id}/reset-password` via Swagger UI as `admin@test.com`) between iterations:

  - [ ] **Smoke 1:** Type wrong password once → "Invalid credentials" red text appears (no warning chip).
  - [ ] **Smoke 2:** Type wrong password 3 times total → 3rd response shows amber chip: "Invalid credentials. **2 attempts remaining** before lockout."
  - [ ] **Smoke 3:** Type wrong password 4 times total → 4th response shows amber chip: "Invalid credentials. **1 attempt remaining** before lockout." (singular).
  - [ ] **Smoke 4:** Type wrong password a 5th time → red lockout banner appears with `Try again in 15:00` (or close to it) ticking down. Sign In button is disabled.
  - [ ] **Smoke 5:** Watch the countdown decrement for ~10 seconds. It should tick down every second.
  - [ ] **Smoke 6:** Reload the page. The banner disappears (no error state on a fresh page). Click Sign In with a wrong password — backend re-emits the lockout response with the current `lockoutSecondsRemaining`, and the banner reappears with the correct remaining time.
  - [ ] **Smoke 7:** Use the Admin reset endpoint to clear the lock. Reload page. Banner is gone. Type correct password → login succeeds.

If any smoke step fails, debug before proceeding to Task 11.

No commit for this task — verification only.

---

### Task 11: Push branch + open PR

**Files:** none modified.

- [ ] **Step 11.1: Confirm branch is up to date with master**

```bash
git fetch origin master
git log --oneline origin/master..HEAD
```

Expected: list of commits from this implementation (Tasks 1–9, plus the spec commit from before this plan). Should be ~10 commits. Verify no `master` SHA newer than what we branched from. If master has moved, rebase: `git rebase origin/master`.

- [ ] **Step 11.2: Push**

```bash
git push origin feat/login-lockout-ux
```

Expected: push succeeds (branch already exists on remote from the spec commit).

- [ ] **Step 11.3: Open PR**

```bash
gh pr create \
  --base master \
  --head feat/login-lockout-ux \
  --title "feat(auth): surface attempts-remaining + lockout countdown on login" \
  --body "$(cat <<'EOF'
## Summary

- Backend `POST /api/auth/login` now returns `attemptsRemaining` on wrong-password 401 responses and `lockoutSecondsRemaining` on 400 lockout responses (RFC 7807 extension member).
- Fixes a latent bug where the 5th wrong-password attempt returned generic 401 instead of the lockout response — the user couldn't tell they were locked until they tried again.
- Web login page now renders an amber "X attempts remaining" warning chip on the 3rd and 4th wrong attempts, and a red lockout banner with a live mm:ss countdown that auto-clears at 0 (Sign In button re-enables).
- Lockout copy now hints the admin-reset path ("If you forgot your password, contact your administrator.").
- Mobile is intentionally out of scope — the additive backend changes don't break the shipped APK; mobile UX will catch up on the next rebuild cycle.

## Design + plan
- Spec: `docs/superpowers/specs/2026-05-12-login-lockout-ux-design.md`
- Plan: `docs/superpowers/plans/2026-05-12-login-lockout-ux.md`

## Test plan

- [x] Backend: 5 new tests in `LoginLockoutTests.cs` (T1–T5) + 1 regression update (`Login_After5FailedAttempts_IsLocked` loop now expects 4×401 then 1×400).
- [x] Frontend: 3 new `useLockoutCountdown` tests + 6 new `LoginPage.test.tsx` tests.
- [x] Full backend test suite green.
- [x] Full frontend test suite green; `tsc -b` clean; `npm run build` clean.
- [x] Manual localhost smoke (7 cases) passed.
- [ ] Post-merge smoke on https://bom-fpf.pages.dev (verify Cloudflare auto-deploy picks up the web change).

## Security note

The `attemptsRemaining` field is included on known-wrong-password responses but **NOT** on unknown-email responses (no user record to count against). This is the deliberately-accepted enumeration leak documented in the spec (Q1 decision) — acceptable for an internal app with ~5 staff. `UnknownEmail_Returns401WithoutAttemptsRemainingField` documents this as a regression test.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

Expected: PR URL is printed. Report it back to the user.

---

## Acceptance criteria (re-statement from spec)

1. ✅ All existing `LoginLockoutTests` + `AuthTests` pass (with `Login_After5FailedAttempts_IsLocked` updated per the new 5th-attempt behaviour).
2. ✅ New backend tests T1–T5 pass.
3. ✅ New frontend tests F1–F6 + H1–H3 pass.
4. ✅ `dotnet build` clean. `npm run build` in `bom-web` clean. `npx tsc -b` clean.
5. ✅ Manual smoke on localhost confirms: warning chip on the 3rd wrong attempt, lockout banner with live mm:ss countdown on the 5th, banner auto-clears at 0, Sign In re-enables.
6. ⏳ Post-merge smoke on `https://bom-fpf.pages.dev` matches local behaviour.

Mobile is NOT a gate.
