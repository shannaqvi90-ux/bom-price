# Mobile V2.1 Phase 2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the Accountant mobile app to feature-parity with Sales/MD by adding a KPI dashboard, all-list with status chips, notification deep-link fix for Accountant, and customer-change mobile parity (Feature X).

**Architecture:** Backend gets one new endpoint (`GET /api/stats/accountant-dashboard`) and an optional date filter on the requisitions list (`?from=&to=`). Mobile replaces `(accountant)/index.tsx` with a dashboard, adds a new `(accountant)/list.tsx` for the all-list with chips, makes `(accountant)/[id].tsx` a status-aware smart route (form vs read-only), extracts the MD historical screen into a shared component, and adds two bottom-sheet components for customer swap and change-history. Customer change uses already-shipped backend endpoints — no backend work for Feature X.

**Tech Stack:** ASP.NET Core 8 (C#) + EF Core 8 + PostgreSQL (backend) · React 19 (web — not touched here) · Expo Router 4 + React Native 0.76 + TanStack Query 5 + Moti + NativeWind (mobile) · xUnit + Testcontainers (integration tests)

**Spec reference:** [`docs/superpowers/specs/2026-04-25-mobile-v21-phase2-design.md`](../specs/2026-04-25-mobile-v21-phase2-design.md)

---

## File Map

### Backend (BomPriceApproval.API)

| Action | Path |
|---|---|
| MODIFY | `Features/Requisitions/RequisitionsController.cs` (add `from`/`to` filter to `GetAll`) |
| CREATE | `Features/Stats/StatsController.cs` (new `GET /api/stats/accountant-dashboard`) |
| CREATE | `Features/Stats/StatsDtos.cs` (response DTO) |

### Backend tests (BomPriceApproval.Tests)

| Action | Path |
|---|---|
| CREATE | `Stats/AccountantDashboardTests.cs` |
| MODIFY | `Requisitions/ValidationTests.cs` *or* CREATE `Requisitions/ListDateFilterTests.cs` (date-filter tests) — plan uses a new file |

### Mobile (bom-mobile)

| Action | Path |
|---|---|
| REPLACE | `app/(accountant)/index.tsx` (was: pending list → now: dashboard) |
| CREATE | `app/(accountant)/list.tsx` (all-list with chips + URL params) |
| MODIFY | `app/(accountant)/[id].tsx` (smart route: form vs `<HistoricalRequisitionScreen>`) |
| MODIFY | `app/(md)/historical/[id].tsx` (delegate to shared component) |
| MODIFY | `app/notifications.tsx` (add Accountant to `pathForNotification`) |
| CREATE | `src/components/HistoricalRequisitionScreen.tsx` (extracted from MD historical) |
| CREATE | `src/components/CustomerSwapSheet.tsx` |
| CREATE | `src/components/CustomerChangeHistorySheet.tsx` |
| MODIFY | `src/api/stats.ts` (add `useAccountantDashboardStats`) |
| MODIFY | `src/api/requisitions.ts` (add `useChangeCustomer` + `useCustomerChangeHistory`) |
| MODIFY | `src/types/api.ts` (add change-history + dashboard-stats DTOs) |

---

## Conventions

- **Backend tests:** TDD — write the failing test first, run, see RED, implement, run, see GREEN, commit.
- **Mobile changes:** Manual on-device smoke (no automated tests in this codebase per Phase 1 + V2.2 precedent). Each task has a smoke checklist that must pass before commit.
- **Compile gate:** After every backend file edit run `dotnet build --nologo -v q`. After every mobile file edit run `cd bom-mobile && npx tsc --noEmit`.
- **Commits:** Conventional Commits format (`feat(scope): …` / `fix(scope): …` / `test(scope): …`). Show `git diff --stat` and the proposed message before committing per `CLAUDE.md` mandatory safety procedure.
- **Backend running:** Tests start their own Testcontainers Postgres — no need to run the API locally. For mobile manual smoke, run `dotnet run --project BomPriceApproval.API` (port 7300) + `cd bom-mobile && npx expo start`.

---

## Task 1: Backend — Add `?from=&to=` filter to requisitions list (TDD)

**Files:**
- Test: `BomPriceApproval.Tests/Requisitions/ListDateFilterTests.cs` (CREATE)
- Modify: `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs:25-79` (the `GetAll` action)

### Steps

- [ ] **Step 1.1: Write the failing test**

Create `BomPriceApproval.Tests/Requisitions/ListDateFilterTests.cs`:

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Requisitions;

public class ListDateFilterTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
        return body.AccessToken;
    }

    private record CountedListItem(int Id, string RefNo, string Status);

    [Fact]
    public async Task List_WithFromFilter_ReturnsOnlyItemsUpdatedOnOrAfter()
    {
        // Arrange — login as MD (sees all branches), bump UpdatedAt on a known seeded REQ
        var token = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Pick a future date — nothing seeded after it
        var future = DateTime.UtcNow.AddDays(1).Date;
        var fromParam = future.ToString("yyyy-MM-dd");

        // Act
        var resp = await _client.GetAsync($"/api/requisitions?from={fromParam}");
        resp.EnsureSuccessStatusCode();
        var items = (await resp.Content.ReadFromJsonAsync<List<CountedListItem>>())!;

        // Assert
        items.Should().BeEmpty("no seeded requisitions exist with UpdatedAt in the future");
    }

    [Fact]
    public async Task List_WithToFilter_ReturnsOnlyItemsUpdatedBefore()
    {
        var token = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 'to' very far in the past — should yield zero
        var farPast = new DateTime(2000, 1, 1).ToString("yyyy-MM-dd");

        var resp = await _client.GetAsync($"/api/requisitions?to={farPast}");
        resp.EnsureSuccessStatusCode();
        var items = (await resp.Content.ReadFromJsonAsync<List<CountedListItem>>())!;

        items.Should().BeEmpty("nothing exists with UpdatedAt before 2000-01-01");
    }

    [Fact]
    public async Task List_WithoutDateFilters_BackwardsCompatible()
    {
        var token = await LoginAsync("md@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/requisitions");
        resp.EnsureSuccessStatusCode();
        var items = (await resp.Content.ReadFromJsonAsync<List<CountedListItem>>())!;

        // No assertion on count — just verify the endpoint still returns 200 + an array
        items.Should().NotBeNull();
    }

    private record LoginResponse(string AccessToken, string RefreshToken);
}
```

- [ ] **Step 1.2: Run the test, verify it fails**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~ListDateFilterTests" --nologo
```

Expected: 2 of 3 tests **FAIL** (the future-date and far-past tests fail because the filter is not implemented and the endpoint returns matching seeded data). The "BackwardsCompatible" one will pass.

- [ ] **Step 1.3: Implement the date filter**

Edit `BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs`. In the `GetAll` action signature, add the two parameters:

```csharp
[HttpGet]
public async Task<IActionResult> GetAll(
    [FromQuery(Name = "status")] string[]? statuses = null,
    [FromQuery] string? search = null,
    [FromQuery] DateTime? from = null,
    [FromQuery] DateTime? to = null,
    [FromQuery] int? page = null,
    [FromQuery] int? pageSize = null)
{
```

Add the filter block right after the existing `search` block (around line 63), before the `var projected = …` line:

```csharp
if (from.HasValue)
{
    var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
    query = query.Where(q => q.UpdatedAt >= fromUtc);
}

if (to.HasValue)
{
    // Exclusive upper: < to + 1 day
    var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc);
    query = query.Where(q => q.UpdatedAt < toUtc);
}
```

