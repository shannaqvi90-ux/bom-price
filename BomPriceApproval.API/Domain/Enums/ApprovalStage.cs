namespace BomPriceApproval.API.Domain.Enums;

public enum ApprovalStage
{
    InitialPricing = 0,  // V3 Stage 1: MD margin entry
    FinalSign = 1        // V3 Stage 2: locked + signed; or legacy V2.3 final approval
}
