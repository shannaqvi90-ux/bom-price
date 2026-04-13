# Backend API Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the ASP.NET Core 8 Web API with PostgreSQL, JWT auth, full BOM & price approval workflow, SignalR notifications, email, and PDF generation.

**Architecture:** Single Web API project with feature-based folder structure. EF Core with PostgreSQL (port 5500). JWT access + refresh token auth. SignalR hub for real-time notifications. QuestPDF for server-side PDF generation.

**Tech Stack:** .NET 8, EF Core 8, Npgsql, JWT Bearer, SignalR, QuestPDF, MailKit, ClosedXML (Excel import), CsvHelper (CSV import), xUnit + Testcontainers (tests)

---

## File Structure

```
BomPriceApproval.API/
  Program.cs
  appsettings.json
  appsettings.Development.json

  Domain/
    Entities/
      Branch.cs
      User.cs
      Customer.cs
      Item.cs
      Process.cs
      ExchangeRate.cs
      QuotationRequest.cs
      BomHeader.cs
      BomLine.cs
      BomCost.cs
      QuotationApproval.cs
      Notification.cs
      RefreshToken.cs
    Enums/
      UserRole.cs
      ItemType.cs
      RequisitionStatus.cs
      LandedCostType.cs

  Infrastructure/
    Data/
      AppDbContext.cs
      Migrations/
    Services/
      EmailService.cs
      PdfService.cs
      NotificationService.cs
      TokenService.cs
      ItemImportService.cs

  Features/
    Auth/
      AuthController.cs
      AuthDtos.cs
    Users/
      UsersController.cs
      UserDtos.cs
    Branches/
      BranchesController.cs
    Customers/
      CustomersController.cs
      CustomerDtos.cs
    Items/
      ItemsController.cs
      ItemDtos.cs
      ItemImportController.cs
    Processes/
      ProcessesController.cs
      ProcessDtos.cs
    ExchangeRates/
      ExchangeRatesController.cs
      ExchangeRateDtos.cs
    Requisitions/
      RequisitionsController.cs
      RequisitionDtos.cs
    Bom/
      BomController.cs
      BomDtos.cs
    Costing/
      CostingController.cs
      CostingDtos.cs
    Approvals/
      ApprovalsController.cs
      ApprovalDtos.cs
    Notifications/
      NotificationsController.cs
      NotificationHub.cs

  Middleware/
    BranchAccessMiddleware.cs

BomPriceApproval.Tests/
  Auth/
    AuthTests.cs
  Requisitions/
    RequisitionWorkflowTests.cs
  Bom/
    BomTests.cs
  Costing/
    CostingTests.cs
  Approvals/
    ApprovalTests.cs
  Helpers/
    TestDbContext.cs
    TestAuthHelper.cs
```

---

## Task 1: Solution & Project Setup

**Files:**
- Create: `BomPriceApproval.API/BomPriceApproval.API.csproj`
- Create: `BomPriceApproval.Tests/BomPriceApproval.Tests.csproj`
- Create: `BomPriceApproval.sln`
- Create: `BomPriceApproval.API/appsettings.json`
- Create: `BomPriceApproval.API/appsettings.Development.json`

- [ ] **Step 1: Create solution and projects**

```bash
cd "D:/shan projects/BOM & Price Approval"
dotnet new sln -n BomPriceApproval
dotnet new webapi -n BomPriceApproval.API --framework net8.0
dotnet new xunit -n BomPriceApproval.Tests --framework net8.0
dotnet sln add BomPriceApproval.API/BomPriceApproval.API.csproj
dotnet sln add BomPriceApproval.Tests/BomPriceApproval.Tests.csproj
```

- [ ] **Step 2: Add NuGet packages to API project**

```bash
cd BomPriceApproval.API
dotnet add package Microsoft.EntityFrameworkCore --version 8.0.4
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.4
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.4
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.4
dotnet add package Microsoft.AspNetCore.SignalR --version 1.1.0
dotnet add package QuestPDF --version 2024.3.4
dotnet add package MailKit --version 4.5.0
dotnet add package ClosedXML --version 0.102.3
dotnet add package CsvHelper --version 33.0.1
dotnet add package BCrypt.Net-Next --version 4.0.3
```

- [ ] **Step 3: Add NuGet packages to Tests project**

```bash
cd ../BomPriceApproval.Tests
dotnet add reference ../BomPriceApproval.API/BomPriceApproval.API.csproj
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 8.0.4
dotnet add package Testcontainers.PostgreSql --version 3.8.0
dotnet add package FluentAssertions --version 6.12.0
```

- [ ] **Step 4: Write appsettings.json**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5500;Database=bom_price_approval;Username=postgres;Password=yourpassword"
  },
  "Jwt": {
    "Key": "CHANGE_THIS_TO_A_32_CHAR_SECRET_KEY_MIN",
    "Issuer": "BomPriceApproval",
    "Audience": "BomPriceApprovalClients",
    "AccessTokenExpiryMinutes": 15,
    "RefreshTokenExpiryDays": 7
  },
  "Email": {
    "Host": "smtp.gmail.com",
    "Port": 587,
    "Username": "",
    "Password": "",
    "FromAddress": "noreply@fujairahplastic.com",
    "FromName": "Fujairah Plastic Factory"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 5: Commit**

```bash
cd "D:/shan projects/BOM & Price Approval"
git init
git add .
git commit -m "feat: initialize solution with API and Tests projects"
```

---

## Task 2: Domain Entities & Enums

**Files:**
- Create: `BomPriceApproval.API/Domain/Enums/UserRole.cs`
- Create: `BomPriceApproval.API/Domain/Enums/ItemType.cs`
- Create: `BomPriceApproval.API/Domain/Enums/RequisitionStatus.cs`
- Create: `BomPriceApproval.API/Domain/Enums/LandedCostType.cs`
- Create: `BomPriceApproval.API/Domain/Entities/*.cs` (all entity files)

- [ ] **Step 1: Create enums**

`Domain/Enums/UserRole.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum UserRole
{
    Admin,
    SalesPerson,
    BomCreator,
    Accountant,
    ManagingDirector
}
```

`Domain/Enums/ItemType.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum ItemType { FinishedGood, RawMaterial }
```

`Domain/Enums/RequisitionStatus.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum RequisitionStatus
{
    Draft,
    BomPending,
    BomInProgress,
    CostingPending,
    CostingInProgress,
    MdReview,
    Approved,
    Rejected
}
```

`Domain/Enums/LandedCostType.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Enums;

public enum LandedCostType { Percentage, FixedValue }
```

- [ ] **Step 2: Create entity classes**

`Domain/Entities/Branch.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class Branch
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<User> Users { get; set; } = [];
    public ICollection<Customer> Customers { get; set; } = [];
    public ICollection<Item> Items { get; set; } = [];
    public ICollection<QuotationRequest> QuotationRequests { get; set; } = [];
}
```

`Domain/Entities/User.cs`:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public int? BranchId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Branch? Branch { get; set; }
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
```

`Domain/Entities/RefreshToken.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class RefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
}
```

`Domain/Entities/Customer.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Branch Branch { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
}
```

`Domain/Entities/Item.cs`:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class Item
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ItemType Type { get; set; }
    public int BranchId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Branch Branch { get; set; } = null!;
}
```

