using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Authorization;
using BomPriceApproval.API.Infrastructure.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BomPriceApproval.Tests.Authorization;

public class BranchAuthorizationHelperTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private AppDbContext NewDb()
    {
        var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>();
    }

    [Fact]
    public void SalesPerson_AlwaysAuthorized_RegardlessOfBranch()
    {
        using var db = NewDb();
        var sp = new User { Id = 999_001, Role = UserRole.SalesPerson, BranchId = 1 };
        BranchAuthorization.UserAuthorizedForBranch(sp, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(sp, 2, db).Should().BeTrue();
    }

    [Fact]
    public void BomCreator_AuthorizedOnlyForOwnBranch()
    {
        using var db = NewDb();
        var bom = new User { Id = 999_002, Role = UserRole.BomCreator, BranchId = 1 };
        BranchAuthorization.UserAuthorizedForBranch(bom, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(bom, 2, db).Should().BeFalse();
    }

    [Fact]
    public void Accountant_AuthorizedForBranchesInUserBranches()
    {
        using var db = NewDb();
        var acct = new User { Id = 999_003, Email = "tmp1@test.com", Name = "Tmp1", Role = UserRole.Accountant, IsActive = true };
        db.Users.Add(acct);
        db.UserBranches.Add(new UserBranch { UserId = acct.Id, BranchId = 1 });
        db.SaveChanges();

        BranchAuthorization.UserAuthorizedForBranch(acct, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(acct, 2, db).Should().BeFalse();
    }

    [Fact]
    public void Accountant_WithEmptyUserBranches_AuthorizedForNothing()
    {
        using var db = NewDb();
        var acct = new User { Id = 999_004, Email = "tmp2@test.com", Name = "Tmp2", Role = UserRole.Accountant, IsActive = true };
        db.Users.Add(acct);
        db.SaveChanges();

        BranchAuthorization.UserAuthorizedForBranch(acct, 1, db).Should().BeFalse();
        BranchAuthorization.UserAuthorizedForBranch(acct, 2, db).Should().BeFalse();
    }

    [Fact]
    public void ManagingDirector_AlwaysAuthorized()
    {
        using var db = NewDb();
        var md = new User { Id = 999_005, Role = UserRole.ManagingDirector };
        BranchAuthorization.UserAuthorizedForBranch(md, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(md, 999, db).Should().BeTrue();
    }

    [Fact]
    public void Admin_AlwaysAuthorized()
    {
        using var db = NewDb();
        var admin = new User { Id = 999_006, Role = UserRole.Admin };
        BranchAuthorization.UserAuthorizedForBranch(admin, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(admin, 999, db).Should().BeTrue();
    }
}
