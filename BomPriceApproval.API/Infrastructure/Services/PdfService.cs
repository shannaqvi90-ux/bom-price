using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BomPriceApproval.API.Infrastructure.Services;

/// <summary>
/// Unified quotation PDF generator. Single entry point — handles both V2.3
/// (legacy SalesPricePerKgAed prices) and V3 (MarginPerKg + cost computation)
/// approvals, optionally embeds the MD's signature image when a signer User
/// is provided. Re-queries the DB for cost lines + FX rates so callers don't
/// have to remember which Includes are needed.
/// </summary>
public class PdfService(AppDbContext db)
{
    // Brand palette — aligned with web/mobile (#1e40af blue family).
    private const string Brand     = "#1e40af";   // primary brand blue
    private const string BrandDark = "#1e3a8a";   // navy for accents/titles
    private const string BrandSoft = "#eff6ff";   // soft tint for cards
    private const string White     = "#FFFFFF";
    private const string Text      = "#0f172a";   // primary text (slate-900)
    private const string Muted     = "#64748b";   // muted text (slate-500)
    private const string Border    = "#e2e8f0";   // subtle dividers (slate-200)
    private const string RowAlt    = "#f8fafc";   // alternating row tint

    public async Task<byte[]> GenerateQuotationAsync(
        QuotationRequest req,
        QuotationApproval approval,
        User? signer = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Defensive reload — callers may not Include everything we need.
        var fullReq = await db.QuotationRequests
            .AsNoTracking()
            .Include(r => r.Customer)
            .Include(r => r.Branch)
            .Include(r => r.SalesPerson)
            .Include(r => r.Items.OrderBy(ri => ri.SortOrder))
                .ThenInclude(ri => ri.Item)
            .Include(r => r.Items)
                .ThenInclude(ri => ri.BomHeader!)
                    .ThenInclude(b => b.Cost!)
            .Include(r => r.Items)
                .ThenInclude(ri => ri.BomHeader!)
                    .ThenInclude(b => b.Lines)
            .FirstOrDefaultAsync(r => r.Id == req.Id) ?? req;

        // V3 cost lines (separate query — not on BomHeader nav).
        var bomHeaderIds = fullReq.Items
            .Where(ri => ri.BomHeader != null)
            .Select(ri => ri.BomHeader!.Id)
            .ToList();
        var costLinesByHeader = bomHeaderIds.Count == 0
            ? new Dictionary<int, List<BomCostLine>>()
            : await db.Set<BomCostLine>()
                .AsNoTracking()
                .Where(cl => bomHeaderIds.Contains(cl.BomHeaderId))
                .GroupBy(cl => cl.BomHeaderId)
                .ToDictionaryAsync(g => g.Key, g => g.ToList());

        // FX rates for any non-AED printing currency we encounter.
        var fxByCurrency = await db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.IsActive)
            .GroupBy(r => r.CurrencyCode)
            .Select(g => g.OrderByDescending(r => r.EffectiveDate).First())
            .ToDictionaryAsync(r => r.CurrencyCode, r => r.RateToAed);

        var approvalItemMap = approval.Items.ToDictionary(ai => ai.RequisitionItemId);
        // V3 reqs have at least one ApprovalItem.MarginPerKg set; V2.3 use
        // SalesPricePerKgAed only. Branch the price computation accordingly.
        var isV3 = approval.Items.Any(ai => ai.MarginPerKg.HasValue);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(40);
                page.MarginVertical(36);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial").FontColor(Text));

                // ── HEADER ────────────────────────────────────────────────────
                page.Header().Column(col =>
                {
                    col.Item().Height(4).Background(Brand);

                    col.Item().PaddingTop(14).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("FUJAIRAH PLASTIC FACTORY")
                                .Bold().FontSize(18).FontColor(BrandDark).LetterSpacing(0.02f);
                            c.Item().PaddingTop(2).Text(fullReq.Branch?.Name ?? "")
                                .FontSize(9.5f).FontColor(Brand);
                            c.Item().PaddingTop(1).Text("Fujairah, United Arab Emirates")
                                .FontSize(8.5f).FontColor(Muted);
                        });

