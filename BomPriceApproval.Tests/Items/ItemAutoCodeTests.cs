using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Items;

public class ItemAutoCodeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<string> LoginAsync(string email, string password)
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    [Fact]
    public async Task CreateFinishedGood_AutoGeneratesFGCode()
    {
        var client = factory.CreateClient();
        var token = await LoginAsync("ali@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var req = new
        {
            code = "MANUAL-IGNORED",
            description = $"FG Test {Guid.NewGuid():N}",
            type = 0,    // FinishedGood
            isActive = true
        };
        var resp = await client.PostAsJsonAsync("/api/items", req);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var code = created.GetProperty("code").GetString()!;
        code.Should().MatchRegex(@"^FG-\d{4,}$");
    }

    [Fact]
    public async Task CreateRawMaterial_AutoGeneratesRMCode()
    {
        var client = factory.CreateClient();
        var token = await LoginAsync("ali@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var req = new
        {
            code = "MANUAL-IGNORED",
            description = $"RM Test {Guid.NewGuid():N}",
            type = 1,    // RawMaterial
            isActive = true
        };
        var resp = await client.PostAsJsonAsync("/api/items", req);
        resp.EnsureSuccessStatusCode();
        var created = await resp.Content.ReadFromJsonAsync<JsonElement>();
        var code = created.GetProperty("code").GetString()!;
        code.Should().MatchRegex(@"^RM-\d{4,}$");
    }

    [Fact]
    public async Task TwoFGItems_GetDistinctCodes()
    {
        var client = factory.CreateClient();
        var token = await LoginAsync("ali@test.com", "Test@1234");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp1 = await client.PostAsJsonAsync("/api/items", new
        {
            description = $"FG A {Guid.NewGuid():N}",
            type = 0
        });
        var resp2 = await client.PostAsJsonAsync("/api/items", new
        {
            description = $"FG B {Guid.NewGuid():N}",
            type = 0
        });

        resp1.EnsureSuccessStatusCode();
        resp2.EnsureSuccessStatusCode();

        var body1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        var body2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();

        var code1 = body1.GetProperty("code").GetString()!;
        var code2 = body2.GetProperty("code").GetString()!;

        code1.Should().MatchRegex(@"^FG-\d{4,}$");
        code2.Should().MatchRegex(@"^FG-\d{4,}$");
        code1.Should().NotBe(code2);
    }
}