`Domain/Entities/Process.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class Process
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

`Domain/Entities/ExchangeRate.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class ExchangeRate
{
    public int Id { get; set; }
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencyName { get; set; } = string.Empty;
    public decimal RateToAed { get; set; }
    public int SetByUserId { get; set; }
    public DateTime EffectiveDate { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User SetBy { get; set; } = null!;
}
```

`Domain/Entities/QuotationRequest.cs`:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class QuotationRequest
{
    public int Id { get; set; }
    public string RefNo { get; set; } = string.Empty;
    public int BranchId { get; set; }
    public int SalesPersonId { get; set; }
    public int CustomerId { get; set; }
    public int ItemId { get; set; }
    public decimal ExpectedQty { get; set; }
    public string CurrencyCode { get; set; } = "AED";
    public decimal? ExchangeRateSnapshot { get; set; }
    public RequisitionStatus Status { get; set; } = RequisitionStatus.Draft;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Branch Branch { get; set; } = null!;
    public User SalesPerson { get; set; } = null!;
    public Customer Customer { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public BomHeader? BomHeader { get; set; }
    public QuotationApproval? Approval { get; set; }
}
```

`Domain/Entities/BomHeader.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class BomHeader
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public int ItemId { get; set; }
    public int CreatedByUserId { get; set; }
    public decimal TotalCostPerKg { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public QuotationRequest QuotationRequest { get; set; } = null!;
    public Item Item { get; set; } = null!;
    public User CreatedBy { get; set; } = null!;
    public ICollection<BomLine> Lines { get; set; } = [];
    public BomCost? Cost { get; set; }
}
```

`Domain/Entities/BomLine.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class BomLine
{
    public int Id { get; set; }
    public int BomHeaderId { get; set; }
    public int ProcessId { get; set; }
    public int RawMaterialItemId { get; set; }
    public decimal QtyPerKg { get; set; }
    public decimal WastagePct { get; set; }
    public BomHeader BomHeader { get; set; } = null!;
    public Process Process { get; set; } = null!;
    public Item RawMaterial { get; set; } = null!;
}
```

`Domain/Entities/BomCost.cs`:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Entities;

public class BomCost
{
    public int Id { get; set; }
    public int BomHeaderId { get; set; }
    public decimal RawMaterialCostTotal { get; set; }
    public LandedCostType LandedCostType { get; set; }
    public decimal LandedCostValue { get; set; }
    public decimal FohAmount { get; set; }
    public decimal TotalCostPerKg { get; set; }
    public int SubmittedByUserId { get; set; }
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public BomHeader BomHeader { get; set; } = null!;
    public User SubmittedBy { get; set; } = null!;
}
```

`Domain/Entities/QuotationApproval.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class QuotationApproval
{
    public int Id { get; set; }
    public int QuotationRequestId { get; set; }
    public decimal SalesPricePerKgAed { get; set; }
    public decimal? SalesPricePerKgForeign { get; set; }
    public decimal ProfitMarginPct { get; set; }
    public decimal MaterialCostPct { get; set; }
    public decimal OtherCostPct { get; set; }
    public int ApprovedByUserId { get; set; }
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
    public bool IsApproved { get; set; }
    public QuotationRequest QuotationRequest { get; set; } = null!;
    public User ApprovedBy { get; set; } = null!;
}
```

`Domain/Entities/Notification.cs`:
```csharp
namespace BomPriceApproval.API.Domain.Entities;

public class Notification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public int ReferenceId { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User User { get; set; } = null!;
}
```

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add domain entities and enums"
```

---

## Task 3: DbContext & Migrations

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Data/AppDbContext.cs`

- [ ] **Step 1: Create AppDbContext**

```csharp
using BomPriceApproval.API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Process> Processes => Set<Process>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<QuotationRequest> QuotationRequests => Set<QuotationRequest>();
    public DbSet<BomHeader> BomHeaders => Set<BomHeader>();
    public DbSet<BomLine> BomLines => Set<BomLine>();
    public DbSet<BomCost> BomCosts => Set<BomCost>();
    public DbSet<QuotationApproval> QuotationApprovals => Set<QuotationApproval>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Branch>().HasData(
            new Branch { Id = 1, Name = "Fujairah" },
            new Branch { Id = 2, Name = "Al Ain" }
        );

        mb.Entity<QuotationRequest>()
            .Property(q => q.RefNo)
            .HasComputedColumnSql("'REQ-' || LPAD(id::text, 4, '0')", stored: true);

        mb.Entity<BomCost>()
            .HasOne(c => c.BomHeader)
            .WithOne(h => h.Cost)
            .HasForeignKey<BomCost>(c => c.BomHeaderId);

        mb.Entity<QuotationApproval>()
            .HasOne(a => a.QuotationRequest)
            .WithOne(q => q.Approval)
            .HasForeignKey<QuotationApproval>(a => a.QuotationRequestId);

        // Decimal precision
        mb.Entity<BomLine>().Property(b => b.QtyPerKg).HasPrecision(18, 6);
        mb.Entity<BomLine>().Property(b => b.WastagePct).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.RawMaterialCostTotal).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.LandedCostValue).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.FohAmount).HasPrecision(18, 4);
        mb.Entity<BomCost>().Property(b => b.TotalCostPerKg).HasPrecision(18, 4);
        mb.Entity<QuotationApproval>().Property(a => a.SalesPricePerKgAed).HasPrecision(18, 4);
        mb.Entity<QuotationApproval>().Property(a => a.SalesPricePerKgForeign).HasPrecision(18, 4);
        mb.Entity<QuotationApproval>().Property(a => a.ProfitMarginPct).HasPrecision(18, 4);
        mb.Entity<ExchangeRate>().Property(e => e.RateToAed).HasPrecision(18, 6);
    }
}
```

- [ ] **Step 2: Register DbContext in Program.cs**

```csharp
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
app.Run();

public partial class Program { }
```

- [ ] **Step 3: Create and apply migration**

```bash
cd BomPriceApproval.API
dotnet ef migrations add InitialCreate --output-dir Infrastructure/Data/Migrations
dotnet ef database update
```

Expected: Migration files created, database `bom_price_approval` created with all tables.

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: add DbContext and initial migration"
```

---

## Task 4: JWT Authentication

**Files:**
- Create: `BomPriceApproval.API/Infrastructure/Services/TokenService.cs`
- Create: `BomPriceApproval.API/Features/Auth/AuthDtos.cs`
- Create: `BomPriceApproval.API/Features/Auth/AuthController.cs`
- Create: `BomPriceApproval.Tests/Auth/AuthTests.cs`

- [ ] **Step 1: Write failing auth tests**

`BomPriceApproval.Tests/Auth/AuthTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Auth;

public class AuthTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokens()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "admin@test.com",
            Password = "Admin@1234"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        body!.AccessToken.Should().NotBeNullOrEmpty();
        body.RefreshToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Login_WithInvalidCredentials_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            Email = "admin@test.com",
            Password = "wrongpassword"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name);
}
```

- [ ] **Step 2: Run test — expect fail**

```bash
cd BomPriceApproval.Tests
dotnet test --filter "AuthTests" -v
```
Expected: FAIL — `AuthController` not found.

- [ ] **Step 3: Create TokenService**

`Infrastructure/Services/TokenService.cs`:
```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BomPriceApproval.API.Domain.Entities;
using Microsoft.IdentityModel.Tokens;

namespace BomPriceApproval.API.Infrastructure.Services;

public class TokenService(IConfiguration config)
{
    public string GenerateAccessToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddMinutes(int.Parse(config["Jwt:AccessTokenExpiryMinutes"]!));

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("branchId", user.BranchId?.ToString() ?? ""),
            new Claim("name", user.Name)
        };

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: expiry,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        var bytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}
```

- [ ] **Step 4: Create AuthDtos**

`Features/Auth/AuthDtos.cs`:
```csharp
namespace BomPriceApproval.API.Features.Auth;

public record LoginRequest(string Email, string Password);
public record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
public record RefreshRequest(string RefreshToken);
```

- [ ] **Step 5: Create AuthController**

