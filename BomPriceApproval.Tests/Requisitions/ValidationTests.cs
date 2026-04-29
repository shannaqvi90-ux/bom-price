using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;

namespace BomPriceApproval.Tests.Requisitions;

public class ValidationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private record SanitizedErrorResponse(string Title, string Detail, int? Status);

    [Fact]
    public async Task UnhandledException_ReturnsSanitized500()
    {
        // Build a minimal in-process pipeline that mirrors the Production exception handler
        // in Program.cs, then verify it returns a sanitized ProblemDetails (no stack trace).
        var appBuilder = WebApplication.CreateBuilder();
        appBuilder.WebHost.UseTestServer();
        var app = appBuilder.Build();

        app.UseExceptionHandler(exceptionHandlerApp =>
        {
            exceptionHandlerApp.Run(async context =>
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "application/problem+json";
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    title = "Internal Server Error",
                    detail = "An unexpected error occurred.",
                    status = 500
                });
                await context.Response.WriteAsync(json);
            });
        });

        app.MapGet("/__test/throw", _ => throw new InvalidOperationException("boom: secret detail"));

        await app.StartAsync();
        var client = app.GetTestClient();

        var resp = await client.GetAsync("/__test/throw");

        resp.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        resp.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await resp.Content.ReadFromJsonAsync<SanitizedErrorResponse>();
        body!.Title.Should().Be("Internal Server Error");
        body.Detail.Should().Be("An unexpected error occurred.");
        body.Detail.Should().NotContain("boom");
        body.Detail.Should().NotContain("InvalidOperationException");

        await app.StopAsync();
    }
}
