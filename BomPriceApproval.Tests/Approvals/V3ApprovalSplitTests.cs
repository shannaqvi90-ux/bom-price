using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Approvals;

/// <summary>
/// V3 split-approval coverage (Tasks 26-29 endpoints):
///   • set-margin     (MdPricing → CustomerConfirm)
///   • accept-customer (CustomerConfirm → MdFinalSign)
///   • reject-customer (CustomerConfirm → MdPricing, supersedes prior approval)
///   • final-sign     (MdFinalSign → Signed, requires "SIGN" token)
///
/// All walks live in <see cref="V3WorkflowTestHelpers"/>; tests stay focused
/// on the assertion surface for each split endpoint.
/// </summary>
public class V3ApprovalSplitTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    [Fact]
    public async Task FullHappyPath_DraftToSigned()
    {
        await V3WorkflowTestHelpers.EnsureAccountantInAlainAsync(_factory);

        // 1) SP creates + submits — transitions Draft → Costing.
        var sales = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);
        var (customerId, fgId, rmId, processId) = await V3WorkflowTestHelpers.SeedV3MinimumAsync(_factory);
        var (createStatus, reqId, _) =
            await V3WorkflowTestHelpers.CreateV3DraftRequisitionAsync(sales, customerId, fgId, rmId, processId);
        createStatus.Should().Be(HttpStatusCode.Created);

        var spSubmit = await sales.PostAsync($"/api/requisitions/{reqId}/submit", content: null);
        spSubmit.EnsureSuccessStatusCode();

        // 2) Accountant submits — Costing → MdPricing. Cost data populated direct-DB.
        // Use admin client (cross-branch role) — V3 reqs are pinned to Alain but
        // sara's seeded JWT branchId claim is Fujairah(1), which trips CostingController.
        await V3WorkflowTestHelpers.PopulateBomCostAsync(_factory, reqId);
        var admin = await V3WorkflowTestHelpers.CreateAdminClientAsync(_factory);
        var costingSubmit = await admin.PostAsync($"/api/costing/{reqId}/submit", content: null);
        costingSubmit.EnsureSuccessStatusCode();

        // 3) MD set-margin — MdPricing → CustomerConfirm.
        var reqItemIds = await V3WorkflowTestHelpers.GetReqItemIdsAsync(sales, reqId);
        var md = await V3WorkflowTestHelpers.CreateMdClientAsync(_factory);
        var setMargin = await md.PostAsJsonAsync($"/api/approvals/{reqId}/set-margin", new
        {
            notes = "Initial margin",
            items = reqItemIds.Select(id => new { requisitionItemId = id, marginPerKg = 1.5m }).ToArray()
        });
        setMargin.EnsureSuccessStatusCode();
        var setMarginBody = await setMargin.Content.ReadFromJsonAsync<JsonElement>();
        setMarginBody.GetProperty("status").GetString().Should().Be("CustomerConfirm");

        // 4) SP accept-customer — CustomerConfirm → MdFinalSign.
        var accept = await sales.PostAsJsonAsync($"/api/approvals/{reqId}/accept-customer",
            new { customerFeedback = "Customer agreed on the call" });
        accept.EnsureSuccessStatusCode();
        var acceptBody = await accept.Content.ReadFromJsonAsync<JsonElement>();
        acceptBody.GetProperty("status").GetString().Should().Be("MdFinalSign");

        // 5) MD final-sign — MdFinalSign → Signed. PDF stub throws NotImplementedException
        //    inside ApprovalsController; controller swallows + logs warning, transition stays.
        var finalSign = await md.PostAsJsonAsync($"/api/approvals/{reqId}/final-sign",
            new { confirmationToken = "SIGN", notes = "Signed off" });
        finalSign.EnsureSuccessStatusCode();
        var finalBody = await finalSign.Content.ReadFromJsonAsync<JsonElement>();
        finalBody.GetProperty("status").GetString().Should().Be("Signed");
    }

    [Fact]
    public async Task FinalSign_WrongToken_Returns400()
    {
        var reqId = await V3WorkflowTestHelpers.WalkToMdFinalSignAsync(_factory);

        var md = await V3WorkflowTestHelpers.CreateMdClientAsync(_factory);
        // Case-mismatch — controller checks ConfirmationToken != "SIGN" exactly.
        var resp = await md.PostAsJsonAsync($"/api/approvals/{reqId}/final-sign",
            new { confirmationToken = "SiGn", notes = (string?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectCustomer_LoopsBackToMdPricing_AndSupersedesApproval()
    {
        var reqId = await V3WorkflowTestHelpers.WalkToCustomerConfirmAsync(_factory);

        var sales = await V3WorkflowTestHelpers.CreateSalesClientAsync(_factory);
        var resp = await sales.PostAsJsonAsync($"/api/approvals/{reqId}/reject-customer",
            new { reason = "Customer wants a lower price" });

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("MdPricing");

        // Verify the InitialPricing approval that was created at set-margin
        // is now superseded — controller flips IsSuperseded + sets SupersededAt.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var approvals = await db.QuotationApprovals
            .Where(qa => qa.QuotationRequestId == reqId
                      && qa.Stage == ApprovalStage.InitialPricing)
            .ToListAsync();
        approvals.Should().NotBeEmpty();
        approvals.Should().OnlyContain(a => a.IsSuperseded);
        approvals.Should().OnlyContain(a => a.SupersededAt != null);
    }
}