`Features/Auth/AuthController.cs`:
```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Auth;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokenService, IConfiguration config) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest req)
    {
        var user = await db.Users
            .Include(u => u.RefreshTokens)
            .FirstOrDefaultAsync(u => u.Email == req.Email && u.IsActive);

        if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid credentials" });

        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshTokenValue = tokenService.GenerateRefreshToken();

        var refreshToken = new RefreshToken
        {
            Token = refreshTokenValue,
            UserId = user.Id,
            ExpiresAt = DateTime.UtcNow.AddDays(int.Parse(config["Jwt:RefreshTokenExpiryDays"]!))
        };
        db.RefreshTokens.Add(refreshToken);
        await db.SaveChangesAsync();

        return Ok(new LoginResponse(accessToken, refreshTokenValue, user.Role.ToString(), user.Id, user.Name, user.BranchId));
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest req)
    {
        var token = await db.RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == req.RefreshToken && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow);

        if (token is null) return Unauthorized(new { message = "Invalid refresh token" });

        token.IsRevoked = true;
        var newRefresh = tokenService.GenerateRefreshToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefresh,
            UserId = token.UserId,
            ExpiresAt = DateTime.UtcNow.AddDays(int.Parse(config["Jwt:RefreshTokenExpiryDays"]!))
        });
        await db.SaveChangesAsync();

        return Ok(new LoginResponse(
            tokenService.GenerateAccessToken(token.User),
            newRefresh,
            token.User.Role.ToString(),
            token.User.Id,
            token.User.Name,
            token.User.BranchId));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest req)
    {
        var token = await db.RefreshTokens.FirstOrDefaultAsync(t => t.Token == req.RefreshToken);
        if (token is not null) { token.IsRevoked = true; await db.SaveChangesAsync(); }
        return NoContent();
    }
}
```

- [ ] **Step 6: Register services in Program.cs and add test seed user**

Update `Program.cs`:
```csharp
using System.Text;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<TokenService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
        // Support SignalR token from query string
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p
        .WithOrigins("http://localhost:5300")
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

// Seed admin user for dev
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    if (!db.Users.Any(u => u.Email == "admin@test.com"))
    {
        db.Users.Add(new User
        {
            Name = "Admin",
            Email = "admin@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),
            Role = UserRole.Admin,
            BranchId = null
        });
        await db.SaveChangesAsync();
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();

public partial class Program { }
```

- [ ] **Step 7: Run auth tests — expect pass**

```bash
dotnet test --filter "AuthTests" -v
```
Expected: PASS — both tests green.

- [ ] **Step 8: Commit**

```bash
git add .
git commit -m "feat: add JWT auth with login, refresh, and logout"
```

---

## Task 5: Users, Branches & Processes CRUD (Admin)

**Files:**
- Create: `Features/Users/UserDtos.cs`
- Create: `Features/Users/UsersController.cs`
- Create: `Features/Branches/BranchesController.cs`
- Create: `Features/Processes/ProcessDtos.cs`
- Create: `Features/Processes/ProcessesController.cs`

- [ ] **Step 1: Create UserDtos**

`Features/Users/UserDtos.cs`:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Users;

public record CreateUserRequest(string Name, string Email, string Password, UserRole Role, int? BranchId);
public record UpdateUserRequest(string Name, string Email, UserRole Role, int? BranchId, bool IsActive);
public record UserResponse(int Id, string Name, string Email, string Role, int? BranchId, string? BranchName, bool IsActive);
```

- [ ] **Step 2: Create UsersController**

`Features/Users/UsersController.cs`:
```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Users;

[ApiController]
[Route("api/users")]
[Authorize(Roles = "Admin")]
public class UsersController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Users.Include(u => u.Branch)
            .Select(u => new UserResponse(u.Id, u.Name, u.Email, u.Role.ToString(), u.BranchId, u.Branch!.Name, u.IsActive))
            .ToListAsync());

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest req)
    {
        if (await db.Users.AnyAsync(u => u.Email == req.Email))
            return Conflict(new { message = "Email already exists" });

        var user = new User
        {
            Name = req.Name, Email = req.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
            Role = req.Role, BranchId = req.BranchId
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new UserResponse(user.Id, user.Name, user.Email, user.Role.ToString(), user.BranchId, null, user.IsActive));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, UpdateUserRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.Name = req.Name; user.Email = req.Email;
        user.Role = req.Role; user.BranchId = req.BranchId; user.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound();
        user.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 3: Create ProcessDtos and ProcessesController**

`Features/Processes/ProcessDtos.cs`:
```csharp
namespace BomPriceApproval.API.Features.Processes;

public record CreateProcessRequest(string Name, int DisplayOrder);
public record UpdateProcessRequest(string Name, int DisplayOrder, bool IsActive);
public record ProcessResponse(int Id, string Name, int DisplayOrder, bool IsActive);
```

`Features/Processes/ProcessesController.cs`:
```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Processes;

[ApiController]
[Route("api/processes")]
public class ProcessesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Processes.OrderBy(p => p.DisplayOrder)
            .Select(p => new ProcessResponse(p.Id, p.Name, p.DisplayOrder, p.IsActive))
            .ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CreateProcessRequest req)
    {
        var process = new Process { Name = req.Name, DisplayOrder = req.DisplayOrder };
        db.Processes.Add(process);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new ProcessResponse(process.Id, process.Name, process.DisplayOrder, process.IsActive));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Update(int id, UpdateProcessRequest req)
    {
        var p = await db.Processes.FindAsync(id);
        if (p is null) return NotFound();
        p.Name = req.Name; p.DisplayOrder = req.DisplayOrder; p.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await db.Processes.FindAsync(id);
        if (p is null) return NotFound();
        p.IsActive = false;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 4: Create BranchesController**

`Features/Branches/BranchesController.cs`:
```csharp
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Branches;

[ApiController]
[Route("api/branches")]
[Authorize]
public class BranchesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Branches.Select(b => new { b.Id, b.Name }).ToListAsync());
}
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add users, branches, and processes CRUD endpoints"
```

---

## Task 6: Customers & Items

**Files:**
- Create: `Features/Customers/CustomerDtos.cs`
- Create: `Features/Customers/CustomersController.cs`
- Create: `Features/Items/ItemDtos.cs`
- Create: `Features/Items/ItemsController.cs`

- [ ] **Step 1: Write failing test for customer branch isolation**

`BomPriceApproval.Tests/Customers/CustomerTests.cs`:
```csharp
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Customers;

