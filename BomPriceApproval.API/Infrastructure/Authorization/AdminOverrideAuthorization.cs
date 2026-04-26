using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Infrastructure.Authorization;

public static class AdminOverrideAuthorization
{
    private static readonly Dictionary<RequisitionStatus, RequisitionStatus> RollbackWhitelist = new()
    {
        [RequisitionStatus.Approved] = RequisitionStatus.MdReview,
        [RequisitionStatus.MdReview] = RequisitionStatus.CostingPending,
        [RequisitionStatus.CostingInProgress] = RequisitionStatus.CostingPending,
        [RequisitionStatus.CostingPending] = RequisitionStatus.BomInProgress,
        [RequisitionStatus.BomInProgress] = RequisitionStatus.BomPending,
    };

    public static bool CanRollback(RequisitionStatus from, RequisitionStatus to)
        => RollbackWhitelist.TryGetValue(from, out var allowed) && allowed == to;

    public static bool CanUnlockBom(RequisitionStatus current)
        => current is RequisitionStatus.CostingPending
                    or RequisitionStatus.CostingInProgress
                    or RequisitionStatus.MdReview;

    public static bool CanUnlockCosting(RequisitionStatus current)
        => current is RequisitionStatus.MdReview;

    /// <summary>For UI/API to populate the rollback target dropdown.</summary>
    public static RequisitionStatus? RollbackTarget(RequisitionStatus from)
        => RollbackWhitelist.TryGetValue(from, out var target) ? target : null;
}
