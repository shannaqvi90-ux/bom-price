namespace BomPriceApproval.API.Features.Customers;

public record CreateCustomerRequest(string Name, string Address, string Email, string PhoneNumber);
public record UpdateCustomerRequest(string Name, string Address, string Email, string PhoneNumber);
public record CustomerResponse(int Id, string Name, string Address, string Email, string PhoneNumber, int BranchId, int CreatedByUserId);
