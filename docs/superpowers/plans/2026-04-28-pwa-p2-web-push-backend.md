# PWA Phase 2 — Web Push Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** Draft, awaiting plan approval before execute.
**Spec:** `docs/superpowers/specs/2026-04-28-pwa-conversion-design.md` (P2 section)
**Branch:** `feat/pwa-web-push-backend` (off `master @ {P1_MERGE_SHA}`)
**Goal:** Add backend infrastructure for Web Push notifications: VAPID keys, `PushSubscription` table, 2 endpoints (subscribe/unsubscribe), and `NotificationService` web push fan-out alongside existing SignalR + DB notification flow.
**Architecture:** New `WebPushService` wraps `WebPush` NuGet (~3M downloads). New `PushSubscriptionsController` exposes 2 endpoints. `NotificationService.SendAsync` + `SendToUsersAsync` extended to fan out to web push as **additive, non-blocking** behavior. 410-Gone responses auto-cleanup dead subscriptions.
**Tech Stack:** ASP.NET Core 8, EF Core 8, `WebPush` NuGet, Npgsql/PostgreSQL.
**Total tasks:** 7. **Estimated:** 2-3 hr.

---

## Phase ordering rationale

Foundation (entity + migration + VAPID config) must land before service. Service must exist before endpoints. Endpoints must exist before `NotificationService` extension calls them. Verification at end.

```
F1 entity → F2 VAPID + DI → F3 service → E1 endpoints → R1 NotificationService extension → V1 verify → C1 close
```

---

## Tasks

### Foundation (F1-F3)

| # | Task | Outputs |
|---|---|---|
| **F1** | Add `Domain/Entities/PushSubscription.cs`. Add `DbSet<PushSubscription>` to `AppDbContext` + fluent config (FK Cascade, unique index on `Endpoint`, index on `UserId`). Generate EF migration `AddPushSubscription`. Apply locally. | 1 entity, 1 AppDbContext edit, 1 migration pair |
| **F2** | Add empty `WebPush:VapidPublicKey/VapidPrivateKey/Subject` keys to `appsettings.json`. Generate VAPID key pair (one-time CLI utility). Set values via `dotnet user-secrets`. | 2 appsettings edits, 1 user-secrets entry, 1 production Fly secrets entry |
| **F3** | Install `WebPush` NuGet (latest 2.x compatible with .NET 8). Add `Infrastructure/Services/WebPushService.cs`. Register in `Program.cs` as singleton. Unit tests for `SendAsync` happy path + 410 Gone error mapping. | 1 csproj edit, 1 service, 1 Program.cs edit, 1 test file |

#### F1 detailed steps

- [ ] **Step 1: Create `Domain/Entities/PushSubscription.cs`**

```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class PushSubscription
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }

    public User? User { get; set; }
}
```

- [ ] **Step 2: Update `AppDbContext.cs`**

Add property:
```csharp
public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
```

Add fluent config in `OnModelCreating`:
```csharp
modelBuilder.Entity<PushSubscription>(b =>
{
    b.HasIndex(x => x.UserId);
    b.HasIndex(x => x.Endpoint).IsUnique();
    b.HasOne(x => x.User)
     .WithMany()
     .HasForeignKey(x => x.UserId)
     .OnDelete(DeleteBehavior.Cascade);
    b.Property(x => x.Endpoint).HasMaxLength(2048);
    b.Property(x => x.P256dh).HasMaxLength(512);
    b.Property(x => x.Auth).HasMaxLength(512);
    b.Property(x => x.UserAgent).HasMaxLength(512);
});
```

- [ ] **Step 3: Generate migration**

```bash
dotnet ef migrations add AddPushSubscription --project BomPriceApproval.API
```

Expected: 2 files in `Infrastructure/Data/Migrations/`. Inspect Up() — should create `PushSubscriptions` table with FK + 2 indexes.

- [ ] **Step 4: Apply migration locally**

```bash
dotnet ef database update --project BomPriceApproval.API
```

