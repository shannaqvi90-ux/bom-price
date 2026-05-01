using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Domain.Enums;
using BomPriceApproval.API.Features.Requisitions;
using BomPriceApproval.API.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace BomPriceApproval.Tests.Approvals;

public class FinalPriceComputerTests
{
    [Fact]
    public void Compute_AedRequisition_SaleEqualsAed()
    {
        var req = MakeReq("AED");
        var approval = MakeApproval(rateSnapshot: null, items: new[]
        {
            (req.Items.ElementAt(0).Id, marginPerKg: 1.80m),
        });
        // BomCost: TotalCostPerKg = 3.20 for FG[0]
        AddCost(req, fgIdx: 0, totalCostPerKg: 3.20m);

        var result = FinalPriceComputer.Compute(req, approval);

        result.CurrencyCode.Should().Be("AED");
        result.RateSnapshot.Should().BeNull();
        result.PerFg[0].SalePerKg.Should().Be(5.00m);          // 3.20 + 1.80
        result.PerFg[0].SalePerKgAed.Should().Be(5.00m);       // == SalePerKg for AED
        result.PerFg[0].TotalAed.Should().Be(25_000m);          // 5.00 × 5000
        result.TotalAed.Should().Be(25_000m);
    }

    [Fact]
    public void Compute_ForeignRequisition_SalePerKgAedUsesRateSnapshot()
    {
        var req = MakeReq("USD");
        var approval = MakeApproval(rateSnapshot: 3.6725m, items: new[]
        {
            (req.Items.ElementAt(0).Id, marginPerKg: 1.00m),
        });
        AddCost(req, fgIdx: 0, totalCostPerKg: 1.00m);

        var result = FinalPriceComputer.Compute(req, approval);

        result.CurrencyCode.Should().Be("USD");
        result.RateSnapshot.Should().Be(3.6725m);
        result.PerFg[0].SalePerKg.Should().Be(2.00m);                 // 1.00 + 1.00
        result.PerFg[0].SalePerKgAed.Should().Be(7.345m);             // 2.00 × 3.6725
        result.PerFg[0].TotalAed.Should().Be(36_725m);                // 7.345 × 5000
    }

    [Fact]
    public void Compute_MultiFg_SumsTotalsCorrectly()
    {
        var req = MakeReq("AED", fgCount: 2);
        var approval = MakeApproval(rateSnapshot: null, items: new[]
        {
            (req.Items.ElementAt(0).Id, marginPerKg: 1.00m),
            (req.Items.ElementAt(1).Id, marginPerKg: 2.00m),
        });
        AddCost(req, fgIdx: 0, totalCostPerKg: 3.00m);
        AddCost(req, fgIdx: 1, totalCostPerKg: 5.00m);

        var result = FinalPriceComputer.Compute(req, approval);

        result.PerFg.Should().HaveCount(2);
        result.PerFg[0].TotalAed.Should().Be(20_000m);  // (3+1) × 5000
        result.PerFg[1].TotalAed.Should().Be(35_000m);  // (5+2) × 5000
        result.TotalAed.Should().Be(55_000m);
    }

    [Fact]
    public void Compute_ZeroMargin_OK()
    {
        var req = MakeReq("AED");
        var approval = MakeApproval(rateSnapshot: null, items: new[]
        {
            (req.Items.ElementAt(0).Id, marginPerKg: 0m),
        });
        AddCost(req, fgIdx: 0, totalCostPerKg: 4.00m);

        var result = FinalPriceComputer.Compute(req, approval);

        result.PerFg[0].SalePerKg.Should().Be(4.00m);
        result.TotalAed.Should().Be(20_000m);
    }

    // === Test fixtures ===
    private static QuotationRequest MakeReq(string currency, int fgCount = 1)
    {
        var req = new QuotationRequest
        {
            Id = 100,
            CurrencyCode = currency,
            Status = RequisitionStatus.MdFinalSign,
            BranchId = 2,
            CustomerId = 1,
            SalesPersonId = 1,
        };
        for (int i = 0; i < fgCount; i++)
        {
            req.Items.Add(new RequisitionItem
            {
                Id = 1000 + i,
                ItemId = 2000 + i,
                Item = new Item { Id = 2000 + i, Description = $"FG {i + 1}", Code = $"FG-{i + 1}" },
                ExpectedQty = 5000m,
            });
        }
        return req;
    }

    private static QuotationApproval MakeApproval(decimal? rateSnapshot,
        (int RequisitionItemId, decimal marginPerKg)[] items)
    {
        var qa = new QuotationApproval
        {
            Stage = ApprovalStage.InitialPricing,
            RateSnapshot = rateSnapshot,
        };
        foreach (var (riId, margin) in items)
            qa.Items.Add(new ApprovalItem { RequisitionItemId = riId, MarginPerKg = margin });
        return qa;
    }

    private static void AddCost(QuotationRequest req, int fgIdx, decimal totalCostPerKg)
    {
        var ri = req.Items.ElementAt(fgIdx);
        var bomHeader = new BomHeader { RequisitionItemId = ri.Id };
        bomHeader.Cost = new BomCost { BomHeaderId = bomHeader.Id, TotalCostPerKg = totalCostPerKg };
        ri.BomHeader = bomHeader;
    }
}