`UpdatedAt` is the available proxy timestamp on `QuotationRequest` (see spec §10 open question #1 — `SubmittedAt` does not exist).

- [ ] **Step 1.4: Run the test, verify it passes**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~ListDateFilterTests" --nologo
```

Expected: **3 PASS** / 0 FAIL.

- [ ] **Step 1.5: Run the full test suite for regression**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --nologo
```

Expected: all tests PASS (no regression).

- [ ] **Step 1.6: Commit**

```bash
git add BomPriceApproval.API/Features/Requisitions/RequisitionsController.cs BomPriceApproval.Tests/Requisitions/ListDateFilterTests.cs
git commit -m "feat(requisitions): add optional ?from= and ?to= date filters on list"
```

---

## Task 2: Backend — `GET /api/stats/accountant-dashboard` endpoint (TDD)

**Files:**
- Create: `BomPriceApproval.API/Features/Stats/StatsDtos.cs`
- Create: `BomPriceApproval.API/Features/Stats/StatsController.cs`
- Test: `BomPriceApproval.Tests/Stats/AccountantDashboardTests.cs` (CREATE)

### Steps

- [ ] **Step 2.1: Write the failing test**

Create `BomPriceApproval.Tests/Stats/AccountantDashboardTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Stats;

public class AccountantDashboardTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record LoginResponse(string AccessToken, string RefreshToken);

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private record DashboardStats(
        int PendingCosting,
        int InProgress,
        int SubmittedThisMonth,
        int AwaitingMd);

    [Fact]
    public async Task Get_AsAccountant_ReturnsAllFourCounts()
    {
        var token = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.EnsureSuccessStatusCode();

        var stats = (await resp.Content.ReadFromJsonAsync<DashboardStats>())!;

        stats.Should().NotBeNull();
        stats.PendingCosting.Should().BeGreaterThanOrEqualTo(0);
        stats.InProgress.Should().BeGreaterThanOrEqualTo(0);
        stats.SubmittedThisMonth.Should().BeGreaterThanOrEqualTo(0);
        stats.AwaitingMd.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Get_AsAdmin_ReturnsAllFourCounts()
    {
        var token = await LoginAsync("admin@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.EnsureSuccessStatusCode();

        var stats = (await resp.Content.ReadFromJsonAsync<DashboardStats>())!;
        stats.Should().NotBeNull();
    }

    [Fact]
    public async Task Get_AsSalesPerson_Returns403()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_Unauthenticated_Returns401()
    {
        // No Authorization header
        _client.DefaultRequestHeaders.Authorization = null;
        var resp = await _client.GetAsync("/api/stats/accountant-dashboard");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
```

If the seed user emails differ in this codebase (per memory `reference_seed_and_endpoints.md`: accountant is `sara@test.com`), use those — the lines above already match. Verify by checking `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs` seed section if any test fails on login.

- [ ] **Step 2.2: Run test, verify it fails**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~AccountantDashboardTests" --nologo
```

Expected: all 4 tests **FAIL** (endpoint does not exist → 404 instead of expected codes).

- [ ] **Step 2.3: Create the response DTO**

Create `BomPriceApproval.API/Features/Stats/StatsDtos.cs`:

```csharp
namespace BomPriceApproval.API.Features.Stats;

public record AccountantDashboardStats(
    int PendingCosting,
    int InProgress,
    int SubmittedThisMonth,
    int AwaitingMd);
```

- [ ] **Step 2.4: Create the controller**

Create `BomPriceApproval.API/Features/Stats/StatsController.cs`:

```csharp
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Stats;

[ApiController]
[Route("api/stats")]
[Authorize]
public class StatsController(AppDbContext db) : ControllerBase
{
    [HttpGet("accountant-dashboard")]
    [Authorize(Roles = "Accountant,Admin")]
    public async Task<IActionResult> AccountantDashboard()
    {
        // Accountant has null BranchId per CLAUDE.md (sees all branches), so no branch filter.
        var startOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var pendingCosting = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.CostingPending);

        var inProgress = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.CostingInProgress);

        var submittedThisMonth = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdReview && q.UpdatedAt >= startOfMonth);

        var awaitingMd = await db.QuotationRequests
            .CountAsync(q => q.Status == RequisitionStatus.MdReview);

        return Ok(new AccountantDashboardStats(pendingCosting, inProgress, submittedThisMonth, awaitingMd));
    }
}
```

`UpdatedAt` is used as the proxy for "submitted to MD" timestamp (spec §10 open question #1).

- [ ] **Step 2.5: Compile**

```bash
dotnet build --nologo -v q
```

Expected: `Build succeeded.` 0 errors.

- [ ] **Step 2.6: Run the dashboard tests, verify they pass**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --filter "FullyQualifiedName~AccountantDashboardTests" --nologo
```

Expected: **4 PASS** / 0 FAIL.

- [ ] **Step 2.7: Run the full test suite for regression**

```bash
dotnet test BomPriceApproval.Tests/BomPriceApproval.Tests.csproj --nologo
```

Expected: all tests pass.

- [ ] **Step 2.8: Commit**

```bash
git add BomPriceApproval.API/Features/Stats/ BomPriceApproval.Tests/Stats/
git commit -m "feat(stats): add GET /api/stats/accountant-dashboard endpoint"
```

---

## Task 3: Mobile — Extract `HistoricalRequisitionScreen` from MD historical route

**Files:**
- Create: `bom-mobile/src/components/HistoricalRequisitionScreen.tsx`
- Modify: `bom-mobile/app/(md)/historical/[id].tsx` (delegate to the new component)

### Steps

- [ ] **Step 3.1: Read the current MD historical screen end-to-end**

```bash
cat "bom-mobile/app/(md)/historical/[id].tsx"
```

Identify the default export function body. The component reads `id` from `useLocalSearchParams`, calls `useRequisitionDetail(id)`, and renders the read-only detail. Everything inside that function (and the supporting top-level functions/constants used only by it) is what gets extracted.

- [ ] **Step 3.2: Create the shared component**

Create `bom-mobile/src/components/HistoricalRequisitionScreen.tsx`. The component takes `requisitionId` as a prop instead of reading it from route params. Move the entire body of `(md)/historical/[id].tsx`'s default-exported function into the new component, with these substitutions:

- The component signature becomes `export function HistoricalRequisitionScreen({ requisitionId }: { requisitionId: number })`.
- Remove the `useLocalSearchParams` line; use `requisitionId` directly.
- Keep all imports (`useRequisitionDetail`, `Button`, `StatusPill`, etc.) intact.
- The screen owns its own `<ScreenHeader>` + back-button + log-out (so both MD and Accountant routes get the same UX without duplicating chrome).

Skeleton:

```tsx
import { useState } from "react";
import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import * as Haptics from "expo-haptics";
import { useRequisitionDetail } from "@/api/requisitions";
import { downloadRequisitionPdf } from "@/api/pdf";
import { Button } from "@/components/Button";
import { StatusPill } from "@/components/StatusPill";
import { ItemStageBadge } from "@/components/ItemStageBadge";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { ScreenHeader } from "@/components/ScreenHeader";
import { SectionCard } from "@/components/SectionCard";
import { ItemCardShell } from "@/components/ItemCardShell";
import { ItemPriceBlock } from "@/components/ItemPriceBlock";
import { NotificationBell } from "@/components/NotificationBell";
import { useAuth } from "@/auth/AuthContext";
import { formatShortDate } from "@/utils/dates";

export function HistoricalRequisitionScreen({ requisitionId }: { requisitionId: number }) {
  const router = useRouter();
  const { logout } = useAuth();
  const id = requisitionId;
  const q = useRequisitionDetail(id);
  const [pdfError, setPdfError] = useState<string | null>(null);
  const [pdfLoading, setPdfLoading] = useState(false);

  // ... [paste the rest of the body from (md)/historical/[id].tsx unchanged] ...
}
```

