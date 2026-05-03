using BomPriceApproval.API.Domain.Entities;
using BomPriceApproval.API.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BomPriceApproval.API.Infrastructure.Services;

/// <summary>
/// Letterhead Classic quotation PDF generator. Reads admin-editable
/// CompanySettings (singleton row id=1) for letterhead text + validity + T&amp;C.
/// Salesperson email shown next to Bill-To. Single MD signature only — no
/// salesperson signature, no footer, no Notes section.
/// </summary>
public class PdfService(AppDbContext db)
{
    private const string BrandDark = "#1e3a8a";
    private const string Text      = "#0f172a";
    private const string Muted     = "#475569";
    private const string Faint     = "#94a3b8";

    public async Task<byte[]> GenerateQuotationAsync(
        QuotationRequest req,
        QuotationApproval approval,
        User? signer = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

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

        var fxByCurrency = await db.ExchangeRates
            .AsNoTracking()
            .Where(r => r.IsActive)
            .GroupBy(r => r.CurrencyCode)
            .Select(g => g.OrderByDescending(r => r.EffectiveDate).First())
            .ToDictionaryAsync(r => r.CurrencyCode, r => r.RateToAed);

        var settings = await db.CompanySettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == 1) ?? DefaultSettings();

        var approvalItemMap = approval.Items.ToDictionary(ai => ai.RequisitionItemId);
        var isV3 = approval.Items.Any(ai => ai.MarginPerKg.HasValue);

        var validUntil = approval.ApprovedAt.AddDays(settings.QuotationValidityDays);
        var termsList = (settings.TermsAndConditions ?? "")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginHorizontal(36);
                page.MarginVertical(32);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Times New Roman").FontColor(Text));

