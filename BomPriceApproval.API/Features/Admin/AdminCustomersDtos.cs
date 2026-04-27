namespace BomPriceApproval.API.Features.Admin;

public record HardDeleteCustomerRequest(string Reason);

public record HardDeleteCustomerBlockedResponse(
    string Error,
    IReadOnlyList<int> BlockingRequisitions);
