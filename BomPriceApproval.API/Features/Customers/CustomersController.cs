using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Customers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var query = db.Customers.Include(c => c.SalesPerson).AsQueryable();
        if (CurrentRole == "SalesPerson")
            query = query.Where(c => c.SalesPersonId == CurrentUserId);

        var list = await query
            .OrderBy(c => c.Name)
            .Select(c => new CustomerResponse(
                c.Id, c.Code, c.Name, c.Address, c.Email, c.PhoneNumber,
                c.SalesPersonId, c.SalesPerson != null ? c.SalesPerson.Name : null,
                c.CreatedByUserId))
            .ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var c = await db.Customers.Include(c => c.SalesPerson).FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        if (CurrentRole == "SalesPerson" && c.SalesPersonId != CurrentUserId) return Forbid();
        return Ok(new CustomerResponse(
            c.Id, c.Code, c.Name, c.Address, c.Email, c.PhoneNumber,
            c.SalesPersonId, c.SalesPerson?.Name, c.CreatedByUserId));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Create(CreateCustomerRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return Validation
                .Detail("Customer code is required.")
                .Field("Code", "Customer code is required.")
                .Return();

        if (await db.Customers.AnyAsync(c => c.Code == req.Code))
            return Conflict(new { message = $"Customer with code '{req.Code}' already exists." });

        var customer = new Customer
        {
            Code = req.Code.Trim(),
            Name = req.Name,
            Address = req.Address,
            Email = req.Email,
            PhoneNumber = req.PhoneNumber,
            SalesPersonId = CurrentRole == "SalesPerson" ? CurrentUserId : null,
            CreatedByUserId = CurrentUserId
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = customer.Id },
            new CustomerResponse(customer.Id, customer.Code, customer.Name, customer.Address,
                customer.Email, customer.PhoneNumber, customer.SalesPersonId, null, customer.CreatedByUserId));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SalesPerson,Admin")]
    public async Task<IActionResult> Update(int id, UpdateCustomerRequest req)
    {
        var c = await db.Customers.FindAsync(id);
        if (c is null) return NotFound();
        if (CurrentRole == "SalesPerson" && c.SalesPersonId != CurrentUserId) return Forbid();

        c.Name = req.Name;
        c.Address = req.Address;
        c.Email = req.Email;
        c.PhoneNumber = req.PhoneNumber;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
