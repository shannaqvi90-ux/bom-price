using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Data;

namespace BomPriceApproval.API.Infrastructure.Authorization;

public static class SalesAuthorization
{
    /// <summary>
    /// Returns the set of SalesPerson IDs whose customers + reqs are visible to <paramref name="user"/>.
    /// SP with a group: returns all SP members of that group.
    /// SP without a group: returns just self (Q9 fallback).
    /// Non-SP roles: empty array (caller should not use this helper for them).
    /// </summary>
    public static int[] VisibleSalesPersonIds(User user, AppDbContext db)
    {
        if (user.Role != UserRole.SalesPerson) return [];
        if (user.GroupId == null) return [user.Id];
        return db.Users
            .Where(u => u.GroupId == user.GroupId && u.Role == UserRole.SalesPerson)
            .Select(u => u.Id)
            .ToArray();
    }
}
