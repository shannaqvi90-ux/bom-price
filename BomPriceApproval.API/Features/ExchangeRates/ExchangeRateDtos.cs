using System.ComponentModel.DataAnnotations;

namespace BomPriceApproval.API.Features.ExchangeRates;

public record CreateExchangeRateRequest(
    [Required, RegularExpression("^[A-Z]{3}$", ErrorMessage = "Currency code must be 3 uppercase letters.")] string CurrencyCode,
    [Required, MaxLength(100)] string CurrencyName,
    [Range(0.0001, 999999)] decimal RateToAed,
    DateTime EffectiveDate);

public record UpdateExchangeRateRequest(
    [Range(0.0001, 999999)] decimal RateToAed,
    DateTime EffectiveDate,
    bool IsActive);

public record ExchangeRateResponse(int Id, string CurrencyCode, string CurrencyName, decimal RateToAed, DateTime EffectiveDate, bool IsActive, string SetByName);
