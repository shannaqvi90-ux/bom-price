using System.Security.Claims;
using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BomPriceApproval.API.Features.ExchangeRates;

[ApiController]
[Route("api/exchange-rates")]
[Authorize]
public class ExchangeRatesController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<IActionResult> GetAll() =>
        Ok(await db.ExchangeRates.Include(e => e.SetBy)
            .OrderByDescending(e => e.EffectiveDate)
            .Select(e => new ExchangeRateResponse(e.Id, e.CurrencyCode, e.CurrencyName, e.RateToAed, e.EffectiveDate, e.IsActive, e.SetBy.Name))
            .ToListAsync());

    [HttpGet("active")]
    public async Task<IActionResult> GetActive() =>
        Ok(await db.ExchangeRates.Where(e => e.IsActive)
            .Select(e => new ExchangeRateResponse(e.Id, e.CurrencyCode, e.CurrencyName, e.RateToAed, e.EffectiveDate, e.IsActive, ""))
            .ToListAsync());

    [HttpPost]
    [Authorize(Roles = "Accountant")]
    public async Task<IActionResult> Create(CreateExchangeRateRequest req)
    {
        var rate = new ExchangeRate
        {
            CurrencyCode = req.CurrencyCode.ToUpper(), CurrencyName = req.CurrencyName,
            RateToAed = req.RateToAed, EffectiveDate = req.EffectiveDate,
            SetByUserId = CurrentUserId
        };
        db.ExchangeRates.Add(rate);
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetAll), new { id = rate.Id }, rate);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Accountant")]
    public async Task<IActionResult> Update(int id, UpdateExchangeRateRequest req)
    {
        var rate = await db.ExchangeRates.FindAsync(id);
        if (rate is null) return NotFound();
        rate.RateToAed = req.RateToAed; rate.EffectiveDate = req.EffectiveDate; rate.IsActive = req.IsActive;
        await db.SaveChangesAsync();
        return NoContent();
    }
}