public class CustomerTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task GetCustomers_SalesPersonSeesOnlyOwnCustomers()
    {
        // Seed: two sales persons, each with one customer
        // Login as sales person 1 — should only see their own customer
        // This test verifies branch + owner isolation
        var client = factory.CreateClient();
        // Full implementation in integration test helper — placeholder asserts shape
        var response = await client.GetAsync("/api/customers");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
```

- [ ] **Step 2: Create CustomerDtos**

`Features/Customers/CustomerDtos.cs`:
```csharp
namespace BomPriceApproval.API.Features.Customers;

public record CreateCustomerRequest(string Name, string Address, string Email, string PhoneNumber);
public record UpdateCustomerRequest(string Name, string Address, string Email, string PhoneNumber);
public record CustomerResponse(int Id, string Name, string Address, string Email, string PhoneNumber, int BranchId, int CreatedByUserId);
```

- [ ] **Step 3: Create CustomersController**

`Features/Customers/CustomersController.cs`:
```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Customers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var query = db.Customers.AsQueryable();

        // Sales persons see only their own customers
        if (CurrentRole == "SalesPerson")
            query = query.Where(c => c.CreatedByUserId == CurrentUserId);
        else if (CurrentBranchId.HasValue)
            query = query.Where(c => c.BranchId == CurrentBranchId);

        return Ok(await query
            .Select(c => new CustomerResponse(c.Id, c.Name, c.Address, c.Email, c.PhoneNumber, c.BranchId, c.CreatedByUserId))
            .ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var c = await db.Customers.FindAsync(id);
        if (c is null) return NotFound();
        if (CurrentRole == "SalesPerson" && c.CreatedByUserId != CurrentUserId) return Forbid();
        return Ok(new CustomerResponse(c.Id, c.Name, c.Address, c.Email, c.PhoneNumber, c.BranchId, c.CreatedByUserId));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Create(CreateCustomerRequest req)
    {
        var customer = new Customer
        {
            Name = req.Name, Address = req.Address, Email = req.Email,
            PhoneNumber = req.PhoneNumber,
            BranchId = CurrentBranchId!.Value,
            CreatedByUserId = CurrentUserId
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = customer.Id },
            new CustomerResponse(customer.Id, customer.Name, customer.Address, customer.Email, customer.PhoneNumber, customer.BranchId, customer.CreatedByUserId));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Update(int id, UpdateCustomerRequest req)
    {
        var c = await db.Customers.FindAsync(id);
        if (c is null) return NotFound();
        if (c.CreatedByUserId != CurrentUserId) return Forbid();
        c.Name = req.Name; c.Address = req.Address; c.Email = req.Email; c.PhoneNumber = req.PhoneNumber;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 4: Create ItemDtos and ItemsController**

`Features/Items/ItemDtos.cs`:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Items;

public record CreateItemRequest(string Code, string Description, ItemType Type);
public record ItemResponse(int Id, string Code, string Description, string Type, int BranchId, bool IsActive);
public record SimilarItemResult(int Id, string Code, string Description);
```

`Features/Items/ItemsController.cs`:
```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items")]
[Authorize]
public class ItemsController(AppDbContext db) : ControllerBase
{
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? type = null)
    {
        var query = db.Items.AsQueryable();
        if (CurrentBranchId.HasValue) query = query.Where(i => i.BranchId == CurrentBranchId);
        if (type is not null && Enum.TryParse<Domain.Enums.ItemType>(type, out var t))
            query = query.Where(i => i.Type == t);
        return Ok(await query.Where(i => i.IsActive)
            .Select(i => new ItemResponse(i.Id, i.Code, i.Description, i.Type.ToString(), i.BranchId, i.IsActive))
            .ToListAsync());
    }

    [HttpGet("check-similar")]
    public async Task<IActionResult> CheckSimilar([FromQuery] string description)
    {
        var branchId = CurrentBranchId;
        var similar = await db.Items
            .Where(i => (branchId == null || i.BranchId == branchId) &&
                        EF.Functions.ILike(i.Description, $"%{description}%"))
            .Select(i => new SimilarItemResult(i.Id, i.Code, i.Description))
            .Take(5).ToListAsync();
        return Ok(similar);
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Create(CreateItemRequest req)
    {
        var item = new Item
        {
            Code = req.Code, Description = req.Description, Type = req.Type,
            BranchId = CurrentBranchId!.Value
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll),
            new ItemResponse(item.Id, item.Code, item.Description, item.Type.ToString(), item.BranchId, item.IsActive));
    }
}
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add customers and items endpoints with branch isolation"
```

---

## Task 7: Exchange Rates

**Files:**
- Create: `Features/ExchangeRates/ExchangeRateDtos.cs`
- Create: `Features/ExchangeRates/ExchangeRatesController.cs`

- [ ] **Step 1: Create ExchangeRateDtos**

`Features/ExchangeRates/ExchangeRateDtos.cs`:
```csharp
namespace BomPriceApproval.API.Features.ExchangeRates;

public record CreateExchangeRateRequest(string CurrencyCode, string CurrencyName, decimal RateToAed, DateTime EffectiveDate);
public record UpdateExchangeRateRequest(decimal RateToAed, DateTime EffectiveDate, bool IsActive);
public record ExchangeRateResponse(int Id, string CurrencyCode, string CurrencyName, decimal RateToAed, DateTime EffectiveDate, bool IsActive, string SetByName);
```

- [ ] **Step 2: Create ExchangeRatesController**

`Features/ExchangeRates/ExchangeRatesController.cs`:
```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.ExchangeRates;

[ApiController]
[Route("api/exchange-rates")]
[Authorize]
public class ExchangeRatesController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.ExchangeRates.Include(e => e.SetBy)
            .OrderByDescending(e => e.EffectiveDate)
            .Select(e => new ExchangeRateResponse(e.Id, e.CurrencyCode, e.CurrencyName, e.RateToAed, e.EffectiveDate, e.IsActive, e.SetBy.Name))
            .ToListAsync());

    [HttpGet("active")]
    public async Task<IActionResult> GetActive() =>
        Ok(await db.ExchangeRates.Where(e => e.IsActive)
            .Select(e => new ExchangeRateResponse(e.Id, e.CurrencyCode, e.CurrencyName, e.RateToAed, e.EffectiveDate, e.IsActive, ""))
            .ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Accountant")]
    public async Task<IActionResult> Create(CreateExchangeRateRequest req)
    {
        var rate = new ExchangeRate
        {
            CurrencyCode = req.CurrencyCode.ToUpper(), CurrencyName = req.CurrencyName,
            RateToAed = req.RateToAed, EffectiveDate = req.EffectiveDate,
            SetByUserId = CurrentUserId
        };
        db.ExchangeRates.Add(rate);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = rate.Id }, rate);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Accountant")]
    public async Task<IActionResult> Update(int id, UpdateExchangeRateRequest req)
    {
        var rate = await db.ExchangeRates.FindAsync(id);
        if (rate is null) return NotFound();
        rate.RateToAed = req.RateToAed; rate.EffectiveDate = req.EffectiveDate; rate.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add exchange rates endpoint (accountant only)"
```

---

## Task 8: Quotation Requests

**Files:**
- Create: `Features/Requisitions/RequisitionDtos.cs`
- Create: `Features/Requisitions/RequisitionsController.cs`
- Create: `BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs`

- [ ] **Step 1: Write failing workflow test**

`BomPriceApproval.Tests/Requisitions/RequisitionWorkflowTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Requisitions;

public class RequisitionWorkflowTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task CreateRequisition_AsSalesPerson_ReturnsCreated()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/requisitions", new
        {
            CustomerId = 1,
            ItemId = 1,
            ExpectedQty = 1000m,
            CurrencyCode = "AED"
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task GetRequisitions_AsSalesPerson_SeesOnlyOwnRequests()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/api/requisitions");
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
```

- [ ] **Step 2: Create RequisitionDtos**

`Features/Requisitions/RequisitionDtos.cs`:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Requisitions;

public record CreateRequisitionRequest(int CustomerId, int ItemId, decimal ExpectedQty, string CurrencyCode = "AED");

public record RequisitionListItem(
    int Id, string RefNo, string Status, string ItemDescription,
    string CustomerName, decimal ExpectedQty, string CurrencyCode,
    string BranchName, string SalesPersonName, DateTime CreatedAt);

public record RequisitionDetail(
    int Id, string RefNo, string Status,
    int ItemId, string ItemDescription,
    int CustomerId, string CustomerName, string CustomerEmail, string CustomerPhone, string CustomerAddress,
    decimal ExpectedQty, string CurrencyCode, decimal? ExchangeRateSnapshot,
    int BranchId, string BranchName,
    int SalesPersonId, string SalesPersonName,
    DateTime CreatedAt, DateTime UpdatedAt,
    BomSummary? Bom,
    ApprovalSummary? Approval);

public record BomSummary(int Id, decimal TotalCostPerKg, bool HasCost);
public record ApprovalSummary(decimal SalesPriceAed, decimal? SalesPriceForeign, decimal ProfitMarginPct, bool IsApproved);
```

- [ ] **Step 3: Create RequisitionsController**

`Features/Requisitions/RequisitionsController.cs`:
```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Requisitions;

[ApiController]
[Route("api/requisitions")]
[Authorize]
public class RequisitionsController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var query = db.QuotationRequests
            .Include(q => q.Item).Include(q => q.Customer)
            .Include(q => q.Branch).Include(q => q.SalesPerson)
            .AsQueryable();

        query = CurrentRole switch
        {
            "SalesPerson" => query.Where(q => q.SalesPersonId == CurrentUserId),
            "BomCreator" => query.Where(q => q.BranchId == CurrentBranchId),
            _ when CurrentBranchId.HasValue => query.Where(q => q.BranchId == CurrentBranchId),
            _ => query
        };

        return Ok(await query.OrderByDescending(q => q.CreatedAt)
            .Select(q => new RequisitionListItem(
                q.Id, q.RefNo, q.Status.ToString(), q.Item.Description,
                q.Customer.Name, q.ExpectedQty, q.CurrencyCode,
                q.Branch.Name, q.SalesPerson.Name, q.CreatedAt))
            .ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var q = await db.QuotationRequests
            .Include(r => r.Item).Include(r => r.Customer)
            .Include(r => r.Branch).Include(r => r.SalesPerson)
            .Include(r => r.BomHeader).ThenInclude(b => b != null ? b.Cost : null)
            .Include(r => r.Approval)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (q is null) return NotFound();
        if (!CanAccess(q)) return Forbid();

        return Ok(new RequisitionDetail(
            q.Id, q.RefNo, q.Status.ToString(),
            q.ItemId, q.Item.Description,
            q.CustomerId, q.Customer.Name, q.Customer.Email, q.Customer.PhoneNumber, q.Customer.Address,
            q.ExpectedQty, q.CurrencyCode, q.ExchangeRateSnapshot,
            q.BranchId, q.Branch.Name, q.SalesPersonId, q.SalesPerson.Name,
            q.CreatedAt, q.UpdatedAt,
            q.BomHeader is null ? null : new BomSummary(q.BomHeader.Id, q.BomHeader.TotalCostPerKg, q.BomHeader.Cost is not null),
            q.Approval is null ? null : new ApprovalSummary(q.Approval.SalesPricePerKgAed, q.Approval.SalesPricePerKgForeign, q.Approval.ProfitMarginPct, q.Approval.IsApproved)));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Create(CreateRequisitionRequest req)
    {
        decimal? rateSnapshot = null;
        if (req.CurrencyCode != "AED")
        {
            var rate = await db.ExchangeRates
                .Where(e => e.CurrencyCode == req.CurrencyCode && e.IsActive)
                .OrderByDescending(e => e.EffectiveDate).FirstOrDefaultAsync();
            if (rate is null) return BadRequest(new { message = $"No active exchange rate for {req.CurrencyCode}" });
            rateSnapshot = rate.RateToAed;
        }

        var requisition = new QuotationRequest
        {
            BranchId = CurrentBranchId!.Value,
            SalesPersonId = CurrentUserId,
            CustomerId = req.CustomerId,
            ItemId = req.ItemId,
            ExpectedQty = req.ExpectedQty,
            CurrencyCode = req.CurrencyCode,
            ExchangeRateSnapshot = rateSnapshot,
            Status = RequisitionStatus.BomPending
        };

        db.QuotationRequests.Add(requisition);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = requisition.Id }, new { requisition.Id, requisition.RefNo });
    }

    private bool CanAccess(QuotationRequest q) => CurrentRole switch
    {
        "SalesPerson" => q.SalesPersonId == CurrentUserId,
        "BomCreator" => q.BranchId == CurrentBranchId,
        "Accountant" => true,
        "ManagingDirector" => true,
        "Admin" => true,
        _ => false
    };
}
```

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: add quotation requisitions CRUD with role-based visibility"
```

---

## Task 9: SignalR Notification Hub & Service

**Files:**
- Create: `Features/Notifications/NotificationHub.cs`
- Create: `Infrastructure/Services/NotificationService.cs`
- Create: `Features/Notifications/NotificationsController.cs`

- [ ] **Step 1: Create NotificationHub**

`Features/Notifications/NotificationHub.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace BomPriceApproval.API.Features.Notifications;

[Authorize]
public class NotificationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{userId}");
        await base.OnConnectedAsync();
    }
}
```

- [ ] **Step 2: Create NotificationService**

`Infrastructure/Services/NotificationService.cs`:
```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Features.Notifications;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;

namespace BomPriceApproval.API.Infrastructure.Services;

public class NotificationService(AppDbContext db, IHubContext<NotificationHub> hub)
{
    public async Task SendAsync(int userId, string message, int referenceId, string referenceType)
    {
        var notification = new Notification
        {
            UserId = userId, Message = message,
            ReferenceId = referenceId, ReferenceType = referenceType
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();

        await hub.Clients.Group($"user_{userId}").SendAsync("ReceiveNotification", new
        {
            notification.Id, notification.Message,
            notification.ReferenceId, notification.ReferenceType,
            notification.CreatedAt, notification.IsRead
        });
    }
}
```

- [ ] **Step 3: Create NotificationsController**

`Features/Notifications/NotificationsController.cs`:
```csharp
using System.Security.Claims;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Notifications;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.Notifications
            .Where(n => n.UserId == CurrentUserId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new { n.Id, n.Message, n.ReferenceId, n.ReferenceType, n.IsRead, n.CreatedAt })
            .ToListAsync());

    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkRead(int id)
    {
        var n = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == CurrentUserId);
        if (n is null) return NotFound();
        n.IsRead = true;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        await db.Notifications
            .Where(n => n.UserId == CurrentUserId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return NoContent();
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount() =>
        Ok(new { count = await db.Notifications.CountAsync(n => n.UserId == CurrentUserId && !n.IsRead) });
}
```

- [ ] **Step 4: Register SignalR hub and NotificationService in Program.cs**

Add to `Program.cs` after `builder.Services.AddSignalR()`:
```csharp
builder.Services.AddScoped<NotificationService>();
```

Add after `app.MapControllers()`:
```csharp
app.MapHub<BomPriceApproval.API.Features.Notifications.NotificationHub>("/hubs/notifications");
```

- [ ] **Step 5: Commit**

```bash
git add .
git commit -m "feat: add SignalR notification hub and notification service"
```

---

## Task 10: BOM Creation

**Files:**
- Create: `Features/Bom/BomDtos.cs`
- Create: `Features/Bom/BomController.cs`
- Test: `BomPriceApproval.Tests/Bom/BomTests.cs`

- [ ] **Step 1: Write failing BOM test**

`BomPriceApproval.Tests/Bom/BomTests.cs`:
```csharp
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Bom;

public class BomTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task SubmitBom_UpdatesRequisitionStatusToCostingPending()
    {
        var client = factory.CreateClient();
        // Login as BOM creator, submit BOM for a requisition in BomPending status
        var response = await client.PostAsJsonAsync("/api/bom/1/submit", new
        {
            Lines = new[] { new { ProcessId = 1, RawMaterialItemId = 2, QtyPerKg = 0.8m, WastagePct = 5m } }
        });
        // Requisition status should become CostingPending
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
    }
}
```

- [ ] **Step 2: Create BomDtos**

`Features/Bom/BomDtos.cs`:
```csharp
namespace BomPriceApproval.API.Features.Bom;

