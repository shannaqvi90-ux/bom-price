using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BomPriceApproval.API.Infrastructure.Services;

public class PdfService(AppDbContext db)
{
    // Brand palette
    private const string Navy      = "#1B3A5C";
    private const string Blue      = "#2E86C1";
    private const string LightBlue = "#EBF5FB";
    private const string White     = "#FFFFFF";
    private const string TextDark  = "#2C3E50";
    private const string TextGrey  = "#7F8C8D";
    private const string Border    = "#D5D8DC";

    public byte[] GenerateQuotation(QuotationRequest req, QuotationApproval approval)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var approvalItemMap = approval.Items.ToDictionary(ai => ai.RequisitionItemId);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(36);
                page.MarginVertical(36);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial").FontColor(TextDark));

                // ── HEADER ────────────────────────────────────────────────────
                page.Header().Column(col =>
                {
                    // Top accent bar
                    col.Item().Height(6).Background(Navy);

                    col.Item().PaddingTop(14).Row(row =>
                    {
                        // Company branding
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("FUJAIRAH PLASTIC FACTORY")
                                .Bold().FontSize(22).FontColor(Navy);
                            c.Item().PaddingTop(2).Text(req.Branch?.Name ?? "")
                                .FontSize(10).FontColor(Blue);
                            c.Item().PaddingTop(1).Text("Fujairah, United Arab Emirates")
                                .FontSize(9).FontColor(TextGrey);
                        });

                        // Quotation identity box
                        row.ConstantItem(190)
                            .Border(1.5f).BorderColor(Navy)
                            .Background(LightBlue)
                            .Padding(12)
                            .Column(c =>
                            {
                                c.Item().AlignCenter().Text("SALES QUOTATION")
                                    .Bold().FontSize(15).FontColor(Navy);
                                c.Item().PaddingTop(8).Table(t =>
                                {
                                    t.ColumnsDefinition(cd =>
                                    {
                                        cd.RelativeColumn();
                                        cd.RelativeColumn();
                                    });
                                    QuoteDetailRow(t, "Ref No:", req.RefNo, bold: true, valueColor: Blue);
                                    QuoteDetailRow(t, "Date:", approval.ApprovedAt.ToString("dd MMM yyyy"));
                                    QuoteDetailRow(t, "Valid Until:", approval.ApprovedAt.AddDays(30).ToString("dd MMM yyyy"));
                                    QuoteDetailRow(t, "Currency:", req.CurrencyCode);
                                });
                            });
                    });