                page.Content().Column(col =>
                {
                    // ── LETTERHEAD ────────────────────────────────────────
                    col.Item().AlignCenter().Text(settings.CompanyName)
                        .Bold().FontSize(22).FontColor(BrandDark).LetterSpacing(0.04f);

                    if (!string.IsNullOrWhiteSpace(settings.Address))
                        col.Item().PaddingTop(4).AlignCenter().Text(settings.Address)
                            .FontSize(10).FontColor(Muted);

                    var contactParts = new List<string>();
                    if (!string.IsNullOrWhiteSpace(settings.Telephone)) contactParts.Add($"Tel: {settings.Telephone}");
                    if (!string.IsNullOrWhiteSpace(settings.Trn))       contactParts.Add($"TRN: {settings.Trn}");
                    if (!string.IsNullOrWhiteSpace(settings.Email))     contactParts.Add(settings.Email);
                    if (!string.IsNullOrWhiteSpace(settings.Website))   contactParts.Add(settings.Website);
                    if (contactParts.Count > 0)
                        col.Item().PaddingTop(2).AlignCenter()
                            .Text(string.Join("  ·  ", contactParts))
                            .FontSize(9.5f).FontColor(Muted);

                    col.Item().PaddingTop(10).LineHorizontal(2f).LineColor(BrandDark);

                    // ── TITLE ─────────────────────────────────────────────
                    col.Item().PaddingTop(14).AlignCenter().Text("SALES QUOTATION")
                        .Bold().FontSize(14).FontColor(Text).LetterSpacing(0.18f);
                    col.Item().PaddingTop(2).AlignCenter().Container().Width(160).LineHorizontal(0.5f).LineColor(Text);

                    // ── META STRIP ────────────────────────────────────────
                    col.Item().PaddingTop(10).PaddingBottom(6).BorderBottom(0.5f).BorderColor(Faint).Row(row =>
                    {
                        MetaPair(row.RelativeItem(), "Ref:", fullReq.RefNo);
                        MetaPair(row.RelativeItem(), "Date:", approval.ApprovedAt.ToString("dd MMM yyyy"));
                        MetaPair(row.RelativeItem(), "Valid Until:", validUntil.ToString("dd MMM yyyy"));
                        MetaPair(row.RelativeItem(), "Currency:", fullReq.CurrencyCode);
                    });

                    // ── BILL TO + SALES REP ───────────────────────────────
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            PartyHeader(c, "Bill To");
                            c.Item().PaddingTop(2).Text(fullReq.Customer.Name)
                                .Bold().FontSize(12).FontColor(Text);
                            c.Item().PaddingTop(1).Text(fullReq.Customer.Code).FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(fullReq.Customer.Address))
                                c.Item().PaddingTop(1).Text(fullReq.Customer.Address).FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(fullReq.Customer.PhoneNumber))
                                c.Item().PaddingTop(1).Text($"Tel: {fullReq.Customer.PhoneNumber}").FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(fullReq.Customer.Email))
                                c.Item().PaddingTop(1).Text(fullReq.Customer.Email).FontSize(9.5f).FontColor(Muted);
                        });
                        row.ConstantItem(22);
                        row.RelativeItem().Column(c =>
                        {
                            PartyHeader(c, "Sales Representative");
                            c.Item().PaddingTop(2).Text(fullReq.SalesPerson?.Name ?? "—")
                                .Bold().FontSize(12).FontColor(Text);
                            c.Item().PaddingTop(1).Text("Sales Department").FontSize(9.5f).FontColor(Muted);
                            if (!string.IsNullOrWhiteSpace(fullReq.SalesPerson?.Email))
                                c.Item().PaddingTop(1).Text(fullReq.SalesPerson!.Email).FontSize(9.5f).FontColor(Muted);
                        });
                    });

                    // ── SALUTATION ────────────────────────────────────────
                    col.Item().PaddingTop(12).Text(
                        "Dear Sir/Madam, with reference to your enquiry, we are pleased to submit our quotation as below:")
                        .FontSize(10).FontColor(Text);

                    // ── ITEMS TABLE ───────────────────────────────────────
                    decimal grandTotal = 0;
                    col.Item().PaddingTop(10).Table(t =>
                    {
                        t.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(34);
                            cd.RelativeColumn(4);
                            cd.RelativeColumn(1.5f);
                            cd.RelativeColumn(1.7f);
                            cd.RelativeColumn(2);
                        });

                        ItemsTableHeader(t, "S.No");
                        ItemsTableHeader(t, "Description");
                        ItemsTableHeader(t, "Qty (kg)", alignRight: true);
                        ItemsTableHeader(t, $"Rate ({fullReq.CurrencyCode})", alignRight: true);
                        ItemsTableHeader(t, $"Amount ({fullReq.CurrencyCode})", alignRight: true);

                        var rowNum = 0;
                        foreach (var ri in fullReq.Items.OrderBy(i => i.SortOrder))
                        {
                            if (!approvalItemMap.TryGetValue(ri.Id, out var ai)) continue;
                            rowNum++;

                            var unitPrice = isV3
                                ? ComputeV3PricePerKg(ri, ai, approval, fullReq.CurrencyCode,
                                    fullReq.ExchangeRateSnapshot, costLinesByHeader, fxByCurrency)
                                : (fullReq.CurrencyCode == "AED"
                                    ? ai.SalesPricePerKgAed
                                    : ai.SalesPricePerKgForeign ?? ai.SalesPricePerKgAed);

                            var lineTotal = unitPrice * ri.ExpectedQty;
                            grandTotal += lineTotal;

                            ItemsTableCell(t, rowNum.ToString());
                            ItemsTableCell(t, ri.Item.Description);
                            ItemsTableCell(t, ri.ExpectedQty.ToString("N0"), alignRight: true);
                            ItemsTableCell(t, unitPrice.ToString("N4"), alignRight: true);
                            ItemsTableCell(t, lineTotal.ToString("N2"), alignRight: true);
                        }
                    });

                    // ── TOTAL ─────────────────────────────────────────────
                    col.Item().PaddingTop(2)
                        .BorderTop(1.5f).BorderBottom(1.5f).BorderColor(BrandDark)
                        .PaddingVertical(8).PaddingHorizontal(6).Row(row =>
                    {
                        row.RelativeItem().Text("TOTAL AMOUNT")
                            .Bold().FontSize(12).FontColor(BrandDark);
                        row.ConstantItem(160).AlignRight()
                            .Text($"{fullReq.CurrencyCode}  {grandTotal:N2}")
                            .Bold().FontSize(12).FontColor(BrandDark);
                    });

                    // Exchange rate disclosure (non-AED)
                    if (fullReq.CurrencyCode != "AED")
                    {
                        var rate = approval.RateSnapshot ?? fullReq.ExchangeRateSnapshot;
                        if (rate is decimal r)
                        {
                            col.Item().PaddingTop(4).AlignRight()
                                .Text($"Exchange Rate: 1 {fullReq.CurrencyCode} = {r:N4} AED  (as of {approval.ApprovedAt:dd MMM yyyy})")
                                .FontSize(9).FontColor(Muted).Italic();
                        }
                    }

                    // ── TERMS & CONDITIONS ────────────────────────────────
                    if (termsList.Count > 0)
                    {
                        col.Item().PaddingTop(16).Text("Terms & Conditions:")
                            .Bold().FontSize(10).FontColor(Text).Underline();
                        col.Item().PaddingTop(4).Column(tc =>
                        {
                            for (int i = 0; i < termsList.Count; i++)
                            {
                                tc.Item().PaddingBottom(2).Text($"{i + 1}. {termsList[i]}")
                                    .FontSize(9.5f).FontColor("#334155");
                            }
                        });
                    }

                    // ── SIGNATURE (MD only, right-aligned) ────────────────
                    col.Item().PaddingTop(28).AlignRight().Width(220).Column(sig =>
                    {
                        sig.Item().Height(48).AlignCenter().Element(box =>
                        {
                            if (signer?.SignatureImage is { Length: > 0 })
                                box.Image(signer.SignatureImage).FitArea();
                            else
                                box.AlignBottom().AlignCenter().Text("(signature pending)")
                                    .FontSize(9).FontColor(Faint).Italic();
                        });
                        sig.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Text);
                        sig.Item().PaddingTop(4).AlignCenter()
                            .Text($"For {settings.CompanyName}").Bold().FontSize(11).FontColor(Text);
                        sig.Item().PaddingTop(1).AlignCenter()
                            .Text("Authorized Signatory · Managing Director").FontSize(9).FontColor(Muted).Italic();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static CompanySettings DefaultSettings() => new()
    {
        Id = 1,
        CompanyName = "FUJAIRAH PLASTIC FACTORY",
        Address = "Fujairah, United Arab Emirates",
        Telephone = "",
        Trn = "",
        Email = "info@fujairahplastic.com",
        Website = "",
        QuotationValidityDays = 30,
        TermsAndConditions = "",
    };

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
            totalCostInQuoteCcy = totalCostAed;
        else
        {
            var saleRate = approval.RateSnapshot ?? requisitionRateSnapshot ?? 1m;
            totalCostInQuoteCcy = saleRate > 0 ? totalCostAed / saleRate : totalCostAed;
        }

        return totalCostInQuoteCcy + (ai.MarginPerKg ?? 0m);
    }

    private static void MetaPair(QuestPDF.Infrastructure.IContainer box, string label, string value)
    {
        box.Text(text =>
        {
            text.Span($"{label} ").FontSize(10).FontColor(Muted);
            text.Span(value).FontSize(10).Bold().FontColor(Text);
        });
    }

    private static void PartyHeader(QuestPDF.Fluent.ColumnDescriptor col, string title)
    {
        col.Item().BorderBottom(0.5f).BorderColor("#cbd5e1").PaddingBottom(3)
            .Text(title.ToUpperInvariant())
            .Bold().FontSize(9).FontColor(Muted).LetterSpacing(0.18f);
    }

    private static void ItemsTableHeader(QuestPDF.Fluent.TableDescriptor t, string text, bool alignRight = false)
    {
        var cell = t.Cell()
            .BorderTop(1).BorderBottom(1).BorderColor(BrandDark)
            .PaddingHorizontal(6).PaddingVertical(6);
        var container = alignRight ? cell.AlignRight() : cell;
        container.Text(text).Bold().FontSize(9.5f).FontColor(Text);
    }

    private static void ItemsTableCell(QuestPDF.Fluent.TableDescriptor t, string text, bool alignRight = false)
    {
        var cell = t.Cell()
            .BorderBottom(0.5f).BorderColor(Faint)
            .PaddingHorizontal(6).PaddingVertical(6);
        var container = alignRight ? cell.AlignRight() : cell;
        container.Text(text).FontSize(10).FontColor(Text);
    }
}