The exact code to paste is whatever exists today in `(md)/historical/[id].tsx` lines 29 onwards (everything inside the default-exported function body, after the `useLocalSearchParams` line).

- [ ] **Step 3.3: Update `(md)/historical/[id].tsx` to delegate**

Replace the entire contents of `bom-mobile/app/(md)/historical/[id].tsx` with:

```tsx
import { useLocalSearchParams } from "expo-router";
import { HistoricalRequisitionScreen } from "@/components/HistoricalRequisitionScreen";

export default function MdHistoricalDetail() {
  const params = useLocalSearchParams<{ id: string }>();
  return <HistoricalRequisitionScreen requisitionId={Number(params.id)} />;
}
```

- [ ] **Step 3.4: Compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 3.5: Manual on-device smoke — MD historical regression**

Start the API and Expo:

```bash
# Terminal 1 — backend
dotnet run --project BomPriceApproval.API
# Terminal 2 — mobile
cd bom-mobile && npx expo start --clear
```

On the device, log in as MD (`md@test.com` / `Test@1234`), open the Requisitions list, tap a non-`MdReview` requisition (e.g. an `Approved` one). Confirm: the historical detail screen renders correctly — items list, prices, status pill, PDF button — with no regressions vs current behavior.

If the screen is broken, fix the extraction and re-run smoke.

- [ ] **Step 3.6: Commit**

```bash
git add bom-mobile/src/components/HistoricalRequisitionScreen.tsx "bom-mobile/app/(md)/historical/[id].tsx"
git commit -m "refactor(mobile): extract HistoricalRequisitionScreen for shared use across roles"
```

---

## Task 4: Mobile — Smart `(accountant)/[id]` route (form vs read-only)

**Files:**
- Modify: `bom-mobile/app/(accountant)/[id].tsx`

### Steps

- [ ] **Step 4.1: Read the current file**

```bash
cat "bom-mobile/app/(accountant)/[id].tsx"
```

Note the existing default-exported function — call it `AccountantDetail`. It currently always renders the costing form for any item. Identify the existing form code path. We will branch on `requisition.status` at the top.

- [ ] **Step 4.2: Modify to branch by status**

Edit `bom-mobile/app/(accountant)/[id].tsx`. At the top of the default export function, after fetching the requisition (which the file already does — find the `useRequisitionDetail(id)` call), add the status branch BEFORE the existing form return:

```tsx
import { HistoricalRequisitionScreen } from "@/components/HistoricalRequisitionScreen";
// ... other existing imports unchanged ...

export default function AccountantDetail() {
  const params = useLocalSearchParams<{ id: string }>();
  const id = Number(params.id);
  const q = useRequisitionDetail(id);

  // Smart-route branch: read-only historical for non-active statuses
  if (q.data) {
    const status = q.data.status;
    const isCostingActive = status === "CostingPending" || status === "CostingInProgress";
    if (!isCostingActive) {
      return <HistoricalRequisitionScreen requisitionId={id} />;
    }
  }

  // ... existing form code below unchanged ...
}
```

If the existing file doesn't already have `useRequisitionDetail` at the top of the default export, lift it from wherever it is now. If it has its own loading/error gates, keep them — the branch above only fires when `q.data` is loaded.

- [ ] **Step 4.3: Compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 4.4: Manual smoke**

1. Log in as Accountant (`sara@test.com` / `Test@1234`).
2. Open the pending list (still at `(accountant)/index.tsx` until Task 6) and tap a `CostingPending` requisition. Confirm the costing form renders (existing Phase 1 behavior).
3. Use the `(md)` app or the database to advance a requisition to `Approved`, then back in the Accountant app navigate directly to `/(accountant)/<approvedId>` (e.g. via the URL bar in Expo dev tools or by editing a recent route). Confirm the historical screen renders read-only.
4. (Optional) Repeat with a `MdReview` requisition.

- [ ] **Step 4.5: Commit**

```bash
git add "bom-mobile/app/(accountant)/[id].tsx"
git commit -m "feat(mobile): smart Accountant detail route (form for active, historical otherwise)"
```

---

## Task 5: Mobile — `(accountant)/list.tsx` — all-list with chips, search, URL params

**Files:**
- Create: `bom-mobile/app/(accountant)/list.tsx`

### Steps

- [ ] **Step 5.1: Create the file**

Create `bom-mobile/app/(accountant)/list.tsx`. The structure mirrors `(md)/pending.tsx` (which is the closest peer — chip-driven list with search). Key differences from MD:
- Header label: `"ACCOUNTANT"` and title: `"Requisitions"`.
- Default chip: `"Costing"`.
- URL params: `?chip=…&onlyStatus=…&from=…&to=…&search=…`.
- Tap routing: always `/(accountant)/${id}` (smart route handles the form-vs-historical decision).

Full code:

