namespace BomPriceApproval.API.Domain.Enums;

public enum RequisitionStatus
{
    Draft,
    BomPending,
    BomInProgress,
    CostingPending,
    CostingInProgress,
    MdReview,
    Approved,
    Rejected
}