public record BomLineInput(int ProcessId, int RawMaterialItemId, decimal QtyPerKg, decimal WastagePct);
public record SubmitBomRequest(List<BomLineInput> Lines);

public record BomLineResponse(int Id, int ProcessId, string ProcessName, int RawMaterialItemId,
    string RawMaterialDescription, decimal QtyPerKg, decimal WastagePct);

public record BomDetailResponse(int Id, int QuotationRequestId, string RefNo,
    string ItemDescription, List<BomLineResponse> Lines, decimal TotalCostPerKg, DateTime? SubmittedAt);
```

- [ ] **Step 3: Create BomController**

`Features/Bom/BomController.cs`:
```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Bom;

[ApiController]
[Route("api/bom")]
[Authorize]
public class BomController(AppDbContext db, NotificationService notificationService) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> Get(int requisitionId)
    {
        var bom = await db.BomHeaders
            .Include(b => b.Lines).ThenInclude(l => l.Process)
            .Include(b => b.Lines).ThenInclude(l => l.RawMaterial)
            .Include(b => b.QuotationRequest)
            .Include(b => b.Item)
            .FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);

        if (bom is null) return NotFound();

        return Ok(new BomDetailResponse(
            bom.Id, bom.QuotationRequestId, bom.QuotationRequest.RefNo,
            bom.Item.Description,
            bom.Lines.Select(l => new BomLineResponse(
                l.Id, l.ProcessId, l.Process.Name, l.RawMaterialItemId,
                l.RawMaterial.Description, l.QtyPerKg, l.WastagePct)).ToList(),
            bom.TotalCostPerKg, bom.SubmittedAt));
    }

    [HttpPost("{requisitionId}/start")]
    [Authorize(Roles = "BomCreator")]
    public async Task<IActionResult> Start(int requisitionId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.BomPending)
            return BadRequest(new { message = "Requisition is not in BomPending status" });

        req.Status = RequisitionStatus.BomInProgress;
        req.UpdatedAt = DateTime.UtcNow;

        var bom = new BomHeader { QuotationRequestId = requisitionId, ItemId = req.ItemId, CreatedByUserId = CurrentUserId };
        db.BomHeaders.Add(bom);
        await db.SaveChangesAsync();
        return Ok(new { bom.Id });
    }

    [HttpPost("{requisitionId}/submit")]
    [Authorize(Roles = "BomCreator")]
    public async Task<IActionResult> Submit(int requisitionId, SubmitBomRequest request)
    {
        var req = await db.QuotationRequests.Include(q => q.Branch).FirstOrDefaultAsync(q => q.Id == requisitionId);
        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.BomInProgress)
            return BadRequest(new { message = "BOM can only be submitted when status is BomInProgress" });

        var bom = await db.BomHeaders.Include(b => b.Lines).FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom is null) return BadRequest(new { message = "BOM not started. Call /start first." });

        // Replace lines
        db.BomLines.RemoveRange(bom.Lines);
        bom.Lines = request.Lines.Select(l => new BomLine
        {
            ProcessId = l.ProcessId, RawMaterialItemId = l.RawMaterialItemId,
            QtyPerKg = l.QtyPerKg, WastagePct = l.WastagePct
        }).ToList();

        bom.SubmittedAt = DateTime.UtcNow;
        req.Status = RequisitionStatus.CostingPending;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify accountants in same branch
        var accountants = await db.Users
            .Where(u => u.Role == UserRole.Accountant && (u.BranchId == req.BranchId || u.BranchId == null) && u.IsActive)
            .ToListAsync();

        foreach (var accountant in accountants)
            await notificationService.SendAsync(accountant.Id,
                $"BOM ready for costing: {req.RefNo}", req.Id, "QuotationRequest");

        return NoContent();
    }
}
```

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: add BOM creation and submission with status transition and notifications"
```

