using BomPriceApproval.API.Domain.Enums;

namespace BomPriceApproval.API.Domain.Workflow;

/// <summary>
/// Single source of truth for V3 requisition state transitions.
/// Used by RequisitionsController, CostingController, ApprovalsController, and AdminRequisitionsController.
/// </summary>
public static class RequisitionStateMachine
{
    private static readonly HashSet<(RequisitionStatus, RequisitionStatus)> AllowedTransitions = new()
    {
        // Happy path
        (RequisitionStatus.Draft, RequisitionStatus.Costing),
        (RequisitionStatus.Costing, RequisitionStatus.MdPricing),
        (RequisitionStatus.MdPricing, RequisitionStatus.CustomerConfirm),
        (RequisitionStatus.MdPricing, RequisitionStatus.Rejected),
        (RequisitionStatus.CustomerConfirm, RequisitionStatus.MdFinalSign),
        (RequisitionStatus.CustomerConfirm, RequisitionStatus.MdPricing),
        (RequisitionStatus.MdFinalSign, RequisitionStatus.Signed),

        // Cancel — any non-terminal can move to Cancelled
        (RequisitionStatus.Draft, RequisitionStatus.Cancelled),
        (RequisitionStatus.Costing, RequisitionStatus.Cancelled),
        (RequisitionStatus.MdPricing, RequisitionStatus.Cancelled),
        (RequisitionStatus.CustomerConfirm, RequisitionStatus.Cancelled),
        (RequisitionStatus.MdFinalSign, RequisitionStatus.Cancelled),
    };

    private static readonly Dictionary<RequisitionStatus, RequisitionStatus[]> AdminRollback = new()
    {
        { RequisitionStatus.Signed,          new[] { RequisitionStatus.MdFinalSign } },
        { RequisitionStatus.MdFinalSign,     new[] { RequisitionStatus.CustomerConfirm } },
        { RequisitionStatus.CustomerConfirm, new[] { RequisitionStatus.MdPricing } },
        { RequisitionStatus.MdPricing,       new[] { RequisitionStatus.Costing } },
        { RequisitionStatus.Costing,         new[] { RequisitionStatus.Draft } },
    };

    public static bool CanTransition(RequisitionStatus from, RequisitionStatus to)
        => AllowedTransitions.Contains((from, to));

    public static bool IsTerminal(RequisitionStatus status)
        => status is RequisitionStatus.Signed
                  or RequisitionStatus.Rejected
                  or RequisitionStatus.Cancelled;

    public static IReadOnlyList<RequisitionStatus> AdminRollbackTargets(RequisitionStatus from)
        => AdminRollback.TryGetValue(from, out var targets) ? targets : Array.Empty<RequisitionStatus>();
}