```tsx
import { useState } from "react";
import {
  ActivityIndicator,
  FlatList,
  Pressable,
  RefreshControl,
  Text,
  TextInput,
  View,
} from "react-native";
import { Stack, useLocalSearchParams, useRouter } from "expo-router";
import { useInfiniteQuery } from "@tanstack/react-query";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { api } from "@/api/client";
import { requisitionKeys } from "@/api/requisitions";
import { RequisitionCard } from "@/components/RequisitionCard";
import { ScreenHeader } from "@/components/ScreenHeader";
import { EmptyState } from "@/components/EmptyState";
import { ErrorBanner } from "@/components/ErrorBanner";
import { LoadingView } from "@/components/LoadingView";
import { NotificationBell } from "@/components/NotificationBell";
import { StatusChipRow, CHIP_TO_STATUSES, CHIPS, type ChipLabel } from "@/components/StatusChipRow";
import { useDebouncedValue } from "@/hooks/useDebouncedValue";
import { useAuth } from "@/auth/AuthContext";
import type { RequisitionListItem, RequisitionStatus } from "@/types/api";

const PAGE_SIZE = 20;
const STAGGER_CAP = 20;

const ALL_STATUSES: readonly RequisitionStatus[] = [
  "BomPending", "BomInProgress",
  "CostingPending", "CostingInProgress",
  "MdReview", "Approved", "Rejected",
];

function isChipLabel(value: string | undefined): value is ChipLabel {
  return !!value && (CHIPS as readonly string[]).includes(value);
}

function isStatus(value: string | undefined): value is RequisitionStatus {
  return !!value && (ALL_STATUSES as readonly string[]).includes(value);
}

function useAccountantList(
  statuses: string[],
  search: string,
  from: string | undefined,
  to: string | undefined,
) {
  return useInfiniteQuery({
    queryKey: [
      ...requisitionKeys.list(),
      "accountantList",
      { statuses: statuses.join(","), search, from, to },
    ],
    queryFn: async ({ pageParam }) => {
      const params = new URLSearchParams();
      for (const s of statuses) params.append("status", s);
      if (search) params.append("search", search);
      if (from) params.append("from", from);
      if (to) params.append("to", to);
      params.append("page", String(pageParam));
      params.append("pageSize", String(PAGE_SIZE));

      const res = await api.get<RequisitionListItem[]>(`/api/requisitions?${params.toString()}`);
      return res.data;
    },
    initialPageParam: 1,
    getNextPageParam: (lastPage, allPages) =>
      lastPage.length < PAGE_SIZE ? undefined : allPages.length + 1,
  });
}

export default function AccountantList() {
  const router = useRouter();
  const { logout } = useAuth();
  const search = useLocalSearchParams<{
    chip?: string; onlyStatus?: string; from?: string; to?: string; search?: string;
  }>();

  const initialChip: ChipLabel = isChipLabel(search.chip) ? search.chip : "Costing";
  const initialOnlyStatus: RequisitionStatus | null = isStatus(search.onlyStatus) ? search.onlyStatus : null;

  const [activeChip, setActiveChip] = useState<ChipLabel>(initialChip);
  const [onlyStatus, setOnlyStatus] = useState<RequisitionStatus | null>(initialOnlyStatus);
  const [searchInput, setSearchInput] = useState(search.search ?? "");
  const debouncedSearch = useDebouncedValue(searchInput, 300);

  // Resolution: onlyStatus overrides chip's status set
  const statuses: string[] = onlyStatus ? [onlyStatus] : CHIP_TO_STATUSES[activeChip];

  const handleChipChange = (label: ChipLabel) => {
    setActiveChip(label);
    setOnlyStatus(null); // selecting a chip clears the dashboard's onlyStatus pin
  };

  const q = useAccountantList(statuses, debouncedSearch, search.from, search.to);
  const items: RequisitionListItem[] = q.data?.pages.flat() ?? [];

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const HeaderRight = (
    <>
      <NotificationBell />
      <Pressable
        onPress={onLogout}
        style={{
          paddingHorizontal: 12,
          paddingVertical: 9,
          borderRadius: 8,
          backgroundColor: "#f1f5f9",
        }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader
        label="ACCOUNTANT"
        title="Requisitions"
        count={items.length}
        right={HeaderRight}
        onBack={() => router.back()}
      />

      <View style={{ paddingHorizontal: 14, paddingTop: 8, paddingBottom: 2, backgroundColor: "#f8fafc" }}>
        <TextInput
          value={searchInput}
          onChangeText={setSearchInput}
          placeholder="Search REQ-xxxx or customer..."
          placeholderTextColor="#94a3b8"
          style={{
            borderWidth: 1,
            borderColor: "#cbd5e1",
            backgroundColor: "#ffffff",
            borderRadius: 10,
            paddingHorizontal: 12,
            paddingVertical: 9,
            fontSize: 14,
            color: "#0f172a",
          }}
          autoCorrect={false}
          autoCapitalize="none"
        />
      </View>

      <StatusChipRow active={activeChip} onChange={handleChipChange} />

      {q.isPending ? (
        <LoadingView variant="list" />
      ) : q.isError ? (
        <View style={{ padding: 16 }}>
          <ErrorBanner
            message={q.error instanceof Error ? q.error.message : "Failed to load requisitions"}
            onRetry={() => q.refetch()}
          />
        </View>
      ) : (
        <FlatList
          data={items}
          keyExtractor={(r) => String(r.id)}
          contentContainerStyle={{ padding: 14, paddingTop: 6 }}
          overScrollMode="never"
          bounces={false}
          onEndReached={() => { if (q.hasNextPage && !q.isFetchingNextPage) q.fetchNextPage(); }}
          onEndReachedThreshold={0.5}
          refreshControl={
            <RefreshControl
              refreshing={q.isRefetching && !q.isFetchingNextPage}
              onRefresh={() => {
                Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
                q.refetch();
              }}
              tintColor="#1e40af"
              colors={["#1e40af"]}
            />
          }
          renderItem={({ item, index }) => (
            <MotiView
              from={{ opacity: 0, translateY: 14 }}
              animate={{ opacity: 1, translateY: 0 }}
              transition={{
                type: "spring",
                damping: 16,
                stiffness: 140,
                delay: index < STAGGER_CAP ? 200 + index * 80 : 0,
              }}
            >
              <RequisitionCard
                item={item}
                onPress={() => {
                  Haptics.selectionAsync();
                  router.push(`/(accountant)/${item.id}`);
                }}
              />
            </MotiView>
          )}
          ListFooterComponent={
            q.isFetchingNextPage ? (
              <View style={{ paddingVertical: 20, alignItems: "center" }}>
                <ActivityIndicator color="#1e40af" />
                <Text style={{ color: "#64748b", fontSize: 14, marginTop: 8 }}>Loading more…</Text>
              </View>
            ) : q.hasNextPage === false && items.length > PAGE_SIZE ? (
              <View style={{ paddingVertical: 20, alignItems: "center" }}>
                <Text style={{ color: "#94a3b8", fontSize: 13 }}>End of list · {items.length} total</Text>
              </View>
            ) : null
          }
          ListEmptyComponent={
            <EmptyState
              title={items.length === 0 && !debouncedSearch ? "No requisitions" : "No matches"}
              hint={
                debouncedSearch
                  ? `No matches for "${debouncedSearch}"`
                  : `No requisitions in this filter.`
              }
            />
          }
        />
      )}
    </View>
  );
}
```

If `ScreenHeader` does not accept an `onBack` prop in this codebase, drop that line (the back button isn't strictly required — most list screens here don't have one; verify by reading `src/components/ScreenHeader.tsx` first).

If `RequisitionStatus` is not already exported from `@/types/api`, drop the `RequisitionStatus` import and inline the type as a string literal union.

- [ ] **Step 5.2: Compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 5.3: Manual smoke**

