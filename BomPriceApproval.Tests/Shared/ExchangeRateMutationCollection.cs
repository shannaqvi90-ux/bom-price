namespace BomPriceApproval.Tests.Shared;

/// <summary>
/// Marker for xUnit collection that serializes test classes which mutate or
/// read the seeded ExchangeRates table. Without this serialization,
/// AdminOverridePricesTests (which adds USD rates with later EffectiveDate)
/// races with BomWithCostTests (which depends on the seeded 3.6725 rate
/// being the latest), producing the documented flaky failure on
/// `GetBom_ReturnsCostColumnsAfterCostingSubmitted_ForeignCurrency`.
/// xUnit only serializes test classes within the same collection — the rest
/// of the suite still runs in parallel.
/// </summary>
[CollectionDefinition(Name)]
public class ExchangeRateMutationCollection
{
    public const string Name = "ExchangeRateMutation";
}