                        row.ConstantItem(190)
                            .Border(1).BorderColor(Brand)
                            .Background(BrandSoft)
                            .Padding(12)
                            .Column(c =>
                            {
                                c.Item().AlignCenter().Text("SALES QUOTATION")
                                    .Bold().FontSize(13).FontColor(BrandDark).LetterSpacing(0.05f);
                                c.Item().PaddingTop(8).Table(t =>
                                {
                                    t.ColumnsDefinition(cd =>
                                    {
                                        cd.RelativeColumn();
                                        cd.RelativeColumn();
                                    });
                                    QuoteDetailRow(t, "Ref No:", fullReq.RefNo, bold: true, valueColor: Brand);
                                    QuoteDetailRow(t, "Date:", approval.ApprovedAt.ToString("dd MMM yyyy"));
                                    QuoteDetailRow(t, "Valid Until:", approval.ApprovedAt.AddDays(30).ToString("dd MMM yyyy"));
                                    QuoteDetailRow(t, "Currency:", fullReq.CurrencyCode);
                                });
                            });
                    });

                    col.Item().PaddingTop(12).LineHorizontal(0.75f).LineColor(Border);
                });

                // ── CONTENT ───────────────────────────────────────────────────
                page.Content().PaddingTop(16).Column(col =>
                {
                    // BILL TO / PREPARED BY
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            SectionHeader(c, "BILL TO");
                            c.Item().Border(0.75f).BorderColor(Border).Padding(11).Column(inner =>
                            {
                                inner.Item().Text(fullReq.Customer.Name)
                                    .Bold().FontSize(12).FontColor(BrandDark);
                                inner.Item().PaddingTop(2).Text(fullReq.Customer.Code)
                                    .FontSize(8.5f).FontColor(Muted);
                                if (!string.IsNullOrWhiteSpace(fullReq.Customer.Address))
                                    inner.Item().PaddingTop(5).Text(fullReq.Customer.Address)
                                        .FontSize(9).FontColor(Muted);
                                if (!string.IsNullOrWhiteSpace(fullReq.Customer.PhoneNumber))
                                    inner.Item().PaddingTop(2)
                                        .Text($"Tel: {fullReq.Customer.PhoneNumber}").FontSize(9).FontColor(Muted);
                                if (!string.IsNullOrWhiteSpace(fullReq.Customer.Email))
                                    inner.Item().PaddingTop(2)
                                        .Text($"Email: {fullReq.Customer.Email}").FontSize(9).FontColor(Muted);
                            });
                        });

                        row.ConstantItem(20);

                        row.RelativeItem().Column(c =>
                        {
                            SectionHeader(c, "PREPARED BY", accent: true);
                            c.Item().Border(0.75f).BorderColor(Border).Padding(11).Column(inner =>
                            {
                                inner.Item().Text(fullReq.SalesPerson?.Name ?? "—")
                                    .Bold().FontSize(11).FontColor(BrandDark);
                                inner.Item().PaddingTop(2).Text("Sales Representative")
                                    .FontSize(8.5f).FontColor(Muted);
                                if (signer is not null)
                                {
                                    inner.Item().PaddingTop(8).Text("Approved by").FontSize(8).FontColor(Muted);
                                    inner.Item().PaddingTop(1).Text(signer.Name)
                                        .FontSize(10).Bold().FontColor(Text);
                                    inner.Item().PaddingTop(1).Text("Managing Director")
                                        .FontSize(8.5f).FontColor(Muted);
                                }
                            });
                        });
                    });

                    // Items table
                    decimal grandTotal = 0;
                    col.Item().PaddingTop(20).Column(c =>
                    {
                        SectionHeader(c, "QUOTATION DETAILS");

                        c.Item().Table(t =>
                        {
                            t.ColumnsDefinition(cd =>
                            {
                                cd.ConstantColumn(28);   // #
                                cd.RelativeColumn(4);    // Description
                                cd.RelativeColumn(1.5f); // Qty
                                cd.RelativeColumn(0.8f); // Unit
                                cd.RelativeColumn(2);    // Unit Price
                                cd.RelativeColumn(2);    // Total
                            });

                            TableHeader(t, "#");
                            TableHeader(t, "Item Description");
                            TableHeader(t, "Quantity", alignRight: true);
                            TableHeader(t, "Unit");
                            TableHeader(t, $"Unit Price ({fullReq.CurrencyCode})", alignRight: true);
                            TableHeader(t, $"Total ({fullReq.CurrencyCode})", alignRight: true);

                            var rowNum = 0;
                            foreach (var ri in fullReq.Items.OrderBy(i => i.SortOrder))
                            {
                                if (!approvalItemMap.TryGetValue(ri.Id, out var ai)) continue;
                                rowNum++;

                                var unitPrice = isV3
                                    ? ComputeV3PricePerKg(ri, ai, approval, fullReq.CurrencyCode, fullReq.ExchangeRateSnapshot,
                                        costLinesByHeader, fxByCurrency)
                                    : (fullReq.CurrencyCode == "AED"
                                        ? ai.SalesPricePerKgAed
                                        : ai.SalesPricePerKgForeign ?? ai.SalesPricePerKgAed);

                                var lineTotal = unitPrice * ri.ExpectedQty;
                                grandTotal += lineTotal;

                                var bg = rowNum % 2 == 0 ? RowAlt : White;
                                TableCell(t, rowNum.ToString(), bg);
                                TableCell(t, ri.Item.Description, bg);
                                TableCell(t, ri.ExpectedQty.ToString("N0"), bg, alignRight: true);
                                TableCell(t, "kg", bg);
                                TableCell(t, unitPrice.ToString("N4"), bg, alignRight: true);
                                TableCell(t, lineTotal.ToString("N2"), bg, alignRight: true, bold: true);
                            }
                        });
                    });

                    // Total summary
                    col.Item().PaddingTop(8).AlignRight().Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(170);
                            cd.ConstantColumn(140);
                        });

                        t.Cell().Background(Brand).Padding(11)
                            .Text($"TOTAL AMOUNT ({fullReq.CurrencyCode})")
                            .Bold().FontSize(10).FontColor(White).LetterSpacing(0.04f);
                        t.Cell().Background(Brand).Padding(11).AlignRight()
                            .Text(grandTotal.ToString("N2"))
                            .Bold().FontSize(14).FontColor(White);
                    });

                    // Exchange rate disclosure (non-AED only)
                    if (fullReq.CurrencyCode != "AED")
                    {
                        var rate = approval.RateSnapshot ?? fullReq.ExchangeRateSnapshot;
                        if (rate is decimal r)
                        {
                            col.Item().PaddingTop(4).AlignRight()
                                .Text($"Exchange Rate: 1 {fullReq.CurrencyCode} = {r:N4} AED  (as of {approval.ApprovedAt:dd MMM yyyy})")
                                .FontSize(8).FontColor(Muted).Italic();
                        }
                    }

                    // Notes
                    if (!string.IsNullOrWhiteSpace(approval.Notes))
                    {
                        col.Item().PaddingTop(18).Column(c =>
                        {
                            SectionHeader(c, "NOTES");
                            c.Item().Border(0.75f).BorderColor(Border).Padding(10)
                                .Text(approval.Notes).FontSize(9).FontColor(Muted);
                        });
                    }

                    // Terms & Conditions
                    col.Item().PaddingTop(18).Column(c =>
                    {
                        SectionHeader(c, "TERMS & CONDITIONS");
                        c.Item().Border(0.75f).BorderColor(Border).Padding(11).Column(inner =>
                        {
                            var terms = new[]
                            {
                                "1. This quotation is valid for 30 days from the date of issue.",
                                "2. Prices are subject to change without prior notice after the validity period.",
                                "3. Payment terms as per mutually agreed contract.",
                                "4. Delivery: Ex-Works Fujairah unless otherwise agreed in writing.",
                                "5. All disputes are subject to the jurisdiction of UAE courts.",
                            };
                            foreach (var term in terms)
                                inner.Item().PaddingBottom(3).Text(term)
                                    .FontSize(8.5f).FontColor(Muted);
                        });
                    });

                    // Signature block (anchored to the bottom-right of content area)
                    col.Item().PaddingTop(28).AlignRight().Width(220).Column(sig =>
                    {
                        sig.Item().Text("AUTHORIZED SIGNATORY")
                            .Bold().FontSize(8.5f).FontColor(Muted).LetterSpacing(0.06f);

                        sig.Item().PaddingTop(8).Height(48).AlignCenter().Element(box =>
                        {
                            if (signer?.SignatureImage is { Length: > 0 })
                            {
                                box.Image(signer.SignatureImage).FitArea();
                            }
                            else
                            {
                                box.AlignBottom().AlignCenter().Text("(signature pending)")
                                    .FontSize(8).FontColor(Muted).Italic();
                            }
                        });

                        sig.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(BrandDark);
                        sig.Item().PaddingTop(4).AlignCenter().Text(signer?.Name ?? "—")
                            .Bold().FontSize(10).FontColor(Text);
                        sig.Item().PaddingTop(1).AlignCenter()
                            .Text("Managing Director").FontSize(8.5f).FontColor(Muted);
                        sig.Item().PaddingTop(0).AlignCenter()
                            .Text("Fujairah Plastic Factory").FontSize(8).FontColor(Muted);
                    });
                });

                // ── FOOTER ────────────────────────────────────────────────────
                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(10).LineHorizontal(0.5f).LineColor(Border);

                    col.Item().PaddingTop(6).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Fujairah Plastic Factory  ·  Fujairah, UAE")
                                .FontSize(8).FontColor(Muted);
                            c.Item().Text("info@fujairahplastic.com")
                                .FontSize(8).FontColor(Muted);
                        });

                        row.RelativeItem().AlignRight().AlignBottom().Text(text =>
                        {
                            text.Span("Page ").FontSize(8).FontColor(Muted);
                            text.CurrentPageNumber().FontSize(8).FontColor(Muted);
                            text.Span(" of ").FontSize(8).FontColor(Muted);
                            text.TotalPages().FontSize(8).FontColor(Muted);
                        });
                    });

                    col.Item().PaddingTop(8).Height(2).Background(Brand);
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// V3 per-KG price computation. Cost is summed in AED (BomCostLine.CostPerKgInAed
    /// is precomputed at costing-submit; printing cost is converted via active
    /// ExchangeRate when its currency differs from AED). For non-AED quotes the
    /// AED total is divided by approval.RateSnapshot (or ExchangeRateSnapshot
    /// fallback) before the margin is added.
    /// </summary>
    private static decimal ComputeV3PricePerKg(
        RequisitionItem ri,
        ApprovalItem ai,
        QuotationApproval approval,
        string quoteCurrency,
        decimal? requisitionRateSnapshot,
        IReadOnlyDictionary<int, List<BomCostLine>> costLinesByHeader,
        IReadOnlyDictionary<string, decimal> fxByCurrency)
    {
        var bom = ri.BomHeader;
        var cost = bom?.Cost;
        if (bom is null || cost is null)
            return ai.MarginPerKg ?? ai.SalesPricePerKgAed;

        decimal rmCostAed = 0m;
        if (costLinesByHeader.TryGetValue(bom.Id, out var lines))
            rmCostAed = lines.Sum(l => l.CostPerKgInAed);

        decimal printingCostAed = 0m;
        if (cost.PrintingCostPerKg.HasValue)
        {
            var printingCcy = cost.PrintingCostCurrency ?? "AED";
            if (printingCcy == "AED")
                printingCostAed = cost.PrintingCostPerKg.Value;
            else if (fxByCurrency.TryGetValue(printingCcy, out var rate) && rate > 0)
                printingCostAed = cost.PrintingCostPerKg.Value * rate;
            else
                printingCostAed = cost.PrintingCostPerKg.Value;
        }

        var totalCostAed = rmCostAed + printingCostAed
            + cost.FohPerKg + cost.TransportPerKg + cost.CommissionPerKg;

        decimal totalCostInQuoteCcy;
        if (quoteCurrency == "AED")
        {
            totalCostInQuoteCcy = totalCostAed;
        }
        else
        {
            var saleRate = approval.RateSnapshot ?? requisitionRateSnapshot ?? 1m;
            totalCostInQuoteCcy = saleRate > 0 ? totalCostAed / saleRate : totalCostAed;
        }

        return totalCostInQuoteCcy + (ai.MarginPerKg ?? 0m);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SectionHeader(QuestPDF.Fluent.ColumnDescriptor col, string title, bool accent = false)
    {
        col.Item()
            .Background(accent ? Brand : BrandDark)
            .PaddingHorizontal(8).PaddingVertical(5)
            .Text(title).Bold().FontSize(8.5f).FontColor(White).LetterSpacing(0.05f);
    }

    private static void TableHeader(QuestPDF.Fluent.TableDescriptor t, string text, bool alignRight = false)
    {
        var cell = t.Cell()
            .Background(Brand)
            .PaddingHorizontal(8).PaddingVertical(7);
        var container = alignRight ? cell.AlignRight() : cell;
        container.Text(text).Bold().FontSize(8.5f).FontColor(White).LetterSpacing(0.04f);
    }

    private static void TableCell(QuestPDF.Fluent.TableDescriptor t, string text, string background,
        bool alignRight = false, bool bold = false)
    {
        var cell = t.Cell()
            .Background(background)
            .BorderBottom(0.4f).BorderColor(Border)
            .PaddingHorizontal(8).PaddingVertical(7);

        var container = alignRight ? cell.AlignRight() : cell;
        var textEl = container.Text(text).FontSize(9.5f);
        if (bold) textEl.Bold();
    }

    private static void QuoteDetailRow(QuestPDF.Fluent.TableDescriptor t, string label, string value,
        bool bold = false, string? valueColor = null)
    {
        t.Cell().PaddingVertical(2).Text(label).FontSize(9).FontColor(Muted);
        var textEl = t.Cell().PaddingVertical(2).Text(value).FontSize(9);
        if (bold) textEl.Bold();
        if (valueColor != null) textEl.FontColor(valueColor);
    }
}
