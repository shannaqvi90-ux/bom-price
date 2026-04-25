namespace BomPriceApproval.API.Features.Stats;

public record AccountantDashboardStats(
    int PendingCosting,
    int InProgress,
    int SubmittedThisMonth,
    int AwaitingMd);