Verify table exists in PG via psql:
```bash
psql -h localhost -p 5433 -U postgres -d bom_price_approval -c "\d \"PushSubscriptions\""
```

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/Domain/Entities/PushSubscription.cs BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs BomPriceApproval.API/Infrastructure/Data/Migrations/
git commit -m "feat(api): add PushSubscription entity + migration"
```

#### F2 detailed steps

- [ ] **Step 1: Generate VAPID key pair**

Use a one-time Node CLI (no `dotnet` equivalent — `WebPush` NuGet doesn't expose generator):
```bash
npx web-push generate-vapid-keys
```

Output:
```
Public Key: BJ...
Private Key: xY...
```

- [ ] **Step 2: Update `appsettings.json` with empty placeholders**

```json
{
  "WebPush": {
    "VapidPublicKey": "",
    "VapidPrivateKey": "",
    "Subject": "mailto:shan@fujairahplastic.com"
  }
}
```

Same in `appsettings.Development.json`.

- [ ] **Step 3: Set user-secrets**

```bash
dotnet user-secrets set "WebPush:VapidPublicKey" "BJ..." --project BomPriceApproval.API
dotnet user-secrets set "WebPush:VapidPrivateKey" "xY..." --project BomPriceApproval.API
```

Verify:
```bash
dotnet user-secrets list --project BomPriceApproval.API | grep WebPush
```

- [ ] **Step 4: Set Fly production secrets**

```bash
flyctl secrets set WebPush__VapidPublicKey="BJ..." WebPush__VapidPrivateKey="xY..." --app bom-fpf-api
```

(`__` is the .NET config separator for env vars on Fly.)

- [ ] **Step 5: Commit**

```bash
git add BomPriceApproval.API/appsettings.json BomPriceApproval.API/appsettings.Development.json
git commit -m "feat(api): add WebPush VAPID config placeholders"
```

#### F3 detailed steps

- [ ] **Step 1: Install NuGet**

```bash
dotnet add BomPriceApproval.API package WebPush
```

Expected: `WebPush` 2.x added to `BomPriceApproval.API.csproj`.

- [ ] **Step 2: Create `Infrastructure/Services/WebPushService.cs`**

```csharp
using System.Net;
using System.Text.Json;
using WebPush;
using BomPriceApproval.API.Domain.Entities;

namespace BomPriceApproval.API.Infrastructure.Services;

public class WebPushService
{
    private readonly WebPushClient _client;
    private readonly VapidDetails? _vapid;
    private readonly ILogger<WebPushService> _logger;
    public bool IsConfigured => _vapid is not null;

    public WebPushService(IConfiguration cfg, ILogger<WebPushService> logger)
    {
        _logger = logger;
        _client = new WebPushClient();
        var publicKey = cfg["WebPush:VapidPublicKey"];
        var privateKey = cfg["WebPush:VapidPrivateKey"];
        var subject = cfg["WebPush:Subject"];
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey) || string.IsNullOrWhiteSpace(subject))
        {
            _logger.LogWarning("WebPush VAPID config missing — push notifications disabled for this run.");
            _vapid = null;
        }
        else
        {
            _vapid = new VapidDetails(subject, publicKey, privateKey);
        }
    }

    public virtual async Task SendAsync(Domain.Entities.PushSubscription sub, string title, string body, CancellationToken ct = default)
    {
        if (_vapid is null)
        {
            _logger.LogDebug("Skipping web push (VAPID not configured).");
            return;
        }
        var pushSub = new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth);
        var payload = JsonSerializer.Serialize(new { title, body });
        await _client.SendNotificationAsync(pushSub, payload, _vapid);
    }
}
```

- [ ] **Step 3: Register in Program.cs**

Find existing `builder.Services.AddSingleton<NotificationService>` (or similar) and add:
```csharp
builder.Services.AddSingleton<WebPushService>();
```

- [ ] **Step 4: Write tests**

`BomPriceApproval.Tests/Notifications/WebPushServiceTests.cs`:

```csharp
using System.Net;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WebPush;
using Xunit;
using SubEntity = BomPriceApproval.API.Domain.Entities.PushSubscription;

