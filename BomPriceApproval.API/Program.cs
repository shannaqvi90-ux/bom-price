using System.IdentityModel.Tokens.Jwt;
using System.Text;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Features.Notifications;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

// Preserve jti and exp claims under their standard short names after validation
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddScoped<PdfService>();
builder.Services.AddScoped<ItemImportService>();
builder.Services.AddScoped<CustomerImportService>();
builder.Services.AddScoped<PurchaseLedgerService>();
builder.Services.AddHostedService<RevokedJtiCleanupService>();

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
            },
            OnTokenValidated = async ctx =>
            {
                var jti = ctx.Principal?.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
                if (jti is null) return;
                var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var revoked = await db.RevokedJtis.AnyAsync(r => r.Jti == jti);
                if (revoked) ctx.Fail("Token revoked");
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
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

    // Admin user (branch-less)
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

    // Seed role users + customers + items + exchange rate for smoke testing
    // Branches (Id=1 Fujairah, Id=2 Al Ain) are already seeded via HasData migration
    if (!db.Users.Any(u => u.Email == "ali@test.com"))
    {
        const int fujairahBranchId = 1;
        var admin = db.Users.First(u => u.Email == "admin@test.com");

        var salesPerson = new User
        {
            Name = "Ali Sales", Email = "ali@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@1234"),
            Role = UserRole.SalesPerson, BranchId = fujairahBranchId
        };
        var bomCreator = new User
        {
            Name = "Bob BOM", Email = "bob@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@1234"),
            Role = UserRole.BomCreator, BranchId = fujairahBranchId
        };
        var accountant = new User
        {
            Name = "Sara Accounts", Email = "sara@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@1234"),
            Role = UserRole.Accountant, BranchId = fujairahBranchId
        };
        var md = new User
        {
            Name = "Managing Director", Email = "md@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Test@1234"),
            Role = UserRole.ManagingDirector, BranchId = null
        };
        db.Users.AddRange(salesPerson, bomCreator, accountant, md);
        await db.SaveChangesAsync();

        db.Customers.AddRange(
            new Customer
            {
                Code = "CUST-001",
                Name = "ACME Trading LLC", Address = "Fujairah Free Zone",
                Email = "orders@acme.test", PhoneNumber = "+97192000001",
                SalesPersonId = salesPerson.Id, CreatedByUserId = salesPerson.Id
            },
            new Customer
            {
                Code = "CUST-002",
                Name = "Gulf Plastics Co", Address = "Industrial Area, Fujairah",
                Email = "procurement@gulfplastics.test", PhoneNumber = "+97192000002",
                SalesPersonId = salesPerson.Id, CreatedByUserId = salesPerson.Id
            }
        );

        db.Items.AddRange(
            new Item
            {
                Code = "HDPE-20", Description = "HDPE Pipe 20mm",
                Type = ItemType.FinishedGood, BranchId = fujairahBranchId, IsActive = true
            },
            new Item
            {
                Code = "HDPE-25", Description = "HDPE Pipe 25mm",
                Type = ItemType.FinishedGood, BranchId = fujairahBranchId, IsActive = true
            },
            new Item
            {
                Code = "RM-PE100", Description = "PE100 Resin (Raw Material)",
                Type = ItemType.RawMaterial, BranchId = fujairahBranchId, IsActive = true
            }
        );

        db.ExchangeRates.Add(new ExchangeRate
        {
            CurrencyCode = "USD", CurrencyName = "US Dollar",
            RateToAed = 3.6725m, SetByUserId = admin.Id,
            EffectiveDate = DateTime.UtcNow, IsActive = true
        });

        await db.SaveChangesAsync();
    }

    // Ensure USD exchange rate exists (guard separately in case earlier seed was skipped or failed)
    if (!db.ExchangeRates.Any(e => e.CurrencyCode == "USD" && e.IsActive))
    {
        var admin = db.Users.FirstOrDefault(u => u.Email == "admin@test.com");
        if (admin is not null)
        {
            db.ExchangeRates.Add(new ExchangeRate
            {
                CurrencyCode = "USD", CurrencyName = "US Dollar",
                RateToAed = 3.6725m, SetByUserId = admin.Id,
                EffectiveDate = DateTime.UtcNow, IsActive = true
            });
            await db.SaveChangesAsync();
        }
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