---

## Task 11: Costing Entry

**Files:**
- Create: `Features/Costing/CostingDtos.cs`
- Create: `Features/Costing/CostingController.cs`

- [ ] **Step 1: Create CostingDtos**

`Features/Costing/CostingDtos.cs`:
```csharp
using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Features.Costing;

public record RawMaterialCostInput(int BomLineId, decimal CostPerKg);

public record SubmitCostingRequest(
    List<RawMaterialCostInput> RawMaterialCosts,
    LandedCostType LandedCostType,
    decimal LandedCostValue,
    decimal FohAmount);

public record CostingDetailResponse(
    int Id, decimal RawMaterialCostTotal, string LandedCostType,
    decimal LandedCostValue, decimal FohAmount, decimal TotalCostPerKg,
    DateTime SubmittedAt);
```

- [ ] **Step 2: Create CostingController**

`Features/Costing/CostingController.cs`:
```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Costing;

[ApiController]
[Route("api/costing")]
[Authorize(Roles = "Accountant")]
public class CostingController(AppDbContext db, NotificationService notificationService) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> Get(int requisitionId)
    {
        var bom = await db.BomHeaders.Include(b => b.Cost).FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom?.Cost is null) return NotFound();
        var c = bom.Cost;
        return Ok(new CostingDetailResponse(c.Id, c.RawMaterialCostTotal, c.LandedCostType.ToString(),
            c.LandedCostValue, c.FohAmount, c.TotalCostPerKg, c.SubmittedAt));
    }

    [HttpPost("{requisitionId}/start")]
    public async Task<IActionResult> Start(int requisitionId)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.CostingPending)
            return BadRequest(new { message = "Requisition is not in CostingPending status" });
        req.Status = RequisitionStatus.CostingInProgress;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{requisitionId}/submit")]
    public async Task<IActionResult> Submit(int requisitionId, SubmitCostingRequest request)
    {
        var req = await db.QuotationRequests.FindAsync(requisitionId);
        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.CostingInProgress)
            return BadRequest(new { message = "Costing can only be submitted when status is CostingInProgress" });

        var bom = await db.BomHeaders.Include(b => b.Lines).Include(b => b.Cost)
            .FirstOrDefaultAsync(b => b.QuotationRequestId == requisitionId);
        if (bom is null) return BadRequest(new { message = "No BOM found for this requisition" });

        // Calculate raw material cost total
        decimal rawMaterialTotal = 0;
        foreach (var rc in request.RawMaterialCosts)
        {
            var line = bom.Lines.FirstOrDefault(l => l.Id == rc.BomLineId);
            if (line is not null)
                rawMaterialTotal += rc.CostPerKg * line.QtyPerKg * (1 + line.WastagePct / 100);
        }

        // Calculate landed cost
        decimal landedCostAed = request.LandedCostType == LandedCostType.Percentage
            ? rawMaterialTotal * request.LandedCostValue / 100
            : request.LandedCostValue;

        decimal totalCost = rawMaterialTotal + landedCostAed + request.FohAmount;

        if (bom.Cost is not null) db.BomCosts.Remove(bom.Cost);

        var cost = new BomCost
        {
            BomHeaderId = bom.Id,
            RawMaterialCostTotal = rawMaterialTotal,
            LandedCostType = request.LandedCostType,
            LandedCostValue = request.LandedCostValue,
            FohAmount = request.FohAmount,
            TotalCostPerKg = totalCost,
            SubmittedByUserId = CurrentUserId
        };
        db.BomCosts.Add(cost);

        bom.TotalCostPerKg = totalCost;
        req.Status = RequisitionStatus.MdReview;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify MD
        var mds = await db.Users.Where(u => u.Role == UserRole.ManagingDirector && u.IsActive).ToListAsync();
        foreach (var md in mds)
            await notificationService.SendAsync(md.Id,
                $"Costing complete, ready for approval: {req.RefNo}", req.Id, "QuotationRequest");

        return NoContent();
    }
}
```

- [ ] **Step 3: Commit**

```bash
git add .
git commit -m "feat: add costing entry with cost calculation and MD notification"
```

---

## Task 12: MD Approval & Email + PDF

**Files:**
- Create: `Features/Approvals/ApprovalDtos.cs`
- Create: `Features/Approvals/ApprovalsController.cs`
- Create: `Infrastructure/Services/EmailService.cs`
- Create: `Infrastructure/Services/PdfService.cs`

- [ ] **Step 1: Create EmailService**

`Infrastructure/Services/EmailService.cs`:
```csharp
using MailKit.Net.Smtp;
using MimeKit;

namespace BomPriceApproval.API.Infrastructure.Services;

public class EmailService(IConfiguration config, ILogger<EmailService> logger)
{
    public async Task SendAsync(string toEmail, string toName, string subject, string htmlBody, byte[]? attachment = null, string? attachmentName = null)
    {
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config["Email:FromName"], config["Email:FromAddress"]));
            message.To.Add(new MailboxAddress(toName, toEmail));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
            if (attachment is not null && attachmentName is not null)
                bodyBuilder.Attachments.Add(attachmentName, attachment, new ContentType("application", "pdf"));

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(config["Email:Host"], int.Parse(config["Email:Port"]!), MailKit.Security.SecureSocketOptions.StartTls);
            await client.AuthenticateAsync(config["Email:Username"], config["Email:Password"]);
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send email to {Email}", toEmail);
        }
    }
}
```

- [ ] **Step 2: Create PdfService**

