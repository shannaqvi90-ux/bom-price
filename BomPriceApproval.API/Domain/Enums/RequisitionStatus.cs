namespace BomPriceApproval.API.Domain.Enums;

public enum RequisitionStatus
{
    // V2.3 values — kept for legacy data reads. Int slots preserved.
    Draft = 0,
    BomPending = 1,           // deprecated; cancelled at V3 cutover
    BomInProgress = 2,        // deprecated; cancelled at V3 cutover
    CostingPending = 3,       // deprecated; cancelled at V3 cutover
    CostingInProgress = 4,    // deprecated; cancelled at V3 cutover
    MdReview = 5,             // deprecated; cancelled at V3 cutover
    Approved = 6,             // KEPT — V2.3 Approved reqs stay as-is post-cutover
    Rejected = 7,             // KEPT — used in V3 (MD-rejected from MdPricing)

    // V3 NEW values
    Costing = 8,
    MdPricing = 9,
    CustomerConfirm = 10,
    MdFinalSign = 11,
    Signed = 12,
    Cancelled = 13
}
