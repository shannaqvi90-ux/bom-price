namespace BomPriceApproval.API.Features.ExchangeRates;

public record CreateExchangeRateRequest(string CurrencyCode, string CurrencyName, decimal RateToAed, DateTime EffectiveDate);
public record UpdateExchangeRateRequest(decimal RateToAed, DateTime EffectiveDate, bool IsActive);
public record ExchangeRateResponse(int Id, string CurrencyCode, string CurrencyName, decimal RateToAed, DateTime EffectiveDate, bool IsActive, string SetByName);
