using BomPriceApproval.API.Domain.Entities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BomPriceApproval.API.Infrastructure.Services;

public class PdfService
{
    public byte[] GenerateQuotation(QuotationRequest req, QuotationApproval approval)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial"));

                page.Header().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("FUJAIRAH PLASTIC FACTORY").Bold().FontSize(16);
                            c.Item().Text(req.Branch.Name).FontSize(11).FontColor(Colors.Grey.Medium);
                        });
                        row.ConstantItem(150).AlignRight().Column(c =>
                        {
                            c.Item().Text("SALES QUOTATION").Bold().FontSize(13);
                            c.Item().Text(req.RefNo).FontColor(Colors.Blue.Medium);
                            c.Item().Text(approval.ApprovedAt.ToString("dd/MM/yyyy"));
                        });
                    });
                    col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten3);
                });

                page.Content().PaddingTop(16).Column(col =>
                {
                    // Customer details
                    col.Item().Background(Colors.Grey.Lighten3).Padding(10).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                        t.Cell().Text("Customer:").Bold();
                        t.Cell().Text(req.Customer.Name);
                        t.Cell().Text("Address:");
                        t.Cell().Text(req.Customer.Address);
                        t.Cell().Text("Phone:");
                        t.Cell().Text(req.Customer.PhoneNumber);
                        t.Cell().Text("Email:");
                        t.Cell().Text(req.Customer.Email);
                    });

                    col.Item().PaddingTop(16).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1.5f);
                        });

                        t.Header(h =>
                        {
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).Text("Item Description").Bold().FontColor(Colors.White);
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("Qty").Bold().FontColor(Colors.White);
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text("Unit").Bold().FontColor(Colors.White);
                            h.Cell().Background(Colors.Blue.Darken3).Padding(6).AlignRight().Text($"Unit Price ({req.CurrencyCode})").Bold().FontColor(Colors.White);
                        });

                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(6).Text(req.Item.Description);
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(6).AlignRight().Text(req.ExpectedQty.ToString("N0"));
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(6).AlignRight().Text("kg");

                        var displayPrice = req.CurrencyCode == "AED"
                            ? approval.SalesPricePerKgAed
                            : approval.SalesPricePerKgForeign ?? approval.SalesPricePerKgAed;
                        t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten3).Padding(6).AlignRight()
                            .Text($"{displayPrice:N4}");
                    });

                    col.Item().PaddingTop(8).AlignRight().Column(c =>
                    {
                        var displayPrice = req.CurrencyCode == "AED"
                            ? approval.SalesPricePerKgAed
                            : approval.SalesPricePerKgForeign ?? approval.SalesPricePerKgAed;
                        var totalPrice = displayPrice * req.ExpectedQty;
                        c.Item().Text($"Total Price ({req.CurrencyCode}): {totalPrice:N2}").Bold().FontSize(12);

                        if (req.CurrencyCode != "AED" && req.ExchangeRateSnapshot.HasValue)
                            c.Item().PaddingTop(4).Text($"Exchange Rate: 1 {req.CurrencyCode} = {req.ExchangeRateSnapshot:N4} AED (as of {approval.ApprovedAt:dd/MM/yyyy})")
                                .FontColor(Colors.Grey.Medium).FontSize(9);
                    });

                    col.Item().PaddingTop(24).Text("Valid for 30 days from date of issue.").FontColor(Colors.Grey.Medium).Italic();
                });

                page.Footer().AlignRight().Column(c =>
                {
                    c.Item().PaddingTop(16).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten3);
                    c.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(160).Column(sig =>
                        {
                            sig.Item().Text("Authorized by: Eng Khaled").Bold();
                            sig.Item().PaddingTop(20).LineHorizontal(0.5f).LineColor(Colors.Black);
                            sig.Item().AlignCenter().Text("Signature").FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });
        }).GeneratePdf();
    }
}