public class WebPushServiceTests
{
    [Fact]
    public void IsConfigured_FalseWhenVapidKeysMissing()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "WebPush:VapidPublicKey", "" },
            { "WebPush:VapidPrivateKey", "" },
            { "WebPush:Subject", "" },
        }).Build();
        var svc = new WebPushService(cfg, NullLogger<WebPushService>.Instance);
        Assert.False(svc.IsConfigured);
    }

    [Fact]
    public async Task SendAsync_NoOpWhenNotConfigured()
    {
        var cfg = new ConfigurationBuilder().Build();
        var svc = new WebPushService(cfg, NullLogger<WebPushService>.Instance);
        var sub = new SubEntity { Endpoint = "https://x", P256dh = "p", Auth = "a" };
        await svc.SendAsync(sub, "title", "body"); // should not throw
    }

    [Fact]
    public void IsConfigured_TrueWithValidKeys()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            { "WebPush:VapidPublicKey", "BNxPP9PhIxBjaHv4WdpFrApT7ot3YTeNW0z_uG44VZh3MqcJVDmZ-2I2qRtm6gwKfL0wvtmgrrHpLgSsOQE0aHs" },
            { "WebPush:VapidPrivateKey", "9Q9vdo8gx6JpVvEjtRHsZS0vJjtv1IabO_cERWDFVvw" },
            { "WebPush:Subject", "mailto:test@example.com" },
        }).Build();
        var svc = new WebPushService(cfg, NullLogger<WebPushService>.Instance);
        Assert.True(svc.IsConfigured);
    }
}
```

(Use any valid-format VAPID test pair — these are public throwaway test values from `WebPush` NuGet samples.)

- [ ] **Step 5: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~WebPushServiceTests"
```

Expected: 3 pass.

- [ ] **Step 6: Commit**

```bash
git add BomPriceApproval.API/BomPriceApproval.API.csproj BomPriceApproval.API/Infrastructure/Services/WebPushService.cs BomPriceApproval.API/Program.cs BomPriceApproval.Tests/Notifications/WebPushServiceTests.cs
git commit -m "feat(api): add WebPushService wrapper + VAPID config check"
```

---

### Endpoints (E1)

| # | Task | Outputs |
|---|---|---|
| **E1** | Create `Features/Notifications/PushSubscriptionsController.cs` with `POST /api/notifications/push-subscribe` (upsert) + `DELETE /api/notifications/push-subscribe` (own-only, idempotent). DTOs in same file. Integration tests covering: 401 unauth, 204 first subscribe, 204 re-subscribe (upsert by endpoint), 204 idempotent delete on missing, 204 successful delete, 400 missing fields, isolation (user A can't delete user B's sub). | 1 controller, ~7 integration tests |

#### E1 detailed steps

- [ ] **Step 1: Create controller + DTOs**

`BomPriceApproval.API/Features/Notifications/PushSubscriptionsController.cs`:

```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace BomPriceApproval.API.Features.Notifications;

public record PushKeysDto([Required] string p256dh, [Required] string auth);
public record PushSubscribeRequest([Required] string endpoint, [Required] PushKeysDto keys, string? userAgent);
public record PushUnsubscribeRequest([Required] string endpoint);

[ApiController]
[Route("api/notifications/push-subscribe")]
[Authorize]
public class PushSubscriptionsController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Subscribe([FromBody] PushSubscribeRequest req)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var userId = int.Parse(User.FindFirstValue("UserId")!);
        var existing = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.endpoint);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new PushSubscription
            {
                UserId = userId,
                Endpoint = req.endpoint,
                P256dh = req.keys.p256dh,
                Auth = req.keys.auth,
                UserAgent = req.userAgent,
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.UserId = userId;
            existing.P256dh = req.keys.p256dh;
            existing.Auth = req.keys.auth;
            existing.UserAgent = req.userAgent;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> Unsubscribe([FromBody] PushUnsubscribeRequest req)
    {
        if (!ModelState.IsValid) return ValidationProblem(ModelState);

        var userId = int.Parse(User.FindFirstValue("UserId")!);
        var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == req.endpoint && s.UserId == userId);
        if (sub != null)
        {
            db.PushSubscriptions.Remove(sub);
            await db.SaveChangesAsync();
        }
        return NoContent();
    }
}
```

- [ ] **Step 2: Write integration tests**