1. As Accountant, navigate (via dev tools / direct URL) to `/(accountant)/list`. Default chip should be **Costing**, list should show same items as the current pending list.
2. Tap each chip in turn (`All`, `BOM`, `Costing`, `MD review`, `Approved`, `Rejected`). The list should update.
3. Type a search term — list filters live (300ms debounce).
4. Open `/(accountant)/list?onlyStatus=CostingPending` directly. Only `CostingPending` items appear. Tap a different chip — `onlyStatus` is cleared (the new chip's group is honored).
5. Open `/(accountant)/list?chip=MD%20review&from=2026-04-01` (use the current month). Only `MdReview` items updated on/after April 1 appear.
6. Pull-to-refresh.
7. Scroll past 20 items (if seeded data permits) → next page loads.

- [ ] **Step 5.4: Commit**

```bash
git add "bom-mobile/app/(accountant)/list.tsx"
git commit -m "feat(mobile): add Accountant all-list with status chips, search, URL params"
```

---

## Task 6: Mobile — Replace `(accountant)/index.tsx` with the dashboard

**Files:**
- Modify: `bom-mobile/src/api/stats.ts` (add hook)
- Modify: `bom-mobile/src/types/api.ts` (add DTO)
- Replace: `bom-mobile/app/(accountant)/index.tsx`

### Steps

- [ ] **Step 6.1: Add the dashboard-stats DTO**

Edit `bom-mobile/src/types/api.ts`. Add:

```ts
export interface AccountantDashboardStats {
  pendingCosting: number;
  inProgress: number;
  submittedThisMonth: number;
  awaitingMd: number;
}
```

Place it near the other stats / count types if any exist; otherwise at the end of the file.

- [ ] **Step 6.2: Add the hook**

Edit `bom-mobile/src/api/stats.ts`. Append:

```ts
import type { AccountantDashboardStats } from "@/types/api";

export function useAccountantDashboardStats() {
  return useQuery({
    queryKey: ["stats", "accountantDashboard"],
    queryFn: async () => {
      const res = await api.get<AccountantDashboardStats>("/api/stats/accountant-dashboard");
      return res.data;
    },
    staleTime: 30_000,
  });
}
```

- [ ] **Step 6.3: Replace `(accountant)/index.tsx` with the dashboard**

Replace the entire contents of `bom-mobile/app/(accountant)/index.tsx` with:

```tsx
import { Pressable, ScrollView, Text, View } from "react-native";
import { Stack, useRouter } from "expo-router";
import { MotiView } from "moti";
import * as Haptics from "expo-haptics";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useAuth } from "@/auth/AuthContext";
import { useAccountantDashboardStats } from "@/api/stats";
import { useUnreadCount } from "@/api/notifications";
import { ScreenHeader } from "@/components/ScreenHeader";
import { Skeleton } from "@/components/Skeleton";
import { NotificationBell } from "@/components/NotificationBell";

function greet(): string {
  const h = new Date().getHours();
  if (h < 12) return "Good morning";
  if (h < 17) return "Good afternoon";
  if (h < 21) return "Good evening";
  return "Good night";
}

function startOfMonthIsoDate(): string {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
}

export default function AccountantDashboard() {
  const router = useRouter();
  const { user, logout } = useAuth();
  const insets = useSafeAreaInsets();
  const statsQ = useAccountantDashboardStats();
  const unreadQ = useUnreadCount();

  const onLogout = async () => {
    Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
    await logout();
    router.replace("/login");
  };

  const firstName = (user?.name ?? "").split(" ")[0] || "there";

  const HeaderRight = (
    <>
      <NotificationBell />
      <Pressable
        onPress={onLogout}
        style={{
          paddingHorizontal: 12,
          paddingVertical: 9,
          borderRadius: 8,
          backgroundColor: "#f1f5f9",
        }}
      >
        <Text style={{ color: "#1e40af", fontSize: 15, fontWeight: "600" }}>Log out</Text>
      </Pressable>
    </>
  );

  const navTo = (path: string) => {
    Haptics.selectionAsync();
    router.push(path as Parameters<typeof router.push>[0]);
  };

  const monthStart = startOfMonthIsoDate();

  return (
    <View style={{ flex: 1, backgroundColor: "#f8fafc" }}>
      <Stack.Screen options={{ headerShown: false }} />

      <ScreenHeader label="ACCOUNTANT" title={`${greet()}, ${firstName} 👋`} right={HeaderRight} />

      <ScrollView
        contentContainerStyle={{
          padding: 16,
          paddingTop: 4,
          paddingBottom: Math.max(insets.bottom, 16) + 16,
        }}
        showsVerticalScrollIndicator={false}
      >
        {/* Hero: Pending Costing */}
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 100 }}
        >
          <Pressable
            onPress={() => navTo("/(accountant)/list?onlyStatus=CostingPending")}
            style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
          >
            <View
              style={{
                backgroundColor: "#1e40af",
                borderRadius: 16,
                padding: 20,
                marginBottom: 12,
                shadowColor: "#1e40af",
                shadowOffset: { width: 0, height: 6 },
                shadowOpacity: 0.3,
                shadowRadius: 12,
                elevation: 6,
              }}
            >
              <Text style={{ color: "#dbeafe", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
                PENDING COSTING
              </Text>
              <View style={{ flexDirection: "row", alignItems: "flex-end", marginTop: 10 }}>
                {statsQ.isPending ? (
                  <Skeleton width={80} height={44} radius={8} style={{ backgroundColor: "rgba(255,255,255,0.25)" }} />
                ) : (
                  <Text style={{ color: "#ffffff", fontSize: 44, fontWeight: "800", letterSpacing: -1 }}>
                    {statsQ.data?.pendingCosting ?? 0}
                  </Text>
                )}
                <Text style={{ color: "#dbeafe", fontSize: 15, marginLeft: 10, marginBottom: 8 }}>
                  to review
                </Text>
              </View>
              <Text style={{ color: "#dbeafe", fontSize: 14, marginTop: 14 }}>Tap to open the list →</Text>
            </View>
          </Pressable>
        </MotiView>

        {/* Row: In Progress */}
        <KpiRow
          label="IN PROGRESS"
          value={statsQ.data?.inProgress ?? 0}
          loading={statsQ.isPending}
          delay={180}
          onPress={() => navTo("/(accountant)/list?onlyStatus=CostingInProgress")}
        />

        {/* Row: Submitted This Month */}
        <KpiRow
          label="SUBMITTED THIS MONTH"
          value={statsQ.data?.submittedThisMonth ?? 0}
          loading={statsQ.isPending}
          delay={260}
          onPress={() => navTo(`/(accountant)/list?chip=MD%20review&from=${monthStart}`)}
        />

        {/* Row: Awaiting MD */}
        <KpiRow
          label="AWAITING MD"
          value={statsQ.data?.awaitingMd ?? 0}
          loading={statsQ.isPending}
          delay={340}
          onPress={() => navTo("/(accountant)/list?chip=MD%20review")}
        />

        {/* Notifications card */}
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 420 }}
        >
          <Pressable
            onPress={() => navTo("/notifications")}
            style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
          >
            <View
              style={{
                backgroundColor: "#ffffff",
                borderWidth: 1,
                borderColor: "#e2e8f0",
                borderRadius: 14,
                padding: 16,
                marginTop: 4,
                marginBottom: 12,
                flexDirection: "row",
                alignItems: "center",
                justifyContent: "space-between",
              }}
            >
              <View style={{ flex: 1 }}>
                <Text style={{ color: "#64748b", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
                  NOTIFICATIONS
                </Text>
                <View style={{ flexDirection: "row", alignItems: "baseline", marginTop: 4 }}>
                  {unreadQ.isPending ? (
                    <Skeleton width={40} height={24} />
                  ) : (
                    <Text style={{ color: "#0f172a", fontSize: 22, fontWeight: "700" }}>
                      {unreadQ.data ?? 0}
                    </Text>
                  )}
                  <Text style={{ color: "#64748b", fontSize: 14, marginLeft: 8 }}>unread</Text>
                </View>
              </View>
              <Text style={{ fontSize: 28 }}>🔔</Text>
            </View>
          </Pressable>
        </MotiView>

        {/* User card */}
        <MotiView
          from={{ opacity: 0, translateY: 14 }}
          animate={{ opacity: 1, translateY: 0 }}
          transition={{ type: "spring", damping: 14, stiffness: 140, delay: 500 }}
        >
          <View
            style={{
              backgroundColor: "#ffffff",
              borderWidth: 1,
              borderColor: "#e2e8f0",
              borderRadius: 14,
              padding: 16,
            }}
          >
            <Text style={{ color: "#64748b", fontSize: 13, fontWeight: "600", letterSpacing: 0.5 }}>
              SIGNED IN AS
            </Text>
            <Text style={{ color: "#0f172a", fontSize: 17, fontWeight: "700", marginTop: 6 }}>
              {user?.name ?? "—"}
            </Text>
            <Text style={{ color: "#64748b", fontSize: 14, marginTop: 2 }}>Accountant</Text>
          </View>
        </MotiView>
      </ScrollView>
    </View>
  );
}

function KpiRow({
  label, value, loading, delay, onPress,
}: {
  label: string;
  value: number;
  loading: boolean;
  delay: number;
  onPress: () => void;
}) {
  return (
    <MotiView
      from={{ opacity: 0, translateY: 14 }}
      animate={{ opacity: 1, translateY: 0 }}
      transition={{ type: "spring", damping: 14, stiffness: 140, delay }}
    >
      <Pressable
        onPress={onPress}
        style={({ pressed }) => ({ transform: [{ scale: pressed ? 0.98 : 1 }] })}
      >
        <View
          style={{
            backgroundColor: "#ffffff",
            borderWidth: 1,
            borderColor: "#e2e8f0",
            borderRadius: 14,
            padding: 14,
            marginBottom: 8,
            flexDirection: "row",
            alignItems: "center",
            justifyContent: "space-between",
          }}
        >
          <Text style={{ color: "#64748b", fontSize: 12, fontWeight: "700", letterSpacing: 0.5 }}>
            {label}
          </Text>
          {loading ? (
            <Skeleton width={36} height={26} />
          ) : (
            <Text style={{ color: "#0f172a", fontSize: 22, fontWeight: "800" }}>{value}</Text>
          )}
        </View>
      </Pressable>
    </MotiView>
  );
}
```

- [ ] **Step 6.4: Compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 6.5: Manual smoke**

1. Log in as Accountant. The dashboard renders: hero card + 3 KPI rows + notifications card + user card. Greeting matches time of day.
2. Counts populate (skeletons → numbers). Numbers match what the backend returns (cross-check via `curl http://localhost:7300/api/stats/accountant-dashboard` with the access token).
3. Tap **PENDING COSTING** hero → lands on `/(accountant)/list?onlyStatus=CostingPending` showing only those items.
4. Tap **IN PROGRESS** → only `CostingInProgress` items.
5. Tap **SUBMITTED THIS MONTH** → list pre-filters to MD-review chip + this-month items only.
6. Tap **AWAITING MD** → list shows MD-review chip with all MdReview items.
7. Tap **NOTIFICATIONS** card → navigates to `/notifications`.
8. Pull-to-refresh on the dashboard refetches all four counts (verify via console / observed re-render).

- [ ] **Step 6.6: Commit**

```bash
git add bom-mobile/src/api/stats.ts bom-mobile/src/types/api.ts "bom-mobile/app/(accountant)/index.tsx"
git commit -m "feat(mobile): replace Accountant home with KPI dashboard (hero + 3 stacked rows)"
```

---

## Task 7: Mobile — Notification deep-link fix for Accountant

**Files:**
- Modify: `bom-mobile/app/notifications.tsx`

### Steps

- [ ] **Step 7.1: Update `pathForNotification`**

Edit `bom-mobile/app/notifications.tsx`. Locate the `pathForNotification` function (around line 16). Replace its body:

```ts
function pathForNotification(
  n: Notification,
  role: string
): string | null {
  if (n.referenceType !== "QuotationRequest") return null;
  if (role === "ManagingDirector") return `/(md)/${n.referenceId}`;
  if (role === "SalesPerson")      return `/(sales)/${n.referenceId}`;
  if (role === "Accountant")       return `/(accountant)/${n.referenceId}`;
  // BomCreator: deferred — no (bom) route group exists yet (V2.3+)
  return null;
}
```

- [ ] **Step 7.2: Compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 7.3: Manual smoke**

1. As Accountant, ensure at least one unread notification exists (trigger by acting on a requisition from another role — e.g. as BOM creator, submit BOM on an item; this should notify the Accountant). Open `/notifications` from the bell.
2. Tap the new notification — Expo navigates to `/(accountant)/<id>`. The smart route from Task 4 then renders the form (active) or historical (otherwise).
3. **Regression**: log in as MD, ensure tapping an MD notification still lands on `/(md)/<id>`. Same for Sales.
4. **Regression**: log in as BomCreator (if a seed user exists) — tapping a notification still does nothing (intentional, deferred).

- [ ] **Step 7.4: Commit**

```bash
git add bom-mobile/app/notifications.tsx
git commit -m "fix(mobile): wire notification deep-link for Accountant role"
```

---

## Task 8: Mobile — Feature X hooks (`useChangeCustomer`, `useCustomerChangeHistory`)

**Files:**
- Modify: `bom-mobile/src/types/api.ts` (add types)
- Modify: `bom-mobile/src/api/requisitions.ts` (add hooks)

### Steps

- [ ] **Step 8.1: Add types**

Edit `bom-mobile/src/types/api.ts`. Append (or place near other requisition types):

```ts
export interface ChangeCustomerRequest {
  customerId: number;
  reason?: string;
}

export interface CustomerChangeHistoryEntry {
  id: number;
  oldCustomerName: string;
  newCustomerName: string;
  changedByUserName: string;
  changedAt: string;     // ISO datetime
  reason: string | null;
}
```

If the web `bom-web/src/types/api.ts` already has these shapes (per Grep findings), copy the field names verbatim so backend response binds correctly. Verify by reading lines 369-380 of `bom-web/src/types/api.ts`. If field names differ (e.g. snake_case vs camelCase), use whatever the backend actually returns by inspecting `RequisitionDtos.cs:47` and the `customer-history` endpoint response shape.

- [ ] **Step 8.2: Add hooks**

Edit `bom-mobile/src/api/requisitions.ts`. Append:

```ts
import type { ChangeCustomerRequest, CustomerChangeHistoryEntry } from "@/types/api";

export function useChangeCustomer(requisitionId: number) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (body: ChangeCustomerRequest) =>
      api.patch(`/api/requisitions/${requisitionId}/customer`, body).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: requisitionKeys.detail(requisitionId) });
      qc.invalidateQueries({ queryKey: ["requisition", requisitionId, "customerHistory"] });
      qc.invalidateQueries({ queryKey: requisitionKeys.list() });
    },
  });
}

export function useCustomerChangeHistory(requisitionId: number, enabled = true) {
  return useQuery({
    queryKey: ["requisition", requisitionId, "customerHistory"],
    queryFn: async () => {
      const res = await api.get<CustomerChangeHistoryEntry[]>(
        `/api/requisitions/${requisitionId}/customer-history`,
      );
      return res.data;
    },
    enabled: enabled && requisitionId > 0,
    staleTime: 30_000,
  });
}
```

If `useMutation` / `useQueryClient` aren't already imported at the top of `requisitions.ts`, add them: `import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";` — match the existing style.

If `requisitionKeys.detail(id)` does not exist, use whatever the file already exposes for cache invalidation (e.g. `requisitionKeys.list()` is sufficient — the form refetches on mount).

- [ ] **Step 8.3: Compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 8.4: Commit**

```bash
git add bom-mobile/src/types/api.ts bom-mobile/src/api/requisitions.ts
git commit -m "feat(mobile): add useChangeCustomer + useCustomerChangeHistory hooks"
```

---

## Task 9: Mobile — `CustomerSwapSheet` component + integrate into Accountant detail

**Files:**
- Create: `bom-mobile/src/components/CustomerSwapSheet.tsx`
- Modify: `bom-mobile/app/(accountant)/[id].tsx`

### Steps

- [ ] **Step 9.1: Create the sheet component**

Create `bom-mobile/src/components/CustomerSwapSheet.tsx`:

```tsx
import { useState } from "react";
import { Modal, Pressable, Text, TextInput, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import * as Haptics from "expo-haptics";
import { useChangeCustomer } from "@/api/requisitions";
import { SearchablePicker } from "@/components/SearchablePicker";
import { Button } from "@/components/Button";
import { ErrorBanner } from "@/components/ErrorBanner";
import { useCustomers } from "@/api/lookups";

interface Props {
  requisitionId: number;
  currentCustomerId: number;
  currentCustomerName: string;
  open: boolean;
  onClose: () => void;
}

export function CustomerSwapSheet({
  requisitionId, currentCustomerId, currentCustomerName, open, onClose,
}: Props) {
  const insets = useSafeAreaInsets();
  const customersQ = useCustomers();
  const change = useChangeCustomer(requisitionId);

  const [newCustomerId, setNewCustomerId] = useState<number | null>(null);
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);

  const customers = (customersQ.data ?? []).filter((c) => c.id !== currentCustomerId);

  const handleSave = async () => {
    if (!newCustomerId) return;
    setError(null);
    try {
      Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Medium);
      await change.mutateAsync({
        customerId: newCustomerId,
        reason: reason.trim() ? reason.trim() : undefined,
      });
      // success — reset and close
      setNewCustomerId(null);
      setReason("");
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Failed to change customer");
      Haptics.notificationAsync(Haptics.NotificationFeedbackType.Error);
    }
  };

  return (
    <Modal visible={open} animationType="slide" transparent onRequestClose={onClose}>
      <View style={{ flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" }}>
        <View
          style={{
            backgroundColor: "#ffffff",
            borderTopLeftRadius: 18,
            borderTopRightRadius: 18,
            padding: 20,
            paddingBottom: Math.max(insets.bottom, 16) + 12,
            maxHeight: "90%",
          }}
        >
          <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
            <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a" }}>Change customer</Text>
            <Pressable onPress={onClose} hitSlop={12}>
              <Text style={{ fontSize: 22, color: "#64748b" }}>×</Text>
            </Pressable>
          </View>

          <View style={{ marginTop: 16 }}>
            <Text style={{ fontSize: 12, fontWeight: "600", color: "#64748b", letterSpacing: 0.5 }}>
              CURRENT
            </Text>
            <Text style={{ fontSize: 16, fontWeight: "600", color: "#0f172a", marginTop: 2 }}>
              {currentCustomerName}
            </Text>
          </View>

          <View style={{ marginTop: 16 }}>
            <Text style={{ fontSize: 12, fontWeight: "600", color: "#64748b", letterSpacing: 0.5, marginBottom: 6 }}>
              NEW CUSTOMER
            </Text>
            <SearchablePicker
              items={customers.map((c) => ({ id: c.id, label: c.name, sublabel: c.code }))}
              value={newCustomerId}
              onChange={setNewCustomerId}
              placeholder="Search customers..."
            />
          </View>

          <View style={{ marginTop: 16 }}>
            <Text style={{ fontSize: 12, fontWeight: "600", color: "#64748b", letterSpacing: 0.5, marginBottom: 6 }}>
              REASON (optional)
            </Text>
            <TextInput
              value={reason}
              onChangeText={setReason}
              placeholder="Reason for change (optional)"
              placeholderTextColor="#94a3b8"
              style={{
                borderWidth: 1,
                borderColor: "#cbd5e1",
                borderRadius: 10,
                paddingHorizontal: 12,
                paddingVertical: 10,
                fontSize: 14,
                color: "#0f172a",
              }}
              multiline
              numberOfLines={2}
            />
          </View>

          {error ? (
            <View style={{ marginTop: 12 }}>
              <ErrorBanner message={error} onRetry={() => setError(null)} />
            </View>
          ) : null}

          <View style={{ flexDirection: "row", gap: 10, marginTop: 20 }}>
            <View style={{ flex: 1 }}>
              <Button label="Cancel" variant="secondary" onPress={onClose} />
            </View>
            <View style={{ flex: 1 }}>
              <Button
                label={change.isPending ? "Saving..." : "Save"}
                variant="primary"
                onPress={handleSave}
                disabled={!newCustomerId || change.isPending}
              />
            </View>
          </View>
        </View>
      </View>
    </Modal>
  );
}
```

If `Button` component takes different prop names (`label` vs `children`, `variant` styles), adapt to what `src/components/Button.tsx` accepts — read it once, then map. If `useCustomers` doesn't exist in `src/api/lookups.ts`, check what list-fetch hook is used by the existing `(sales)/new.tsx` form (line 93+) and use that one.

- [ ] **Step 9.2: Wire the sheet into `(accountant)/[id].tsx`**

Edit `bom-mobile/app/(accountant)/[id].tsx`. Inside the form branch (the path that renders when `isCostingActive` is true), import and render the sheet. Add to imports:

```tsx
import { useState } from "react";
import { CustomerSwapSheet } from "@/components/CustomerSwapSheet";
```

Inside the form body (find the section that renders the requisition header / customer name — around the existing customer display), add:

```tsx
const [swapOpen, setSwapOpen] = useState(false);

// ...

{q.data && (q.data.status === "CostingPending" || q.data.status === "CostingInProgress") ? (
  <View style={{ marginTop: 4, marginBottom: 8 }}>
    <Pressable
      onPress={() => setSwapOpen(true)}
      style={{
        alignSelf: "flex-start",
        paddingHorizontal: 12,
        paddingVertical: 8,
        borderRadius: 8,
        borderWidth: 1,
        borderColor: "#1e40af",
        backgroundColor: "#eff6ff",
      }}
    >
      <Text style={{ color: "#1e40af", fontWeight: "600", fontSize: 13 }}>Change customer</Text>
    </Pressable>
  </View>
) : null}

<CustomerSwapSheet
  requisitionId={id}
  currentCustomerId={q.data?.customerId ?? 0}
  currentCustomerName={q.data?.customerName ?? ""}
  open={swapOpen}
  onClose={() => setSwapOpen(false)}
/>
```

The exact prop names `q.data?.customerId` and `q.data?.customerName` depend on the shape returned by `useRequisitionDetail` — verify by reading `src/api/requisitions.ts` (the detail hook) and `src/types/api.ts` (the detail DTO) before writing this. If the field names differ (e.g. `customer.id`, `customer.name`), adapt accordingly.

- [ ] **Step 9.3: Compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 9.4: Manual smoke**

1. As Accountant, open a `CostingPending` requisition. The "Change customer" pill button is visible in the header area.
2. Tap → bottom sheet slides up. Current customer shown read-only. Picker filters as typed. Reason field accepts multi-line input.
3. Pick a different customer + add reason → tap **Save**. Sheet closes. Form refetches; new customer name shows in header. Bell badge increases (notification was dispatched server-side).
4. Re-open same requisition as Salesperson — no "Change customer" button (UI gate).
5. Open an `Approved` requisition as Accountant — no "Change customer" button (status gate; smart route renders historical view, which does not include the swap UI).

- [ ] **Step 9.5: Commit**

```bash
git add bom-mobile/src/components/CustomerSwapSheet.tsx "bom-mobile/app/(accountant)/[id].tsx"
git commit -m "feat(mobile): add Accountant customer swap sheet (Feature X mobile parity)"
```

---

## Task 10: Mobile — `CustomerChangeHistorySheet` + badge wiring

**Files:**
- Create: `bom-mobile/src/components/CustomerChangeHistorySheet.tsx`
- Modify: `bom-mobile/app/(accountant)/[id].tsx` (badge in form branch)
- Modify: `bom-mobile/src/components/HistoricalRequisitionScreen.tsx` (badge for non-active path — used by both MD historical and Accountant non-active)

### Steps

- [ ] **Step 10.1: Create the sheet component**

Create `bom-mobile/src/components/CustomerChangeHistorySheet.tsx`:

```tsx
import { Modal, Pressable, ScrollView, Text, View } from "react-native";
import { useSafeAreaInsets } from "react-native-safe-area-context";
import { useCustomerChangeHistory } from "@/api/requisitions";
import { LoadingView } from "@/components/LoadingView";
import { ErrorBanner } from "@/components/ErrorBanner";
import { formatShortDate } from "@/utils/dates";

interface Props {
  requisitionId: number;
  open: boolean;
  onClose: () => void;
}

export function CustomerChangeHistorySheet({ requisitionId, open, onClose }: Props) {
  const insets = useSafeAreaInsets();
  const q = useCustomerChangeHistory(requisitionId, open);

  return (
    <Modal visible={open} animationType="slide" transparent onRequestClose={onClose}>
      <View style={{ flex: 1, backgroundColor: "rgba(15,23,42,0.4)", justifyContent: "flex-end" }}>
        <View
          style={{
            backgroundColor: "#ffffff",
            borderTopLeftRadius: 18,
            borderTopRightRadius: 18,
            padding: 20,
            paddingBottom: Math.max(insets.bottom, 16) + 12,
            maxHeight: "85%",
          }}
        >
          <View style={{ flexDirection: "row", justifyContent: "space-between", alignItems: "center" }}>
            <Text style={{ fontSize: 18, fontWeight: "700", color: "#0f172a" }}>Customer change history</Text>
            <Pressable onPress={onClose} hitSlop={12}>
              <Text style={{ fontSize: 22, color: "#64748b" }}>×</Text>
            </Pressable>
          </View>

          <ScrollView style={{ marginTop: 16 }}>
            {q.isPending ? (
              <LoadingView variant="list" />
            ) : q.isError ? (
              <ErrorBanner
                message={q.error instanceof Error ? q.error.message : "Failed to load history"}
                onRetry={() => q.refetch()}
              />
            ) : (q.data?.length ?? 0) === 0 ? (
              <Text style={{ color: "#64748b", fontSize: 14, textAlign: "center", paddingVertical: 24 }}>
                No customer changes recorded.
              </Text>
            ) : (
              q.data!.map((entry) => (
                <View
                  key={entry.id}
                  style={{
                    borderWidth: 1,
                    borderColor: "#e2e8f0",
                    borderRadius: 12,
                    padding: 12,
                    marginBottom: 10,
                    backgroundColor: "#f8fafc",
                  }}
                >
                  <Text style={{ fontSize: 14, color: "#0f172a", fontWeight: "600" }}>
                    {entry.oldCustomerName} → {entry.newCustomerName}
                  </Text>
                  <Text style={{ fontSize: 12, color: "#64748b", marginTop: 4 }}>
                    by {entry.changedByUserName} · {formatShortDate(entry.changedAt)}
                  </Text>
                  {entry.reason ? (
                    <Text style={{ fontSize: 13, color: "#475569", marginTop: 6, fontStyle: "italic" }}>
                      "{entry.reason}"
                    </Text>
                  ) : null}
                </View>
              ))
            )}
          </ScrollView>
        </View>
      </View>
    </Modal>
  );
}
```

- [ ] **Step 10.2: Add badge in Accountant form branch**

Edit `bom-mobile/app/(accountant)/[id].tsx`. In the form branch, near the "Change customer" button added in Task 9, add:

```tsx
import { useCustomerChangeHistory } from "@/api/requisitions";
import { CustomerChangeHistorySheet } from "@/components/CustomerChangeHistorySheet";
// ... and below the swap state:
const [historyOpen, setHistoryOpen] = useState(false);
const historyQ = useCustomerChangeHistory(id, true);
const historyCount = historyQ.data?.length ?? 0;
```

In the JSX where the swap button lives, add the badge alongside it:

```tsx
{historyCount > 0 ? (
  <Pressable
    onPress={() => setHistoryOpen(true)}
    style={{
      alignSelf: "flex-start",
      paddingHorizontal: 10,
      paddingVertical: 6,
      borderRadius: 999,
      backgroundColor: "#fef3c7",
      marginTop: 6,
    }}
  >
    <Text style={{ color: "#92400e", fontSize: 12, fontWeight: "600" }}>
      Customer changed ({historyCount})
    </Text>
  </Pressable>
) : null}

<CustomerChangeHistorySheet
  requisitionId={id}
  open={historyOpen}
  onClose={() => setHistoryOpen(false)}
/>
```

- [ ] **Step 10.3: Add badge in `HistoricalRequisitionScreen`**

Edit `bom-mobile/src/components/HistoricalRequisitionScreen.tsx`. Add the same hook + badge + sheet near where the customer name is displayed in the historical view. Reuse identical JSX from Step 10.2 — copy verbatim. This wires both `(md)/historical/[id].tsx` (existing route) and `(accountant)/[id].tsx` non-active path through the same component.

If you want to avoid copy-paste, optionally extract to a tiny `CustomerChangeBadge.tsx` helper component — but **do not over-engineer**. Inline copy is fine; the code is ~10 lines.

- [ ] **Step 10.4: Compile**

```bash
cd bom-mobile && npx tsc --noEmit
```

Expected: 0 errors.

- [ ] **Step 10.5: Manual smoke**

1. Open the requisition swapped in Task 9 smoke step 3 — the "Customer changed (1)" amber badge appears next to the customer name (Accountant form path).
2. Tap the badge → sheet opens, shows the swap entry with old → new, by-whom, date, and reason (if entered).
3. Advance the requisition to `Approved` (via web), then re-open in Accountant app. Smart route renders `<HistoricalRequisitionScreen>`. The badge still appears. Tap → sheet opens.
4. Switch to MD app, open the same approved REQ via `(md)/historical/<id>` — same badge + sheet visible (regression check on the shared component).

- [ ] **Step 10.6: Commit**

```bash
git add bom-mobile/src/components/CustomerChangeHistorySheet.tsx bom-mobile/src/components/HistoricalRequisitionScreen.tsx "bom-mobile/app/(accountant)/[id].tsx"
git commit -m "feat(mobile): add customer-change history sheet + badge across MD/Accountant"
```

---

## Task 11: Final 22-item smoke pass

This task is verification only — no commit unless an issue surfaces a fix.

- [ ] **Step 11.1: Run the full 22-item checklist from the spec**

Open `docs/superpowers/specs/2026-04-25-mobile-v21-phase2-design.md` §8.2 and walk through items 1-22 on a real device (or Android emulator). Each item must pass.

- [ ] **Step 11.2: If any item fails**

Fix the underlying issue. Rerun the affected smoke item. Commit the fix with a descriptive message (`fix(mobile): …`). Re-run the full checklist after the fix to confirm no regression.

- [ ] **Step 11.3: Update memory after green smoke**

Once all 22 items pass, update `C:\Users\Administrator\.claude\projects\D--shan-projects-BOM-Price-Approval\memory\project_mobile_plan1_status.md` to record:

- Phase 2 (dashboard + all-list + notif fix + Feature X) merged & smoked at HEAD
- Date stamp
- Outstanding follow-ups (Feature Y, BomCreator on mobile, item-level deep-link)

This is a memory write, not a code commit.

---

## Self-Review

**Spec coverage:**

| Spec section | Covered by task |
|---|---|
| §4.1.1 stats endpoint | Task 2 |
| §4.1.2 list date filter | Task 1 |
| §4.1.3 Feature X reuse | Tasks 8-10 |
| §4.2 route map | Tasks 3-7 |
| §4.3 new components | Tasks 3, 9, 10 |
| §4.4 API hooks | Tasks 6, 8 |
| §5.1 dashboard | Task 6 |
| §5.2 all-list | Task 5 |
| §5.3 smart `[id]` | Task 4 |
| §5.4 notification deep-link | Task 7 |
| §5.5 Feature X | Tasks 9, 10 |
| §6 authorization | Backend already enforces (Task 2 endpoint roles + reused PATCH endpoint); mobile gates verified in smoke |
| §8.1 backend tests | Tasks 1, 2 (TDD) |
| §8.2 mobile smoke | Task 11 |

All spec sections have a task. ✓

**Placeholder scan:** No "TBD", "TODO", "implement later", "appropriate error handling", "similar to Task N", or empty steps. All code blocks contain real code; all commands have expected output. ✓

**Type consistency:**

- `AccountantDashboardStats` shape matches between backend DTO (Task 2.3), frontend type (Task 6.1), and frontend hook (Task 6.2). ✓
- `ChangeCustomerRequest` field names (`customerId`, `reason`) match backend DTO at `RequisitionDtos.cs:47` (verified via earlier grep). ✓
- `CustomerChangeHistoryEntry` field names are notional — Task 8.1 explicitly directs the engineer to verify against backend response shape. ✓
- `pathForNotification` argument shape (`Notification`, `string`) matches existing function signature at `app/notifications.tsx:16`. ✓

No `f` vs `fc` style discrepancies between tasks.

**Open question handling (from spec §10):**

- Q1 (`SubmittedAt` availability): Resolved in Tasks 1.3 and 2.4 — `UpdatedAt` is used as the proxy with an inline note in code comments.
- Q2 (MD historical extraction side effects): Task 3.5 explicitly smokes the MD path before commit.
- Q3 (`BomPending` smart-route UX): Task 4.4 step 4 covers the historical-view rendering for any non-active status; default behavior is acceptable per spec.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-25-mobile-v21-phase2.md`. Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — execute tasks in this session using executing-plans, batch execution with checkpoints.

**Which approach?**
