using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.Tests.Shared;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Approvals;

/// <summary>
/// V3-D3 B4: when MD calls set-margin, notifications are persisted for the SP
/// AND for active accountants assigned to the requisition's branch.
///
/// The walk uses <see cref="V3WorkflowTestHelpers.WalkToCustomerConfirmAsync"/>
/// which exercises the full path Draft → Costing → MdPricing → CustomerConfirm.
/// `EnsureAccountantInAlainAsync` (called inside that helper) guarantees sara
/// has a UserBranches row for Alain so the fan-out has a recipient to find.
/// </summary>
public class SetMarginNotificationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory = factory;

    [Fact]
    public async Task SetMargin_PersistsNotificationsForSpAndBranchAccountants()
    {
        // WalkToCustomerConfirm calls set-margin internally — perfect for asserting
        // post-fan-out state without re-implementing the walk.
        var reqId = await V3WorkflowTestHelpers.WalkToCustomerConfirmAsync(_factory);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1) SP got the original "priced — confirm with customer" notification.
        var sp = await db.Users.FirstAsync(u => u.Email == "ali@test.com");
        var spNotifs = await db.Notifications
            .Where(n => n.UserId == sp.Id
                     && n.ReferenceId == reqId
                     && n.ReferenceType == "QuotationRequest"
                     && n.Message.Contains("priced"))
            .ToListAsync();
        spNotifs.Should().NotBeEmpty("SP must receive the set-margin notification");

        // 2) Branch accountants (sara is wired into Alain by the walk helper)
        //    got the new D-3 fan-out notification.
        var sara = await db.Users.FirstAsync(u => u.Email == "sara@test.com");
        var saraNotifs = await db.Notifications
            .Where(n => n.UserId == sara.Id
                     && n.ReferenceId == reqId
                     && n.ReferenceType == "QuotationRequest"
                     && n.Message.Contains("margin set by MD"))
            .ToListAsync();
        saraNotifs.Should().NotBeEmpty("branch accountant must receive set-margin fan-out notification");
    }

    [Fact]
    public async Task SetMargin_FanOutSkipsInactiveAccountants()
    {
        // Wire a fresh accountant into Alain, mark them inactive, run the walk,
        // and assert no notification was persisted for the inactive user.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var marker = $"inactive-acc-{Guid.NewGuid():N}@test.local";

            var inactive = new BomPriceApproval.API.Domain.Entities.User
            {
                Email = marker,
                Name = "Inactive Accountant",
                PasswordHash = "x",
                Role = UserRole.Accountant,
                IsActive = false,
            };
            db.Users.Add(inactive);
            await db.SaveChangesAsync();

            db.UserBranches.Add(new BomPriceApproval.API.Domain.Entities.UserBranch
            {
                UserId = inactive.Id,
                BranchId = V3WorkflowTestHelpers.AlainBranchId,
            });
            await db.SaveChangesAsync();
        }

        var reqId = await V3WorkflowTestHelpers.WalkToCustomerConfirmAsync(_factory);

        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inactiveUser = await verifyDb.Users
            .Where(u => u.Email.StartsWith("inactive-acc-") && !u.IsActive)
            .OrderByDescending(u => u.Id)
            .FirstAsync();

        var inactiveNotifs = await verifyDb.Notifications
            .Where(n => n.UserId == inactiveUser.Id && n.ReferenceId == reqId)
            .ToListAsync();
        inactiveNotifs.Should().BeEmpty("inactive accountant must NOT receive set-margin fan-out");
    }
}