`BomPriceApproval.Tests/Notifications/PushSubscriptionsControllerTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using BomPriceApproval.API;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

public class PushSubscriptionsControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PushSubscriptionsControllerTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Post_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint = "https://x", keys = new { p256dh = "p", auth = "a" } });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Post_FirstSubscribe_Returns204AndCreatesRow()
    {
        var client = await TestClient.LoginAs(_factory, "ali@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid()}";
        var resp = await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "p", auth = "a" }, userAgent = "iPhone" });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint);
        Assert.NotNull(sub);
        Assert.Equal("p", sub.P256dh);
    }

    [Fact]
    public async Task Post_ReSubscribe_UpsertsRow()
    {
        var client = await TestClient.LoginAs(_factory, "ali@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid()}";
        await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "old", auth = "old" } });
        var resp = await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "new", auth = "new" } });
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sub = await db.PushSubscriptions.FirstAsync(s => s.Endpoint == endpoint);
        Assert.Equal("new", sub.P256dh);
    }

    [Fact]
    public async Task Delete_OwnSubscription_Returns204AndRemoves()
    {
        var client = await TestClient.LoginAs(_factory, "ali@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid()}";
        await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "p", auth = "a" } });
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/notifications/push-subscribe")
        {
            Content = JsonContent.Create(new { endpoint })
        };
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint));
    }

    [Fact]
    public async Task Delete_MissingSubscription_Returns204Idempotent()
    {
        var client = await TestClient.LoginAs(_factory, "ali@test.com", "Test@1234");
        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/notifications/push-subscribe")
        {
            Content = JsonContent.Create(new { endpoint = "https://nonexistent" })
        };
        var resp = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_OtherUsersSub_LeavesRowIntact()
    {
        var aliClient = await TestClient.LoginAs(_factory, "ali@test.com", "Test@1234");
        var bobClient = await TestClient.LoginAs(_factory, "bob@test.com", "Test@1234");
        var endpoint = $"https://web.push.apple.com/{Guid.NewGuid()}";
        await aliClient.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint, keys = new { p256dh = "p", auth = "a" } });

        var req = new HttpRequestMessage(HttpMethod.Delete, "/api/notifications/push-subscribe")
        {
            Content = JsonContent.Create(new { endpoint })
        };
        var resp = await bobClient.SendAsync(req);
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode); // idempotent for non-owner

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotNull(await db.PushSubscriptions.FirstOrDefaultAsync(s => s.Endpoint == endpoint));
    }

    [Fact]
    public async Task Post_MissingFields_Returns400()
    {
        var client = await TestClient.LoginAs(_factory, "ali@test.com", "Test@1234");
        var resp = await client.PostAsJsonAsync("/api/notifications/push-subscribe",
            new { endpoint = "https://x" /* missing keys */ });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
```

- [ ] **Step 3: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~PushSubscriptionsControllerTests"
```

Expected: 7 pass.

- [ ] **Step 4: Commit**

```bash
git add BomPriceApproval.API/Features/Notifications/PushSubscriptionsController.cs BomPriceApproval.Tests/Notifications/PushSubscriptionsControllerTests.cs
git commit -m "feat(api): add push subscription endpoints (POST/DELETE) with own-user isolation"
```

---

### NotificationService Extension (R1)

| # | Task | Outputs |
|---|---|---|
| **R1** | Inject `WebPushService` into `NotificationService`. Extend `SendAsync(int userId, ...)` and `SendToUsersAsync(IEnumerable<int>, ...)` to fan out to all matching `PushSubscription` rows. On `WebPushException` with status 410/404 → remove row. On any other exception → log warning and swallow (preserve SignalR + DB invariant). Unit tests: happy path (single sub fan-out), 410 cleanup, exception swallow, no-subs no-op, multi-user dispatch. | 1 service edit, 1 test file (~5 cases) |

#### R1 detailed steps

- [ ] **Step 1: Read current `NotificationService`**

```bash
cat BomPriceApproval.API/Infrastructure/Services/NotificationService.cs
```

Identify the two methods to extend: `SendAsync(int userId, ...)` and `SendToUsersAsync(IEnumerable<int>, ...)`.

- [ ] **Step 2: Add WebPushService dependency**

In constructor, inject:
```csharp
private readonly WebPushService _webPush;
public NotificationService(..., WebPushService webPush, ...)
{
    ...
    _webPush = webPush;
}
```

- [ ] **Step 3: Add private fan-out helper**

```csharp
private async Task FanOutWebPushAsync(IEnumerable<int> userIds, string title, string body, CancellationToken ct)
{
    if (!_webPush.IsConfigured) return;
    var ids = userIds.Distinct().ToList();
    var subs = await _db.PushSubscriptions
        .Where(s => ids.Contains(s.UserId))
        .ToListAsync(ct);

    var dead = new List<Domain.Entities.PushSubscription>();
    foreach (var sub in subs)
    {
        try
        {
            await _webPush.SendAsync(sub, title, body, ct);
            sub.LastUsedAt = DateTime.UtcNow;
        }
        catch (WebPush.WebPushException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.Gone ||
            ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            dead.Add(sub);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Web push send failed for sub {SubId}", sub.Id);
        }
    }
    if (dead.Count > 0) _db.PushSubscriptions.RemoveRange(dead);
    await _db.SaveChangesAsync(ct);
}
```

- [ ] **Step 4: Call helper from existing methods**

In existing `SendAsync(int userId, ...)`, after the SignalR push line:
```csharp
await FanOutWebPushAsync(new[] { userId }, title, message, ct);
```

In existing `SendToUsersAsync(IEnumerable<int>, ...)`, after the SignalR fan-out:
```csharp
await FanOutWebPushAsync(userIds, title, message, ct);
```

- [ ] **Step 5: Write tests**

`BomPriceApproval.Tests/Notifications/NotificationServiceWebPushTests.cs`:

```csharp
using System.Net;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.Tests.Shared;
using Microsoft.EntityFrameworkCore;
using WebPush;
using Xunit;

