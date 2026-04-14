using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BomPriceApproval.Tests.Customers;

public class CustomerImportTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<string> LoginAsync(string email, string password)
    {
        var resp = await _client.PostAsJsonAsync("/api/auth/login", new { Email = email, Password = password });
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        return body!.AccessToken;
    }

    [Fact]
    public async Task Template_AsAdmin_ReturnsXlsx()
    {
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/customers/import/template");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Template_AsSalesPerson_Returns403()
    {
        var token = await LoginAsync("ali@test.com", "Test@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await _client.GetAsync("/api/customers/import/template");
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Import_SkipsDuplicateCodes()
    {
        var token = await LoginAsync("admin@test.com", "Admin@1234");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Use a unique base code for this test run to avoid cross-run pollution
        var existingCode = $"EXIST-{Guid.NewGuid():N}".Substring(0, 20);
        var newCode = $"IMP-NEW-{Guid.NewGuid():N}".Substring(0, 20);

        // Pre-create the "existing" customer so the import sees it as a duplicate
        await _client.PostAsJsonAsync("/api/customers", new
        {
            Code = existingCode, Name = "Existing Co", Address = "", Email = "", PhoneNumber = ""
        });

        // Build xlsx with: 1 new code + 1 duplicate
        byte[] bytes;
        using (var wb = new XLWorkbook())
        {
            var ws = wb.Worksheets.Add("Customers");
            ws.Cell(1, 1).Value = "Code"; ws.Cell(1, 2).Value = "Name";
            ws.Cell(1, 3).Value = "Address"; ws.Cell(1, 4).Value = "Email"; ws.Cell(1, 5).Value = "PhoneNumber";
            ws.Cell(2, 1).Value = newCode; ws.Cell(2, 2).Value = "New Co";
            ws.Cell(3, 1).Value = existingCode; ws.Cell(3, 2).Value = "Dup Co";
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            bytes = ms.ToArray();
        }

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        form.Add(fileContent, "file", "test.xlsx");

        var resp = await _client.PostAsync("/api/customers/import", form);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await resp.Content.ReadFromJsonAsync<ImportResultDto>();
        result!.Imported.Should().Be(1);
        result.Skipped.Should().Be(1);
    }

    private record LoginResponse(string AccessToken, string RefreshToken, string Role, int UserId, string Name, int? BranchId);
    private record ImportResultDto(int Imported, int Skipped, List<string> Errors);
}
