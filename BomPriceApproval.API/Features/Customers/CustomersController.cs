using System.Security.Claims;
using System.Text.RegularExpressions;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Infrastructure.Authorization;
using BomPriceApproval.API.Infrastructure.Data;
using BomPriceApproval.API.Infrastructure.Services;
using BomPriceApproval.API.Infrastructure.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.Customers;

[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController(AppDbContext db, ICodeGeneratorService codeGen) : ControllerBase
{
    // Defense-in-depth: reject obvious HTML/script-like payloads in customer text
    // fields before they hit the DB. Frontend (mobile + web) also sanitize on
    // render, but blocking at the API boundary stops new XSS-payload data from
    // ever reaching storage.
    private static readonly Regex HtmlPayloadRegex = new(
        @"<\s*\w+|javascript:|on\w+\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool ContainsHtmlPayload(string? value)
        => !string.IsNullOrEmpty(value) && HtmlPayloadRegex.IsMatch(value);

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string CurrentRole => User.FindFirstValue(ClaimTypes.Role)!;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // V23c P2: hide soft-deleted customers from default listings
        var query = db.Customers.Where(c => !c.IsDeleted).Include(c => c.SalesPerson).AsQueryable();
        if (CurrentRole == "SalesPerson")
        {
            var currentUser = await db.Users.FirstAsync(u => u.Id == CurrentUserId);
            var visibleIds = SalesAuthorization.VisibleSalesPersonIds(currentUser, db);
            query = query.Where(c => c.SalesPersonId.HasValue && visibleIds.Contains(c.SalesPersonId.Value));
        }

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
        // V23c P2: GET-by-id also hides soft-deleted customers (returns 404).
        // Historical req detail pages use the navigation property which still
        // resolves to the anonymized row — they don't go through this endpoint.
        var c = await db.Customers.Include(c => c.SalesPerson)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (c is null) return NotFound();
        if (CurrentRole == "SalesPerson")
        {
            var me = await db.Users.FirstAsync(u => u.Id == CurrentUserId);
            var visibleIds = SalesAuthorization.VisibleSalesPersonIds(me, db);
            if (!c.SalesPersonId.HasValue || !visibleIds.Contains(c.SalesPersonId.Value)) return Forbid();
        }
        return Ok(new CustomerResponse(
            c.Id, c.Code, c.Name, c.Address, c.Email, c.PhoneNumber,
            c.SalesPersonId, c.SalesPerson?.Name, c.CreatedByUserId));
    }

    [HttpPost]
    [Authorize(Roles = "SalesPerson,Admin,Accountant")]
    public async Task<IActionResult> Create(CreateCustomerRequest req)
    {
        if (ContainsHtmlPayload(req.Name) || ContainsHtmlPayload(req.Address))
            return Validation
                .Detail("HTML or script-like content is not allowed in Name or Address.")
                .Field(ContainsHtmlPayload(req.Name) ? "Name" : "Address",
                       "Remove tags / event handlers / javascript: links.")
                .Return();

        var customer = new Customer
        {
            Code = await codeGen.NextCustomerCodeAsync(),
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
    [Authorize(Roles = "SalesPerson,Admin,Accountant")]
    public async Task<IActionResult> Update(int id, UpdateCustomerRequest req)
    {
        var c = await db.Customers.FindAsync(id);
        if (c is null) return NotFound();
        if (CurrentRole == "SalesPerson")
        {
            var me = await db.Users.FirstAsync(u => u.Id == CurrentUserId);
            var visibleIds = SalesAuthorization.VisibleSalesPersonIds(me, db);
            if (!c.SalesPersonId.HasValue || !visibleIds.Contains(c.SalesPersonId.Value)) return Forbid();
        }

        if (ContainsHtmlPayload(req.Name) || ContainsHtmlPayload(req.Address))
            return Validation
                .Detail("HTML or script-like content is not allowed in Name or Address.")
                .Field(ContainsHtmlPayload(req.Name) ? "Name" : "Address",
                       "Remove tags / event handlers / javascript: links.")
                .Return();

        c.Name = req.Name;
        c.Address = req.Address;
        c.Email = req.Email;
        c.PhoneNumber = req.PhoneNumber;
        await db.SaveChangesAsync();
        return NoContent();
    }

    // V3 D20 — implicit FG filter for NewRequisitionPage. Returns active FGs ever
    // quoted for this customer (any past requisition status). UI uses this to
    // narrow the FG picker to items with prior history with the customer.
    [HttpGet("{id}/items")]
    public async Task<IActionResult> GetImplicitItems(int id)
    {
        var customer = await db.Customers
            .FirstOrDefaultAsync(c => c.Id == id && !c.IsDeleted);
        if (customer is null) return NotFound();

        var itemIds = await db.RequisitionItems
            .Where(ri => ri.QuotationRequest.CustomerId == id)
            .Select(ri => ri.ItemId)
            .Distinct()
            .ToListAsync();

        var items = await db.Items
            .Where(i => itemIds.Contains(i.Id) && i.Type == ItemType.FinishedGood && i.IsActive)
            .OrderBy(i => i.Description)
            .Select(i => new ImplicitItemResponse(i.Id, i.Code, i.Description))
            .ToListAsync();

        return Ok(items);
    }
}
