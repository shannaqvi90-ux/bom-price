namespace BomPriceApproval.API.Features.Customers;

public record CreateCustomerRequest(string Code, string Name, string Address, string Email, string PhoneNumber);
public record UpdateCustomerRequest(string Name, string Address, string Email, string PhoneNumber);
public record CustomerResponse(int Id, string Code, string Name, string Address, string Email, string PhoneNumber, int? SalesPersonId, string? SalesPersonName, int CreatedByUserId);
