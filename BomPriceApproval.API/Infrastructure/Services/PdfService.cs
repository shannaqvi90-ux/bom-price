using BomPriceApproval.API.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BomPriceApproval.API.Infrastructure.Services;

public class PdfService
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

        var displayPrice = req.CurrencyCode == "AED"
            ? approval.SalesPricePerKgAed
            : approval.SalesPricePerKgForeign ?? approval.SalesPricePerKgAed;
        var totalPrice = displayPrice * req.ExpectedQty;

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

                            // Data row
                            TableCell(t, "1");
                            TableCell(t, req.Item.Description);
                            TableCell(t, req.ExpectedQty.ToString("N0"), alignRight: true);
                            TableCell(t, "kg");
                            TableCell(t, displayPrice.ToString("N4"), alignRight: true);
                            TableCell(t, totalPrice.ToString("N2"), alignRight: true, bold: true);
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
                            .Text(totalPrice.ToString("N2"))
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