                    col.Item().PaddingTop(14).LineHorizontal(2).LineColor(Navy);
                });

                // ── CONTENT ───────────────────────────────────────────────────
                page.Content().PaddingTop(18).Column(col =>
                {
                    // Bill To / Prepared By row
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            SectionHeader(c, "BILL TO");
                            c.Item().Border(1).BorderColor(Border).Padding(12).Column(inner =>
                            {
                                inner.Item().Text(req.Customer.Name)
                                    .Bold().FontSize(13).FontColor(Navy);
                                if (!string.IsNullOrWhiteSpace(req.Customer.Address))
                                    inner.Item().PaddingTop(4).Text(req.Customer.Address)
                                        .FontColor(TextGrey);
                                if (!string.IsNullOrWhiteSpace(req.Customer.PhoneNumber))
                                    inner.Item().PaddingTop(3)
                                        .Text($"Tel: {req.Customer.PhoneNumber}").FontColor(TextGrey);
                                if (!string.IsNullOrWhiteSpace(req.Customer.Email))
                                    inner.Item().PaddingTop(3)
                                        .Text($"Email: {req.Customer.Email}").FontColor(TextGrey);
                            });
                        });

                        row.ConstantItem(20);

                        row.RelativeItem().Column(c =>
                        {
                            SectionHeader(c, "PREPARED BY", accent: true);
                            c.Item().Border(1).BorderColor(Border).Padding(12).Column(inner =>
                            {
                                inner.Item().Text(req.SalesPerson?.Name ?? "—")
                                    .Bold().FontSize(12).FontColor(Navy);
                                inner.Item().PaddingTop(3).Text("Sales Representative")
                                    .FontColor(TextGrey);
                                inner.Item().PaddingTop(8).Text("Approved By").Bold().FontSize(9);
                                inner.Item().PaddingTop(2).Text("Eng. Khaled — Managing Director")
                                    .FontColor(TextGrey);
                            });
                        });
                    });

                    // Items table
                    decimal grandTotal = 0;
                    col.Item().PaddingTop(22).Column(c =>
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

                            // Header row
                            TableHeader(t, "#");
                            TableHeader(t, "Item Description");
                            TableHeader(t, "Quantity");
                            TableHeader(t, "Unit");
                            TableHeader(t, $"Unit Price ({req.CurrencyCode})");
                            TableHeader(t, $"Total ({req.CurrencyCode})");

                            // Data rows
                            var rowNum = 0;
                            foreach (var ri in req.Items.OrderBy(i => i.SortOrder))
                            {
                                rowNum++;
                                if (!approvalItemMap.TryGetValue(ri.Id, out var ai)) continue;
                                var unitPrice = req.CurrencyCode == "AED"
                                    ? ai.SalesPricePerKgAed
                                    : ai.SalesPricePerKgForeign ?? ai.SalesPricePerKgAed;
                                var lineTotal = unitPrice * ri.ExpectedQty;
                                grandTotal += lineTotal;

                                TableCell(t, rowNum.ToString());
                                TableCell(t, ri.Item.Description);
                                TableCell(t, ri.ExpectedQty.ToString("N0"), alignRight: true);
                                TableCell(t, "kg");
                                TableCell(t, unitPrice.ToString("N4"), alignRight: true);
                                TableCell(t, lineTotal.ToString("N2"), alignRight: true, bold: true);
                            }
                        });
                    });

                    // Total summary box
                    col.Item().PaddingTop(6).AlignRight().Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(160);
                            cd.ConstantColumn(130);
                        });

                        t.Cell().Background(Navy).Padding(10)
                            .Text($"TOTAL AMOUNT ({req.CurrencyCode})")
                            .Bold().FontSize(10).FontColor(White);
                        t.Cell().Background(Navy).Padding(10).AlignRight()
                            .Text(grandTotal.ToString("N2"))
                            .Bold().FontSize(14).FontColor(White);
                    });

                    // Exchange rate note
                    if (req.CurrencyCode != "AED" && req.ExchangeRateSnapshot.HasValue)
                    {
                        col.Item().PaddingTop(5).AlignRight()
                            .Text($"Exchange Rate: 1 {req.CurrencyCode} = {req.ExchangeRateSnapshot:N4} AED  (as of {approval.ApprovedAt:dd MMM yyyy})")
                            .FontSize(8).FontColor(TextGrey).Italic();
                    }

                    // Notes (only if present)
                    if (!string.IsNullOrWhiteSpace(approval.Notes))
                    {
                        col.Item().PaddingTop(20).Column(c =>
                        {
                            SectionHeader(c, "NOTES");
                            c.Item().Border(1).BorderColor(Border).Padding(10)
                                .Text(approval.Notes).FontSize(9).FontColor(TextGrey);
                        });
                    }

                    // Terms & Conditions
                    col.Item().PaddingTop(20).Column(c =>
                    {
                        SectionHeader(c, "TERMS & CONDITIONS");
                        c.Item().Border(1).BorderColor(Border).Padding(10).Column(inner =>
                        {
                            var terms = new[]
                            {
                                "1. This quotation is valid for 30 days from the date of issue.",
                                "2. Prices are subject to change without prior notice after the validity period.",
                                "3. Payment terms as per mutually agreed contract.",
                                "4. Delivery: Ex-Works Fujairah unless otherwise agreed in writing.",
                                "5. All disputes are subject to the jurisdiction of Fujairah courts.",
                            };
                            foreach (var term in terms)
                                inner.Item().PaddingBottom(3).Text(term)
                                    .FontSize(8.5f).FontColor(TextGrey);
                        });
                    });
                });

                // ── FOOTER ────────────────────────────────────────────────────
                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(16).LineHorizontal(1).LineColor(Border);

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        // Signature block
                        row.RelativeItem(2).Column(sig =>
                        {
                            sig.Item().Text("AUTHORIZED SIGNATORY")
                                .FontSize(8).FontColor(TextGrey).Bold();
                            sig.Item().PaddingTop(22).Width(150)
                                .LineHorizontal(1).LineColor(TextDark);
                            sig.Item().PaddingTop(4).Text("Eng. Khaled").Bold().FontSize(10);
                            sig.Item().PaddingTop(1).Text("Managing Director — Fujairah Plastic Factory")
                                .FontSize(8.5f).FontColor(TextGrey);
                        });

                        // Page number
                        row.RelativeItem(1).AlignRight().AlignBottom().Column(c =>
                        {
                            c.Item().Text(text =>
                            {
                                text.Span("Page ").FontSize(8).FontColor(TextGrey);
                                text.CurrentPageNumber().FontSize(8).FontColor(TextGrey);
                                text.Span(" of ").FontSize(8).FontColor(TextGrey);
                                text.TotalPages().FontSize(8).FontColor(TextGrey);
                            });
                        });
                    });

                    col.Item().PaddingTop(10).Height(5).Background(Navy);
                });
            });
        }).GeneratePdf();
    }

    // V3 — Stage 2B signed quotation PDF with embedded MD signature.
    public async Task<byte[]> GenerateSignedQuotationAsync(QuotationRequest req, QuotationApproval approval, User signer)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        // Defensive reload: callers (FinalSign) only Include Customer on `req`.
        // We need Items + BomHeader + Cost + Lines + CostLines + SalesPerson here.
        // Re-query rather than relying on lazy nav properties that aren't enabled.
        var fullReq = await db.QuotationRequests
            .AsNoTracking()
            .Include(r => r.Customer)
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

        // Cost lines need a separate query (BomCostLine isn't on BomHeader nav).
        var bomHeaderIds = fullReq.Items
            .Where(ri => ri.BomHeader != null)
            .Select(ri => ri.BomHeader!.Id)
            .ToList();
        var costLinesByHeader = await db.Set<BomCostLine>()
            .AsNoTracking()
            .Where(cl => bomHeaderIds.Contains(cl.BomHeaderId))
            .GroupBy(cl => cl.BomHeaderId)
            .ToDictionaryAsync(g => g.Key, g => g.ToList());

        // FX rates for any non-AED printing currency we encounter.
        // Cache active rates once so we don't query in a loop.
        var fxByCurrency = await db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.IsActive)
            .GroupBy(r => r.CurrencyCode)
            .Select(g => g.OrderByDescending(r => r.EffectiveDate).First())
            .ToDictionaryAsync(r => r.CurrencyCode, r => r.RateToAed);

        var approvalItemMap = approval.Items.ToDictionary(ai => ai.RequisitionItemId);

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(11));

                page.Header().Column(c =>
                {
                    c.Item().Text("FUJAIRAH PLASTIC FACTORY")
                        .FontSize(16).Bold().FontColor("#1e40af");
                    c.Item().Text($"Quotation {fullReq.RefNo} · {DateTime.UtcNow:yyyy-MM-dd}")
                        .FontSize(9);
                });

                page.Content().Column(c =>
                {
                    c.Item().PaddingTop(10).Text(t =>
                    {
                        t.Span("Customer: ").Bold();
                        t.Span($"{fullReq.Customer.Name} ({fullReq.Customer.Code})");
                    });
                    c.Item().Text(t =>
                    {
                        t.Span("Currency: ").Bold();
                        t.Span(fullReq.CurrencyCode);
                    });

                    c.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(col =>
                        {
                            col.RelativeColumn(3);
                            col.RelativeColumn(1);
                            col.RelativeColumn(1);
                            col.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Background("#f1f5f9").Padding(4).Text("Item").Bold();
                            h.Cell().Background("#f1f5f9").Padding(4).Text("Qty").Bold();
                            h.Cell().Background("#f1f5f9").Padding(4).Text("Price/KG").Bold();
                            h.Cell().Background("#f1f5f9").Padding(4).Text("Total").Bold();
                        });

                        foreach (var ri in fullReq.Items.OrderBy(i => i.SortOrder))
                        {
                            if (!approvalItemMap.TryGetValue(ri.Id, out var ai)) continue;

                            var pricePerKg = ComputePricePerKg(
                                ri, ai, approval, fullReq.CurrencyCode,
                                costLinesByHeader, fxByCurrency);
                            var lineTotal = pricePerKg * ri.ExpectedQty;

                            table.Cell().Padding(4).Text(ri.Item.Description);
                            table.Cell().Padding(4).Text($"{ri.ExpectedQty:N0}");
                            table.Cell().Padding(4).Text($"{pricePerKg:F2}");
                            table.Cell().Padding(4).Text($"{lineTotal:F2}");
                        }
                    });

                    // Signature block
                    c.Item().PaddingTop(40).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Prepared by").FontSize(8).FontColor("#64748b");
                            col.Item().Text(fullReq.SalesPerson?.Name ?? "—").FontSize(10);
                        });

                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("Approved & Signed by MD").FontSize(8).FontColor("#64748b");

                            if (signer.SignatureImage is { Length: > 0 })
                            {
                                col.Item().Height(50).Image(signer.SignatureImage);
                            }
                            else
                            {
                                col.Item().Text("[no signature uploaded]")
                                    .Italic().FontColor("#94a3b8");
                            }

                            col.Item().Text($"{signer.Name}, Managing Director").FontSize(9);
                            col.Item().Text($"{approval.ApprovedAt:yyyy-MM-dd}")
                                .FontSize(8).FontColor("#64748b");
                        });
                    });
                });
            });
        }).GeneratePdf();

        return pdfBytes;
    }

    /// <summary>
    /// Computes the per-KG price for a single FG line in the quote currency.
    /// Cost is summed in AED (BomCostLine.CostPerKgInAed is precomputed at
    /// costing-submit; printing cost is converted via active ExchangeRate when
    /// its currency differs from AED). For non-AED quotes the AED total is
    /// divided by approval.RateSnapshot (or ExchangeRateSnapshot fallback)
    /// before the margin is added.
    /// </summary>
    private static decimal ComputePricePerKg(
        RequisitionItem ri,
        ApprovalItem ai,
        QuotationApproval approval,
        string quoteCurrency,
        IReadOnlyDictionary<int, List<BomCostLine>> costLinesByHeader,
        IReadOnlyDictionary<string, decimal> fxByCurrency)
    {
        var bom = ri.BomHeader;
        var cost = bom?.Cost;
        if (bom is null || cost is null)
            return ai.MarginPerKg ?? ai.SalesPricePerKgAed;

        // Sum RM cost lines (already in AED — converted at costing submit).
        decimal rmCostAed = 0m;
        if (costLinesByHeader.TryGetValue(bom.Id, out var lines))
            rmCostAed = lines.Sum(l => l.CostPerKgInAed);

        // Printing cost — convert to AED if a non-AED currency was specified.
        decimal printingCostAed = 0m;
        if (cost.PrintingCostPerKg.HasValue)
        {
            var printingCcy = cost.PrintingCostCurrency ?? "AED";
            if (printingCcy == "AED")
            {
                printingCostAed = cost.PrintingCostPerKg.Value;
            }
            else if (fxByCurrency.TryGetValue(printingCcy, out var rate) && rate > 0)
            {
                printingCostAed = cost.PrintingCostPerKg.Value * rate;
            }
            else
            {
                // No active rate — treat as AED to avoid silently zeroing the
                // cost. Best-effort fallback; manual review on the PDF will
                // catch any oddity.
                printingCostAed = cost.PrintingCostPerKg.Value;
            }
        }

        var totalCostAed = rmCostAed + printingCostAed
            + cost.FohPerKg + cost.TransportPerKg + cost.CommissionPerKg;

        // Convert AED cost to quote currency (margin is already in quote currency — D6).
        decimal totalCostInQuoteCcy;
        if (quoteCurrency == "AED")
        {
            totalCostInQuoteCcy = totalCostAed;
        }
        else
        {
            var saleRate = approval.RateSnapshot
                ?? ri.QuotationRequest?.ExchangeRateSnapshot
                ?? 1m;
            totalCostInQuoteCcy = saleRate > 0 ? totalCostAed / saleRate : totalCostAed;
        }

        var margin = ai.MarginPerKg ?? 0m;
        return totalCostInQuoteCcy + margin;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SectionHeader(ColumnDescriptor col, string title, bool accent = false)
    {
        col.Item()
            .Background(accent ? Blue : Navy)
            .PaddingHorizontal(8).PaddingVertical(5)
            .Text(title).Bold().FontSize(9).FontColor(White);
    }

    private static void TableHeader(TableDescriptor t, string text)
    {
        t.Cell()
            .Background(Blue)
            .PaddingHorizontal(8).PaddingVertical(7)
            .Text(text).Bold().FontSize(9).FontColor(White);
    }

    private static void TableCell(TableDescriptor t, string text,
        bool alignRight = false, bool bold = false)
    {
        var cell = t.Cell()
            .BorderBottom(0.5f).BorderColor(Border)
            .PaddingHorizontal(8).PaddingVertical(8);

        var container = alignRight ? cell.AlignRight() : cell;
        var textEl = container.Text(text).FontSize(10);
        if (bold) textEl.Bold();
    }

    private static void QuoteDetailRow(TableDescriptor t, string label, string value,
        bool bold = false, string? valueColor = null)
    {
        t.Cell().PaddingVertical(2).Text(label).FontSize(9).FontColor(TextGrey);
        var textEl = t.Cell().PaddingVertical(2).Text(value).FontSize(9);
        if (bold) textEl.Bold();
        if (valueColor != null) textEl.FontColor(valueColor);
    }
}
