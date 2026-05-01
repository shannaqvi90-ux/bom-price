using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Features.Requisitions;

namespace BomPriceApproval.API.Infrastructure.Services;

// Pure compute — no DB access, no async. Caller must hydrate
// req.Items[i].Item, req.Items[i].BomHeader.Cost, and approval.Items.
public static class FinalPriceComputer
{
    public static V3FinalPrice Compute(QuotationRequest req, QuotationApproval approval)
    {
        var rate = approval.RateSnapshot;
        var perFg = req.Items
            .OrderBy(ri => ri.Id)
            .Select(ri =>
            {
                var costPerKg = ri.BomHeader?.Cost?.TotalCostPerKg ?? 0m;
                var margin = approval.Items
                    .FirstOrDefault(ai => ai.RequisitionItemId == ri.Id)?.MarginPerKg ?? 0m;
                var salePerKg = costPerKg + margin;
                var salePerKgAed = rate.HasValue ? salePerKg * rate.Value : salePerKg;
                var totalAed = salePerKgAed * ri.ExpectedQty;
                return new V3FinalPriceItem(
                    ri.Id, ri.ItemId, ri.Item.Description, ri.ExpectedQty,
                    costPerKg, margin, salePerKg, salePerKgAed, totalAed);
            })
            .ToList();

        return new V3FinalPrice(
            TotalAed: perFg.Sum(p => p.TotalAed),
            CurrencyCode: req.CurrencyCode,
            RateSnapshot: rate,
            PerFg: perFg);
    }
}
