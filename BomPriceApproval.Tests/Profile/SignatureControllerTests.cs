using System.Net;
using System.Net.Http.Headers;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Profile;

/// <summary>
/// Covers SignatureController endpoints (Task 32):
///   • POST /api/profile/signature — MD-only upload, ≤ 500 KB, .png/.jpg/.jpeg
///   • GET  /api/profile/signature — MD reads back; 404 when none on file
///
/// Tests reset the MD seed user's <c>SignatureImagePath</c> at start so they
/// stay order-independent against the shared dev DB.
/// </summary>
public class SignatureControllerTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    // 1×1 transparent PNG — minimal valid bytes accepted by the upload endpoint.
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    [Fact]
    public async Task Upload_AsMd_StoresSignature()
    {
        await V3WorkflowTestHelpers.ClearMdSignatureAsync(_factory.Services);

        var client = await V3WorkflowTestHelpers.CreateMdClientAsync(_factory);

        using var content = new MultipartFormDataContent();
        var imgContent = new ByteArrayContent(OnePixelPng);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imgContent, "file", "test-signature.png");

        var resp = await client.PostAsync("/api/profile/signature", content);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // DB row updated.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var md = await db.Users.FirstAsync(u => u.Email == "md@test.com");
        md.SignatureImagePath.Should().NotBeNullOrWhiteSpace();

        // File written to disk where DB says it is.
        File.Exists(md.SignatureImagePath!).Should().BeTrue();
    }

    [Fact]
    public async Task Upload_AsAccountant_Returns403()
    {
        var client = await V3WorkflowTestHelpers.CreateAccountantClientAsync(_factory);

        using var content = new MultipartFormDataContent();
        var imgContent = new ByteArrayContent(OnePixelPng);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imgContent, "file", "test-signature.png");

        var resp = await client.PostAsync("/api/profile/signature", content);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Upload_OverSizeLimit_Returns400()
    {
        var client = await V3WorkflowTestHelpers.CreateMdClientAsync(_factory);

        // 600 KB > the 500 KB limit enforced by SignatureController.MaxBytes.
        var oversizedBytes = new byte[600 * 1024];
        using var content = new MultipartFormDataContent();
        var imgContent = new ByteArrayContent(oversizedBytes);
        imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(imgContent, "file", "huge-signature.png");

        var resp = await client.PostAsync("/api/profile/signature", content);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetOwn_NoSignatureUploaded_Returns404()
    {
        await V3WorkflowTestHelpers.ClearMdSignatureAsync(_factory.Services);

        var client = await V3WorkflowTestHelpers.CreateMdClientAsync(_factory);
        var resp = await client.GetAsync("/api/profile/signature");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