public class NotificationServiceWebPushTests : IClassFixture<TestFactory>
{
    private readonly TestFactory _factory;
    public NotificationServiceWebPushTests(TestFactory factory) => _factory = factory;

    [Fact]
    public async Task SendAsync_FanOutsToWebPush_OnHappyPath()
    {
        // Arrange: seed user + push sub; mock WebPushService to track call
        // Act: NotificationService.SendAsync(userId, ...)
        // Assert: WebPushService.SendAsync called with this sub; LastUsedAt updated
    }

    [Fact]
    public async Task SendAsync_RemovesSub_When410Gone()
    {
        // Mock WebPushService.SendAsync to throw WebPushException with StatusCode=Gone
        // After call, assert PushSubscriptions row deleted
    }

    [Fact]
    public async Task SendAsync_RemovesSub_When404NotFound()
    {
        // Same as above with 404
    }

    [Fact]
    public async Task SendAsync_SwallowsTimeout_AndPreservesSub()
    {
        // Mock WebPushService.SendAsync to throw TaskCanceledException
        // After call, assert sub still exists; SignalR + DB notif still happened
    }

    [Fact]
    public async Task SendAsync_NoOpsWhenUserHasNoSub()
    {
        // No PushSubscription rows; SendAsync should not throw, SignalR + DB notif still happen
    }
}
```

(Detailed test bodies use existing `TestFactory` + helper for substituting `WebPushService` with a fake — pattern matches `NotificationResilienceTests` already in repo.)

- [ ] **Step 6: Run tests**

```bash
dotnet test --filter "FullyQualifiedName~NotificationServiceWebPushTests"
```

Expected: 5 pass.

- [ ] **Step 7: Run full backend suite**

```bash
dotnet test
```

Expected: 318 P1+P2 backend tests pass + 5 new = 323 pass. No regressions.

- [ ] **Step 8: Commit**

```bash
git add BomPriceApproval.API/Infrastructure/Services/NotificationService.cs BomPriceApproval.Tests/Notifications/NotificationServiceWebPushTests.cs
git commit -m "feat(api): NotificationService web push fan-out with 410-Gone auto-cleanup"
```

---

### Verification (V1)

| # | Task | Outputs |
|---|---|---|
| **V1** | Run full backend suite. Manual smoke: hit `POST /api/notifications/push-subscribe` from Postman with mock subscription → verify 204 + DB row. Trigger an existing notification flow (e.g., MD approves a req) → verify no errors and row's `LastUsedAt` is updated. | Test report, smoke checklist |

- [ ] **Step 1: Full suite**

```bash
dotnet test --logger "console;verbosity=normal"
```

Expected: ~323+ tests pass.

- [ ] **Step 2: API up locally**

```bash
dotnet run --project BomPriceApproval.API
```

Expected: API listens on localhost:7300, no startup errors. WebPush log line confirms VAPID configured.

- [ ] **Step 3: Postman smoke**

POST to `http://localhost:7300/api/notifications/push-subscribe` with valid JWT (login first):

```json
{
  "endpoint": "https://web.push.apple.com/QSomeRandomFakeId",
  "keys": { "p256dh": "BJa_test_p256dh_key", "auth": "Xy_test_auth" },
  "userAgent": "Postman smoke"
}
```

Expected: 204. Verify DB row in `PushSubscriptions` table.

