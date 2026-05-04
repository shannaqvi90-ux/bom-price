namespace BomPriceApproval.API.Domain.Enums;

public enum NotificationType
{
    RequisitionDeleted,
    StatusRolledBack,
    SalesPersonReassigned,
    BomUnlocked,
    CostingUnlocked,
    PricesOverridden,
    CustomerDeleted,

    // V3 NEW values
    MarginSet,                  // Stage 1 done — sent to sales + accountant
    CustomerConfirmRequested,   // sent to sales
    CustomerAccepted,           // sent to MD + accountant
    CustomerRejected,           // sent to MD + accountant
    SignedNotif,                // sent to sales + accountant (Signed name suffix to avoid clash with status enum)
    RequisitionCancelled,       // sent to sales

    CostingEditedAfterSubmit    // sent to MDs once when accountant edits in MdPricing
}
