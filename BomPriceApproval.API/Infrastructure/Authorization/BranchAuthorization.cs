using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;

namespace BomPriceApproval.API.Infrastructure.Authorization;

public static class BranchAuthorization
{
    /// <summary>
    /// Returns true if the user is authorized to act on a requisition in the given branch.
    /// SP scoping is by self via SalesPersonId — branch is not the right dimension for SP, returns true.
    /// Accountant: M:N via UserBranches table.
    /// MD/Admin: cross-branch by role.
    /// </summary>
    public static bool UserAuthorizedForBranch(User user, int branchId, AppDbContext db) =>
        user.Role switch
        {
            UserRole.SalesPerson      => true,
            UserRole.Accountant       => db.UserBranches.Any(ub => ub.UserId == user.Id && ub.BranchId == branchId),
            UserRole.ManagingDirector => true,
            UserRole.Admin            => true,
            _                         => false
        };
}
