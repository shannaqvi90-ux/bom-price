using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
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
    private int? CurrentBranchId => int.TryParse(User.FindFirstValue("branchId"), out var b) && b > 0 ? b : null;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var query = db.Customers.AsQueryable();

        if (CurrentRole == "SalesPerson")
            query = query.Where(c => c.CreatedByUserId == CurrentUserId);
        else if (CurrentBranchId.HasValue)
            query = query.Where(c => c.BranchId == CurrentBranchId);

        return Ok(await query
            .Select(c => new CustomerResponse(c.Id, c.Name, c.Address, c.Email, c.PhoneNumber, c.BranchId, c.CreatedByUserId))
            .ToListAsync());
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id)
    {
        var c = await db.Customers.FindAsync(id);
        if (c is null) return NotFound();
        if (CurrentRole == "SalesPerson" && c.CreatedByUserId != CurrentUserId) return Forbid();
        return Ok(new CustomerResponse(c.Id, c.Name, c.Address, c.Email, c.PhoneNumber, c.BranchId, c.CreatedByUserId));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Create(CreateCustomerRequest req)
    {
        var customer = new Customer
        {
            Name = req.Name, Address = req.Address, Email = req.Email,
            PhoneNumber = req.PhoneNumber,
            BranchId = CurrentBranchId!.Value,
            CreatedByUserId = CurrentUserId
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = customer.Id },
            new CustomerResponse(customer.Id, customer.Name, customer.Address, customer.Email, customer.PhoneNumber, customer.BranchId, customer.CreatedByUserId));
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "SalesPerson")]
    public async Task<IActionResult> Update(int id, UpdateCustomerRequest req)
    {
        var c = await db.Customers.FindAsync(id);
        if (c is null) return NotFound();
        if (c.CreatedByUserId != CurrentUserId) return Forbid();
        c.Name = req.Name; c.Address = req.Address; c.Email = req.Email; c.PhoneNumber = req.PhoneNumber;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
