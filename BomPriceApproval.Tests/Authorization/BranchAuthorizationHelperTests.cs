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
    [Fact]
    public void SalesPerson_AlwaysAuthorized_RegardlessOfBranch()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sp = new User { Role = UserRole.SalesPerson, BranchId = 1 };
        BranchAuthorization.UserAuthorizedForBranch(sp, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(sp, 2, db).Should().BeTrue();
    }

    [Fact]
    public void BomCreator_AuthorizedOnlyForOwnBranch()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bom = new User { Role = UserRole.BomCreator, BranchId = 1 };
        BranchAuthorization.UserAuthorizedForBranch(bom, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(bom, 2, db).Should().BeFalse();
    }

    [Fact]
    public void Accountant_AuthorizedForBranchesInUserBranches()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acct = new User { Email = $"acct-{Guid.NewGuid():N}@test.com", Name = "Tmp", Role = UserRole.Accountant, IsActive = true };
        db.Users.Add(acct);
        db.SaveChanges();
        db.UserBranches.Add(new UserBranch { UserId = acct.Id, BranchId = 1 });
        db.SaveChanges();

        BranchAuthorization.UserAuthorizedForBranch(acct, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(acct, 2, db).Should().BeFalse();
    }

    [Fact]
    public void Accountant_WithEmptyUserBranches_AuthorizedForNothing()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var acct = new User { Email = $"acct-{Guid.NewGuid():N}@test.com", Name = "Tmp", Role = UserRole.Accountant, IsActive = true };
        db.Users.Add(acct);
        db.SaveChanges();

        BranchAuthorization.UserAuthorizedForBranch(acct, 1, db).Should().BeFalse();
        BranchAuthorization.UserAuthorizedForBranch(acct, 2, db).Should().BeFalse();
    }

    [Fact]
    public void ManagingDirector_AlwaysAuthorized()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var md = new User { Role = UserRole.ManagingDirector };
        BranchAuthorization.UserAuthorizedForBranch(md, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(md, 999, db).Should().BeTrue();
    }

    [Fact]
    public void Admin_AlwaysAuthorized()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = new User { Role = UserRole.Admin };
        BranchAuthorization.UserAuthorizedForBranch(admin, 1, db).Should().BeTrue();
        BranchAuthorization.UserAuthorizedForBranch(admin, 999, db).Should().BeTrue();
    }
}
