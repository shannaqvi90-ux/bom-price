using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Features.Admin;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Admin;

public class CompanySettingsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> TokenAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login",
            new { Email = email, Password = password });
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!.AccessToken;
    }

    private void AuthAs(string token) =>
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Get_AsAdmin_ReturnsSeededSettings()
    {
        var token = await TokenAsync("admin@test.com", "Admin@1234");
        AuthAs(token);

        var resp = await _client.GetAsync("/api/admin/company-settings");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<CompanySettingsDto>();
        dto!.CompanyName.Should().Be("FUJAIRAH PLASTIC FACTORY");
        dto.QuotationValidityDays.Should().Be(30);
        dto.Email.Should().Be("info@fujairahplastic.com");
        dto.TermsAndConditions.Should().Contain("30 days");
    }

    [Fact]
    public async Task Get_AsNonAdmin_Returns403()
    {
        var token = await TokenAsync("sara@test.com", "Test@1234");
        AuthAs(token);

        var resp = await _client.GetAsync("/api/admin/company-settings");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Put_AsAdmin_UpdatesAndAudits()
    {
        var token = await TokenAsync("admin@test.com", "Admin@1234");
        AuthAs(token);

        var uniqueReason = $"Test update {Guid.NewGuid():N}";
        var newName = $"FPF Test {Guid.NewGuid():N}";
        var body = new UpdateCompanySettingsRequest(
            CompanyName: newName,
            Address: "PO Box 1, Alain, UAE",
            Telephone: "+971 3 111 2222",
            Trn: "100000001",
            Email: "test@example.com",
            Website: "www.example.com",
            QuotationValidityDays: 14,
            TermsAndConditions: "Line one\nLine two",
            Reason: uniqueReason);

        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<CompanySettingsDto>();
        dto!.CompanyName.Should().Be(newName);
        dto.QuotationValidityDays.Should().Be(14);
        dto.UpdatedByName.Should().NotBeNullOrEmpty();

        // Verify audit row + restore original via cleanup
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var audit = await db.AdminAuditLogs
            .Where(a => a.EntityType == "CompanySettings" && a.Reason == uniqueReason)
            .FirstOrDefaultAsync();
        audit.Should().NotBeNull();
        audit!.ActionType.Should().Be(AdminActionType.UpdateCompanySettings);
        audit.EntityId.Should().Be(1);
        audit.BeforeJson.Should().Contain("FUJAIRAH PLASTIC FACTORY");
        audit.AfterJson.Should().Contain(newName);

        // Cleanup: restore original settings + remove audit row
        var s = await db.CompanySettings.FirstAsync(x => x.Id == 1);
        s.CompanyName = "FUJAIRAH PLASTIC FACTORY";
        s.Address = "Fujairah, United Arab Emirates";
        s.Telephone = "";
        s.Trn = "";
        s.Email = "info@fujairahplastic.com";
        s.Website = "";
        s.QuotationValidityDays = 30;
        s.TermsAndConditions = string.Join("\n", new[]
        {
            "This quotation is valid for 30 days from the date of issue.",
            "Prices are subject to change without prior notice after the validity period.",
            "Payment terms as per mutually agreed contract.",
            "Delivery: Ex-Works Fujairah unless otherwise agreed in writing.",
            "All disputes are subject to the jurisdiction of UAE courts."
        });
        s.UpdatedByUserId = null;
        db.AdminAuditLogs.Remove(audit);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Put_AsNonAdmin_Returns403()
    {
        var token = await TokenAsync("sara@test.com", "Test@1234");
        AuthAs(token);

        var body = new UpdateCompanySettingsRequest(
            "X", "X", "X", "X", "x@x.com", "X", 30, "X", "valid reason");
        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task Put_InvalidValidityDays_Returns400(int days)
    {
        var token = await TokenAsync("admin@test.com", "Admin@1234");
        AuthAs(token);

        var body = new UpdateCompanySettingsRequest(
            "FPF", "Addr", "Tel", "Trn", "e@e.com", "Web",
            days, "Terms", "valid reason");
        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        problem!.Errors.Should().ContainKey("QuotationValidityDays");
    }

    [Fact]
    public async Task Put_EmptyReason_Returns400()
    {
        var token = await TokenAsync("admin@test.com", "Admin@1234");
        AuthAs(token);

        var body = new UpdateCompanySettingsRequest(
            "FPF", "Addr", "Tel", "Trn", "e@e.com", "Web", 30, "Terms", "abc");
        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        problem!.Errors.Should().ContainKey("Reason");
    }

    [Fact]
    public async Task Put_EmptyCompanyName_Returns400()
    {
        var token = await TokenAsync("admin@test.com", "Admin@1234");
        AuthAs(token);

        var body = new UpdateCompanySettingsRequest(
            "", "Addr", "Tel", "Trn", "e@e.com", "Web", 30, "Terms", "valid reason");
        var resp = await _client.PutAsJsonAsync("/api/admin/company-settings", body);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var problem = await resp.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        problem!.Errors.Should().ContainKey("CompanyName");
    }
}
