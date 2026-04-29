namespace BomPriceApproval.API.Domain.Enums;

public enum AdminActionType
{
    DeleteRequisition,
    RollbackStatus,
    ReassignSp,
    UnlockBom,
    UnlockCosting,
    ResetPassword,
    OverridePrices,
    HardDeleteCustomer,

    // V3 NEW values
    RollbackToCosting,          // C5 renamed (was UnlockCosting)
    V3CutoverMigration          // logged once during Phase C cutover SQL
}
