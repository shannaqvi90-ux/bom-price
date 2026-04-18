using System.ComponentModel.DataAnnotations;

namespace BomPriceApproval.API.Features.Customers;

public record CreateCustomerRequest(
    [Required, MaxLength(50)] string Code,
    [Required, MaxLength(200)] string Name,
    [MaxLength(500)] string Address,
    [MaxLength(255)] string Email,
    [MaxLength(50)] string PhoneNumber);

public record UpdateCustomerRequest(
    [Required, MaxLength(200)] string Name,
    [MaxLength(500)] string Address,
    [MaxLength(255)] string Email,
    [MaxLength(50)] string PhoneNumber);

public record CustomerResponse(int Id, string Code, string Name, string Address, string Email, string PhoneNumber, int? SalesPersonId, string? SalesPersonName, int CreatedByUserId);
