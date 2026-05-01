namespace BomPriceApproval.API.Features.Stats;

public record AccountantDashboardV3Dto(
    int Costing,
    int AwaitingMd,
    int AwaitingCustomer,
    int SubmittedThisMonth);