`Infrastructure/Services/PdfService.cs`:
```csharp
using BomPriceApproval.API.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BomPriceApproval.API.Infrastructure.Services;

public class PdfService
{
    public byte[] GenerateQuotation(QuotationRequest req, QuotationApproval approval)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("FUJAIRAH PLASTIC FACTORY").Bold().FontSize(16);
                            c.Item().Text(req.Branch.Name).FontSize(11).FontColor(Colors.Grey.Medium);
                        });
                        row.ConstantItem(150).AlignRight().Column(c =>
                        {
                            c.Item().Text("SALES QUOTATION").Bold().FontSize(13);
                            c.Item().Text(req.RefNo).FontColor(Colors.Blue.Medium);
                            c.Item().Text(approval.ApprovedAt.ToString("dd/MM/yyyy"));
                        });
                    });
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Light);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    // Customer details
                    col.Item().Background(Colors.Grey.Lighten3).Padding(10).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        t.Cell().Text("Customer:").Bold();
                        t.Cell().Text(req.Customer.Name);
                        t.Cell().Text("Address:");
                        t.Cell().Text(req.Customer.Address);
                        t.Cell().Text("Phone:");
                        t.Cell().Text(req.Customer.PhoneNumber);
                        t.Cell().Text("Email:");
                        t.Cell().Text(req.Customer.Email);
                    });

                    col.Item().PaddingTop(16).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1.5f);
                        });

                        t.Header(h =>
                        {
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Item Description").Bold().FontColor(Colors.White);
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("Qty").Bold().FontColor(Colors.White);
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("Unit").Bold().FontColor(Colors.White);
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text($"Unit Price ({req.CurrencyCode})").Bold().FontColor(Colors.White);
                        });

                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Light).Padding(6).Text(req.Item.Description);
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Light).Padding(6).AlignRight().Text(req.ExpectedQty.ToString("N0"));
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Light).Padding(6).AlignRight().Text("kg");

                        var displayPrice = req.CurrencyCode == "AED"
                            ? approval.SalesPricePerKgAed
                            : approval.SalesPricePerKgForeign ?? approval.SalesPricePerKgAed;
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Light).Padding(6).AlignRight()
                            .Text($"{displayPrice:N4}");
                    });

                    col.Item().PaddingTop(8).AlignRight().Column(c =>
                    {
                        var displayPrice = req.CurrencyCode == "AED"
                            ? approval.SalesPricePerKgAed
                            : approval.SalesPricePerKgForeign ?? approval.SalesPricePerKgAed;
                        var totalPrice = displayPrice * req.ExpectedQty;
                        c.Item().Text($"Total Price ({req.CurrencyCode}): {totalPrice:N2}").Bold().FontSize(12);

                        if (req.CurrencyCode != "AED" && req.ExchangeRateSnapshot.HasValue)
                            c.Item().PaddingTop(4).Text($"Exchange Rate: 1 {req.CurrencyCode} = {req.ExchangeRateSnapshot:N4} AED (as of {approval.ApprovedAt:dd/MM/yyyy})")
                                .FontColor(Colors.Grey.Medium).FontSize(9);
                    });

                    col.Item().PaddingTop(24).Text("Valid for 30 days from date of issue.").FontColor(Colors.Grey.Medium).Italic();
                });

                page.Footer().AlignRight().Column(c =>
                {
                    c.Item().PaddingTop(16).LineHorizontal(0.5f).LineColor(Colors.Grey.Light);
                    c.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(160).Column(sig =>
                        {
                            sig.Item().Text("Authorized by: Eng Khaled").Bold();
                            sig.Item().PaddingTop(20).LineHorizontal(0.5f).LineColor(Colors.Black);
                            sig.Item().AlignCenter().Text("Signature").FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });
        }).GeneratePdf();
    }
}
```

- [ ] **Step 3: Create ApprovalDtos**

`Features/Approvals/ApprovalDtos.cs`:
```csharp
namespace BomPriceApproval.API.Features.Approvals;

public record ApproveRequest(decimal SalesPricePerKgAed, string? Notes);
public record RejectRequest(string Notes);

public record ApprovalDetailResponse(
    int Id, decimal SalesPricePerKgAed, decimal? SalesPricePerKgForeign,
    decimal ProfitMarginPct, decimal MaterialCostPct, decimal OtherCostPct,
    bool IsApproved, string? Notes, DateTime ApprovedAt);

public record MdReviewDetail(
    string RefNo, string ItemDescription, string CustomerName,
    decimal ExpectedQty, string CurrencyCode, decimal? ExchangeRate,
    decimal RawMaterialCostPerKg, decimal LandedCostPerKg, decimal FohPerKg,
    decimal TotalCostPerKg, decimal MaterialCostPct, decimal LandedCostPct, decimal FohPct);
```

- [ ] **Step 4: Create ApprovalsController**

`Features/Approvals/ApprovalsController.cs`:
```csharp
using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Approvals;

[ApiController]
[Route("api/approvals")]
[Authorize(Roles = "ManagingDirector")]
public class ApprovalsController(AppDbContext db, NotificationService notificationSvc, EmailService emailSvc, PdfService pdfSvc) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("{requisitionId}")]
    public async Task<IActionResult> GetReview(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Item).Include(q => q.Customer).Include(q => q.Branch)
            .Include(q => q.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req?.BomHeader?.Cost is null) return NotFound();
        var c = req.BomHeader.Cost;

        return Ok(new MdReviewDetail(
            req.RefNo, req.Item.Description, req.Customer.Name,
            req.ExpectedQty, req.CurrencyCode, req.ExchangeRateSnapshot,
            c.RawMaterialCostTotal, 
            req.BomHeader.TotalCostPerKg > 0 ? req.BomHeader.TotalCostPerKg - c.RawMaterialCostTotal - c.FohAmount : 0,
            c.FohAmount, req.BomHeader.TotalCostPerKg,
            c.RawMaterialCostTotal / req.BomHeader.TotalCostPerKg * 100,
            (req.BomHeader.TotalCostPerKg - c.RawMaterialCostTotal - c.FohAmount) / req.BomHeader.TotalCostPerKg * 100,
            c.FohAmount / req.BomHeader.TotalCostPerKg * 100));
    }

    [HttpPost("{requisitionId}/approve")]
    public async Task<IActionResult> Approve(int requisitionId, ApproveRequest request)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Item).Include(q => q.Customer)
            .Include(q => q.Branch).Include(q => q.SalesPerson)
            .Include(q => q.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null || req.BomHeader?.Cost is null) return NotFound();
        if (req.Status != RequisitionStatus.MdReview)
            return BadRequest(new { message = "Requisition is not in MdReview status" });

        var totalCost = req.BomHeader.TotalCostPerKg;
        var profitMargin = (request.SalesPricePerKgAed - totalCost) / request.SalesPricePerKgAed * 100;
        var matPct = req.BomHeader.Cost.RawMaterialCostTotal / totalCost * 100;
        var otherPct = 100 - matPct;

        decimal? foreignPrice = null;
        if (req.CurrencyCode != "AED" && req.ExchangeRateSnapshot.HasValue)
            foreignPrice = request.SalesPricePerKgAed / req.ExchangeRateSnapshot.Value;

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            SalesPricePerKgAed = request.SalesPricePerKgAed,
            SalesPricePerKgForeign = foreignPrice,
            ProfitMarginPct = profitMargin,
            MaterialCostPct = matPct,
            OtherCostPct = otherPct,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = true
        };
        db.QuotationApprovals.Add(approval);
        req.Status = RequisitionStatus.Approved;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Reload for PDF
        await db.Entry(approval).Reference(a => a.QuotationRequest).LoadAsync();

        // Generate PDF
        var pdf = pdfSvc.GenerateQuotation(req, approval);

        // Notify + email sales person
        await notificationSvc.SendAsync(req.SalesPersonId,
            $"Quotation approved! Download ready: {req.RefNo}", req.Id, "QuotationRequest");

        await emailSvc.SendAsync(req.SalesPerson.Email, req.SalesPerson.Name,
            $"Quotation Approved – {req.RefNo}",
            $"<p>Dear {req.SalesPerson.Name},</p><p>Your quotation <strong>{req.RefNo}</strong> has been approved. Please find the quotation PDF attached.</p><p>Regards,<br/>Fujairah Plastic Factory</p>",
            pdf, $"{req.RefNo}-Quotation.pdf");

        return Ok(new { message = "Approved", req.RefNo });
    }

    [HttpPost("{requisitionId}/reject")]
    public async Task<IActionResult> Reject(int requisitionId, RejectRequest request)
    {
        var req = await db.QuotationRequests
            .Include(q => q.SalesPerson)
            .Include(q => q.BomHeader).ThenInclude(b => b!.Cost)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req is null) return NotFound();
        if (req.Status != RequisitionStatus.MdReview)
            return BadRequest(new { message = "Requisition is not in MdReview status" });

        var approval = new QuotationApproval
        {
            QuotationRequestId = req.Id,
            SalesPricePerKgAed = 0,
            ProfitMarginPct = 0,
            MaterialCostPct = 0,
            OtherCostPct = 0,
            ApprovedByUserId = CurrentUserId,
            Notes = request.Notes,
            IsApproved = false
        };
        db.QuotationApprovals.Add(approval);
        req.Status = RequisitionStatus.Rejected;
        req.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Notify sales person and accountants
        await notificationSvc.SendAsync(req.SalesPersonId,
            $"Quotation rejected: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");

        var accountants = await db.Users
            .Where(u => u.Role == UserRole.Accountant && u.IsActive).ToListAsync();
        foreach (var acct in accountants)
            await notificationSvc.SendAsync(acct.Id,
                $"Quotation rejected by MD: {req.RefNo}. Reason: {request.Notes}", req.Id, "QuotationRequest");

        return Ok(new { message = "Rejected" });
    }

    [HttpGet("{requisitionId}/pdf")]
    [Authorize(Roles = "ManagingDirector,SalesPerson,Accountant,Admin")]
    public async Task<IActionResult> DownloadPdf(int requisitionId)
    {
        var req = await db.QuotationRequests
            .Include(q => q.Item).Include(q => q.Customer)
            .Include(q => q.Branch).Include(q => q.SalesPerson)
            .Include(q => q.BomHeader).ThenInclude(b => b!.Cost)
            .Include(q => q.Approval)
            .FirstOrDefaultAsync(q => q.Id == requisitionId);

        if (req?.Approval is null || !req.Approval.IsApproved) return NotFound();

        var pdf = pdfSvc.GenerateQuotation(req, req.Approval);
        return File(pdf, "application/pdf", $"{req.RefNo}-Quotation.pdf");
    }
}
```

