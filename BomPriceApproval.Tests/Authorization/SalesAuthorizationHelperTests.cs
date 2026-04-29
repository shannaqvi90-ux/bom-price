using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Authorization;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Authorization;

public class SalesAuthorizationHelperTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public void NonSP_Role_ReturnsEmpty()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acct = new User { Role = UserRole.Accountant };
        SalesAuthorization.VisibleSalesPersonIds(acct, db).Should().BeEmpty();

        var md = new User { Role = UserRole.ManagingDirector };
        SalesAuthorization.VisibleSalesPersonIds(md, db).Should().BeEmpty();
    }

    [Fact]
    public void SP_NoGroup_ReturnsSelfOnly()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = new User { Id = 12345, Role = UserRole.SalesPerson, BranchId = 1, GroupId = null };
        SalesAuthorization.VisibleSalesPersonIds(sp, db).Should().BeEquivalentTo(new[] { 12345 });
    }

    [Fact]
    public void SP_WithGroup_ReturnsAllSPMembersOfThatGroup()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var group = new SalesGroup { Name = $"TestGrp-{Guid.NewGuid():N}".Substring(0, 18), IsActive = true };
        db.SalesGroups.Add(group);
        db.SaveChanges();

        var sp1 = new User { Email = $"sp1-{Guid.NewGuid():N}@test.com", Name = "SP One", Role = UserRole.SalesPerson, BranchId = 1, GroupId = group.Id, IsActive = true };
        var sp2 = new User { Email = $"sp2-{Guid.NewGuid():N}@test.com", Name = "SP Two", Role = UserRole.SalesPerson, BranchId = 2, GroupId = group.Id, IsActive = true };
        var acctInGroup = new User { Email = $"acct-{Guid.NewGuid():N}@test.com", Name = "Acct In Grp", Role = UserRole.Accountant, BranchId = 1, GroupId = group.Id, IsActive = true };
        db.Users.AddRange(sp1, sp2, acctInGroup);
        db.SaveChanges();

        var visible = SalesAuthorization.VisibleSalesPersonIds(sp1, db);
        visible.Should().BeEquivalentTo(new[] { sp1.Id, sp2.Id });
        visible.Should().NotContain(acctInGroup.Id, "non-SP members of the same group are excluded");
    }
}
