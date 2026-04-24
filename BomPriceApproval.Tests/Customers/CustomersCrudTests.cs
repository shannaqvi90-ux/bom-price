using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Customers;

public class CustomersCrudTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<LoginResponse> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LoginResponse>())!;
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns409()
    {
        var login = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var code = $"DUPTEST-{Guid.NewGuid():N}".Substring(0, 20);
        var first = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = code, Name = "A", Address = "", Email = "", PhoneNumber = ""
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = code, Name = "B", Address = "", Email = "", PhoneNumber = ""
        });
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_AsAdmin_SetsSalesPersonIdToNull()
    {
        var login = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var code = $"ADMCUST-{Guid.NewGuid():N}".Substring(0, 20);
        var resp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = code, Name = "Admin Co", Address = "", Email = "", PhoneNumber = ""
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CustomerResponse>();
        body!.SalesPersonId.Should().BeNull();
    }

    [Fact]
    public async Task GetAll_AsSalesPerson_OnlyReturnsOwnCustomers()
    {
        var login = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var resp = await _client.GetAsync("/api/customers");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await resp.Content.ReadFromJsonAsync<List<CustomerResponse>>();
        list!.Should().OnlyContain(c => c.SalesPersonId == login.UserId);
    }

    [Fact]
    public async Task Create_AsAccountant_Succeeds()
    {
        var login = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var code = $"ACCCUST-{Guid.NewGuid():N}".Substring(0, 20);
        var resp = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = code, Name = "Accountant Co", Address = "", Email = "", PhoneNumber = ""
        });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await resp.Content.ReadFromJsonAsync<CustomerResponse>();
        body!.SalesPersonId.Should().BeNull();
        body.CreatedByUserId.Should().Be(login.UserId);
    }

    [Fact]
    public async Task Update_AsAccountant_Succeeds()
    {
        var adminLogin = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminLogin.AccessToken);

        var code = $"ACCUPD-{Guid.NewGuid():N}".Substring(0, 20);
        var created = await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = code, Name = "Orig", Address = "", Email = "", PhoneNumber = ""
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdBody = await created.Content.ReadFromJsonAsync<CustomerResponse>();

        var acctLogin = await LoginAsync("sara@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", acctLogin.AccessToken);

        var upd = await _client.PutAsJsonAsync($"/api/customers/{createdBody!.Id}", new
        {
            Name = "Updated by Accountant", Address = "", Email = "", PhoneNumber = ""
        });
        upd.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record CustomerResponse(int Id, string Code, string Name, string Address, string Email, string PhoneNumber, int? SalesPersonId, string? SalesPersonName, int CreatedByUserId);
}