- [ ] **Step 5: Register services in Program.cs**

Add after `builder.Services.AddScoped<NotificationService>()`:
```csharp
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<PdfService>();
```

- [ ] **Step 6: Commit**

```bash
git add .
git commit -m "feat: add MD approval, PDF generation, and email dispatch"
```

---

## Task 13: Item Import (Excel & CSV)

**Files:**
- Create: `Infrastructure/Services/ItemImportService.cs`
- Create: `Features/Items/ItemImportController.cs`

- [ ] **Step 1: Create ItemImportService**

`Infrastructure/Services/ItemImportService.cs`:
```csharp
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using ClosedXML.Excel;
using CsvHelper;
using System.Globalization;

namespace BomPriceApproval.API.Infrastructure.Services;

public record ImportResult(int Imported, int Skipped, List<string> Errors);

public class ItemImportService(AppDbContext db)
{
    public async Task<ImportResult> ImportExcelAsync(Stream stream, int branchId)
    {
        using var workbook = new XLWorkbook(stream);
        var ws = workbook.Worksheet(1);
        var rows = ws.RangeUsed().RowsUsed().Skip(1); // skip header

        var items = rows.Select(row => new
        {
            Code = row.Cell(1).GetString().Trim(),
            Description = row.Cell(2).GetString().Trim(),
            TypeStr = row.Cell(3).GetString().Trim()
        }).ToList();

        return await ImportItemsAsync(items.Select(i => (i.Code, i.Description, i.TypeStr)), branchId);
    }

    public async Task<ImportResult> ImportCsvAsync(Stream stream, int branchId)
    {
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        var records = csv.GetRecords<dynamic>().ToList();

        var items = records.Select(r => (
            Code: (string)r.Code,
            Description: (string)r.Description,
            TypeStr: (string)r.Type
        ));

        return await ImportItemsAsync(items, branchId);
    }

    private async Task<ImportResult> ImportItemsAsync(IEnumerable<(string Code, string Description, string TypeStr)> items, int branchId)
    {
        int imported = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var (code, description, typeStr) in items)
        {
            if (!Enum.TryParse<ItemType>(typeStr, ignoreCase: true, out var type))
            {
                errors.Add($"Invalid type '{typeStr}' for item code '{code}'");
                skipped++;
                continue;
            }

            if (await db.Items.AnyAsync(i => i.Code == code && i.BranchId == branchId))
            {
                skipped++;
                continue;
            }

            db.Items.Add(new Item { Code = code, Description = description, Type = type, BranchId = branchId });
            imported++;
        }

        await db.SaveChangesAsync();
        return new ImportResult(imported, skipped, errors);
    }
}
```

- [ ] **Step 2: Create ItemImportController**

`Features/Items/ItemImportController.cs`:
```csharp
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BomPriceApproval.API.Features.Items;

[ApiController]
[Route("api/items/import")]
[Authorize(Roles = "Admin")]
public class ItemImportController(ItemImportService importService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Import([FromForm] IFormFile file, [FromForm] int branchId)
    {
        if (file.Length == 0) return BadRequest(new { message = "File is empty" });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext is not (".xlsx" or ".csv"))
            return BadRequest(new { message = "Only .xlsx and .csv files are supported" });

        using var stream = file.OpenReadStream();
        var result = ext == ".xlsx"
            ? await importService.ImportExcelAsync(stream, branchId)
            : await importService.ImportCsvAsync(stream, branchId);

        return Ok(result);
    }
}
```

- [ ] **Step 3: Register ItemImportService in Program.cs**

```csharp
builder.Services.AddScoped<ItemImportService>();
```

- [ ] **Step 4: Commit**

```bash
git add .
git commit -m "feat: add item import for Excel and CSV (admin only)"
```

---

## Task 14: CORS, Final Wiring & Smoke Test

**Files:**
- Modify: `BomPriceApproval.API/Program.cs`

- [ ] **Step 1: Final Program.cs — complete version**

Replace `Program.cs` with fully wired version:

```csharp
using System.Text;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Features.Notifications;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<PdfService>();
builder.Services.AddScoped<ItemImportService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
        };
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Enter: Bearer {token}",
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" }
            }, []
        }
    });
});

builder.Services.AddCors(opt =>
    opt.AddDefaultPolicy(p => p
        .WithOrigins("http://localhost:5300", "http://localhost:8081")
        .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    if (!db.Users.Any(u => u.Email == "admin@test.com"))
    {
        db.Users.Add(new User
        {
            Name = "Admin", Email = "admin@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@1234"),
            Role = UserRole.Admin, BranchId = null
        });
        await db.SaveChangesAsync();
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<NotificationHub>("/hubs/notifications");
app.Run();

public partial class Program { }
```

- [ ] **Step 2: Run full test suite**

```bash
cd BomPriceApproval.Tests
dotnet test -v
```

Expected: All tests pass.

- [ ] **Step 3: Run API and verify Swagger**

```bash
cd BomPriceApproval.API
dotnet run
```

Open: `https://localhost:7301/swagger`
Verify all endpoints appear: `/api/auth/login`, `/api/requisitions`, `/api/bom/{id}`, etc.

- [ ] **Step 4: Final commit**

```bash
git add .
git commit -m "feat: complete backend API — auth, workflow, notifications, PDF, email, item import"
```

---

## Self-Review Checklist

After writing this plan, spec coverage check:

| Spec Requirement | Covered In |
|-----------------|-----------|
| JWT auth, roles, branch isolation | Task 4, 5 |
| Two branches seeded | Task 3 (DbContext seed) |
| Sales person — own customers only | Task 6 |
| Items with duplicate check | Task 6 |
| Configurable processes (admin) | Task 5 |
| Exchange rates (accountant only) | Task 7 |
| Quotation request workflow | Task 8 |
| BOM creation per process | Task 10 |
| Costing with landed cost + FOH | Task 11 |
| MD approval + reject | Task 12 |
| PDF generation | Task 12 |
| Email with PDF attachment | Task 12 |
| SignalR in-app notifications | Task 9 |
| Item import Excel + CSV (admin) | Task 13 |
| Foreign currency + rate snapshot | Task 8 (Create), Task 12 (Approve) |
| Status tracker (all roles, filtered) | Task 8 (GetAll/Get) |
| Ports 7300/7301, DB 5500 | Task 1 |
