namespace BomPriceApproval.API.Features.Admin;

public record CompanySettingsDto(
    string CompanyName,
    string Address,
    string Telephone,
    string Trn,
    string Email,
    string Website,
    int QuotationValidityDays,
    string TermsAndConditions,
    DateTime UpdatedAt,
    string? UpdatedByName);

public record UpdateCompanySettingsRequest(
    string CompanyName,
    string Address,
    string Telephone,
    string Trn,
    string Email,
    string Website,
    int QuotationValidityDays,
    string TermsAndConditions,
    string Reason);