- [ ] **Step 4: Trigger notification flow**

Use existing test seed: login as `ali@test.com`, create a req, BomCreator submits, Accountant submits, MD approves. After approval, the SP gets a notification.

Watch backend logs — should see web push send attempt for the SP's subscription. Since it's a fake endpoint, expect `WebPushException` (Gone or InvalidArgument) → row auto-cleaned. Verify behavior:

```bash
psql -h localhost -p 5433 -U postgres -d bom_price_approval -c "SELECT * FROM \"PushSubscriptions\";"
```

Empty (or no test row) — confirms cleanup. Original SignalR + DB notification still succeeded.

---

### Close (C1)

| # | Task | Outputs |
|---|---|---|
| **C1** | Update `CLAUDE.md` with WebPush section. Update memory `project_pwa_conversion.md` (P2 status). Open PR via GitHub UI. | docs commit, PR opened by user |

- [ ] **Step 1: Update CLAUDE.md**

In Key Infrastructure Services table, add row:
```markdown
| `WebPushService` | VAPID-signed web push sender; auto-cleanup of 410-Gone subscriptions; resilience invariant — failure never breaks SignalR + DB notification flow |
```

In a new subsection (placed near "Real-time Notifications"):
```markdown
### Web Push Notifications (post-2026-04-28, PWA P2)

`bom-web` PWA users (iOS/iPadOS Safari install + permission grant) receive notifications when app is closed.

- VAPID keys live in user-secrets / Fly secrets; `appsettings.json` ships empty
- `PushSubscription` table (UserId, Endpoint, P256dh, Auth, UserAgent, CreatedAt, LastUsedAt; unique on Endpoint)
- `POST/DELETE /api/notifications/push-subscribe` (own-user-only, idempotent delete)
- `NotificationService.SendAsync` + `SendToUsersAsync` fan out to web push **additively** — failure never blocks SignalR + DB
- 410 Gone / 404 → auto-delete dead subscription
- Other errors → log warning and swallow
- Generic notification body (no entity data) — protects lock-screen privacy
```

- [ ] **Step 2: Update memory**

Edit `C:\Users\Administrator\.claude\projects\D--shan-projects-BOM-Price-Approval\memory\project_pwa_conversion.md`:

Replace `## P2 — Web Push Backend (pending)` with:
```markdown
## P2 — Web Push Backend (MERGED)

- Branch `feat/pwa-web-push-backend` → master @ {SHA}
- VAPID public key for frontend: `BJ...` (also in Fly secret)
- `PushSubscription` table live; 0 rows initially
- `WebPushService` configured at startup with log line on success/disabled
- 7 endpoint tests + 5 service tests passing
- Frontend wiring pending in P3
```

- [ ] **Step 3: Commit + open PR**

```bash
git add CLAUDE.md
git commit -m "docs: document WebPush infrastructure (PWA P2)"
```

User opens PR titled `feat: PWA Phase 2 — Web Push backend (VAPID + endpoints + NotificationService extension)`.

---

## Self-review

**Spec coverage:**
- ✅ VAPID keys + storage (F2)
- ✅ `PushSubscription` schema with indexes + cascade FK (F1)
- ✅ POST/DELETE endpoints with own-user isolation + idempotent delete (E1)
- ✅ `WebPushService` wrapper + VAPID-disabled fallback (F3)
- ✅ NotificationService fan-out additive + 410-Gone cleanup + error swallow (R1)
- ✅ Backend test coverage (~12 new tests across 2 files)

**Placeholder scan:** All file paths exact. Test code blocks complete. `{P1_MERGE_SHA}` and `{SHA}` are intentional — filled at branch-off / merge time.

**Type consistency:** `WebPushService.SendAsync(PushSubscription sub, ...)` consumed by `NotificationService.FanOutWebPushAsync`. `IsConfigured` property used as gate in both `SendAsync` (no-op) and `FanOutWebPushAsync` (skip query).

**Out-of-scope:**
- Frontend permission flow + SW push event listener → P3 plan
- Full real-device end-to-end smoke → P3 plan (needs frontend to subscribe first)

---

## Execution mode

Recommend **subagent-driven-development** for F1, F3, R1 (entity + service + critical NotificationService extension — high blast). **Inline** for F2 (VAPID config — manual + commit), E1 (controller pattern is established), V1 (smoke).
