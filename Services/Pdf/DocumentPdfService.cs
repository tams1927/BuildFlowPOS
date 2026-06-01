using HardwareManagementSystem.Models;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HardwareManagementSystem.Services.Pdf
{
    /// <summary>
    /// Generates professional A4 PDF documents using QuestPDF.
    /// Handles quotations, delivery receipts, and generic report tables.
    /// All documents share a standard tenant/branch header and footer.
    /// </summary>
    public class DocumentPdfService
    {
        private readonly IWebHostEnvironment _env;

        public DocumentPdfService(IWebHostEnvironment env)
        {
            _env = env;
        }

        /// <summary>Resolves the absolute filesystem path for a logo stored as a relative web path.</summary>
        private string? ResolveLogoPath(string? logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath)) return null;
            var abs = Path.Combine(_env.WebRootPath, logoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(abs) ? abs : null;
        }

        // ── Quotation ──────────────────────────────────────────────────

        public byte[] GenerateQuotationPdf(
            Quotation q,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName    = tenant?.Name    ?? settings?.BusinessName    ?? "HardBuild POS";
            var bizAddress = tenant?.Address ?? settings?.BusinessAddress ?? "";
            var bizPhone   = tenant?.Phone   ?? settings?.ContactNumber   ?? "";
            var currency   = settings?.CurrencySymbol ?? "₱";

            var absLogoPath = ResolveLogoPath(logoPath);

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    // ── Header ──────────────────────────────────────────
                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogoPath != null)
                            {
                                row.ConstantItem(60).PaddingRight(10)
                                    .Image(absLogoPath).FitHeight();
                            }

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName)
                                    .FontSize(16).Bold().FontColor(Color.FromHex("#1e3a5f"));

                                if (!string.IsNullOrWhiteSpace(bizAddress))
                                    left.Item().Text(bizAddress).FontSize(9).FontColor(Colors.Grey.Darken1);

                                if (!string.IsNullOrWhiteSpace(bizPhone))
                                    left.Item().Text($"Tel: {bizPhone}").FontSize(9).FontColor(Colors.Grey.Darken1);

                                if (q.Branch != null)
                                    left.Item().Text($"Branch: {q.Branch.Name}").FontSize(9);
                            });

                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("QUOTATION")
                                    .FontSize(14).Bold().FontColor(Color.FromHex("#1e3a5f"));

                                right.Item().AlignRight().Text($"No: {q.QuotationNo}")
                                    .FontSize(11).SemiBold();

                                right.Item().AlignRight()
                                    .Text($"Date: {q.QuotationDate:MMMM dd, yyyy}")
                                    .FontSize(9);

                                if (q.ValidUntil.HasValue)
                                    right.Item().AlignRight()
                                        .Text($"Valid Until: {q.ValidUntil:MMMM dd, yyyy}")
                                        .FontSize(9).FontColor(Colors.Orange.Darken2);
                            });
                        });

                        col.Item().PaddingTop(5)
                            .LineHorizontal(2).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    // ── Content ─────────────────────────────────────────
                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // Customer block
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("BILL TO").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(q.Customer?.CustomerName ?? q.CustomerName ?? "Walk-in Customer")
                                    .FontSize(12).Bold();
                                if (!string.IsNullOrWhiteSpace(q.Customer?.ContactNumber))
                                    c.Item().Text(q.Customer.ContactNumber).FontSize(9);
                            });

                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("STATUS").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(q.Status.ToUpper()).FontSize(11).Bold()
                                    .FontColor(q.Status == "Accepted"
                                        ? Color.FromHex("#065f46")
                                        : q.Status == "Voided" || q.Status == "Expired"
                                            ? Color.FromHex("#991b1b")
                                            : Colors.Black);
                            });
                        });

                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Items table
                        col.Item().PaddingVertical(6).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(25);    // #
                                cols.RelativeColumn(4);     // Description
                                cols.RelativeColumn();      // Qty
                                cols.RelativeColumn();      // Unit Price
                                cols.RelativeColumn();      // Disc %
                                cols.RelativeColumn();      // Subtotal
                            });

                            table.Header(h =>
                            {
                                var hStyle = TextStyle.Default.FontSize(9).Bold().FontColor(Colors.White);
                                var hBg = Color.FromHex("#1e3a5f");

                                h.Cell().Background(hBg).Padding(5).Text("#").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).Text("Description").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).AlignRight().Text("Qty").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).AlignRight().Text("Unit Price").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).AlignRight().Text("Disc%").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).AlignRight().Text("Subtotal").Style(hStyle);
                            });

                            int lineNo = 1;
                            foreach (var item in q.Items.OrderBy(i => i.SortOrder))
                            {
                                var bg = lineNo % 2 == 0 ? Color.FromHex("#f8f9fb") : Colors.White;

                                table.Cell().Background(bg).Padding(5).Text(lineNo.ToString()).FontSize(9);
                                table.Cell().Background(bg).Padding(5).Column(c =>
                                {
                                    c.Item().Text(item.Description).FontSize(10).Bold();
                                    if (item.Item != null)
                                        c.Item().Text($"SKU: {item.Item.ItemCode}").FontSize(8)
                                            .FontColor(Colors.Grey.Darken1);
                                });
                                table.Cell().Background(bg).Padding(5).AlignRight()
                                    .Text(item.Quantity.ToString("0.###")).FontSize(10);
                                table.Cell().Background(bg).Padding(5).AlignRight()
                                    .Text($"{currency}{item.UnitPrice:N2}").FontSize(10);
                                table.Cell().Background(bg).Padding(5).AlignRight()
                                    .Text(item.DiscountPercent > 0 ? $"{item.DiscountPercent:N2}%" : "—").FontSize(10);
                                table.Cell().Background(bg).Padding(5).AlignRight()
                                    .Text($"{currency}{item.Subtotal:N2}").FontSize(10).Bold();
                                lineNo++;
                            }
                        });

                        // Total
                        col.Item().AlignRight().PaddingRight(5).PaddingBottom(12).Column(c =>
                        {
                            c.Item().BorderTop(2).BorderColor(Color.FromHex("#1e3a5f"))
                                .PaddingTop(5).Row(r =>
                                {
                                    r.RelativeItem().Text("TOTAL AMOUNT").FontSize(12).Bold();
                                    r.ConstantItem(120).AlignRight()
                                        .Text($"{currency}{q.TotalAmount:N2}").FontSize(14).Bold()
                                        .FontColor(Color.FromHex("#1e3a5f"));
                                });
                        });

                        // Notes
                        if (!string.IsNullOrWhiteSpace(q.Notes))
                        {
                            col.Item().PaddingBottom(8).Column(c =>
                            {
                                c.Item().Text("NOTES").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(q.Notes).FontSize(10)
                                    .FontColor(Colors.Grey.Darken2);
                            });
                        }

                        // Signature area
                        col.Item().PaddingTop(32).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                    .PaddingTop(4).AlignCenter()
                                    .Text("Prepared By").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                            r.ConstantItem(24);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                    .PaddingTop(4).AlignCenter()
                                    .Text("Accepted By / Customer").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });
                    });

                    // ── Footer ───────────────────────────────────────────
                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Text(txt =>
                        {
                            txt.Span(settings?.ReceiptFooter ?? "Thank you for your business.").FontSize(8)
                                .FontColor(Colors.Grey.Medium);
                        });
                        row.RelativeItem().AlignRight().Text(txt =>
                        {
                            txt.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });

            return pdf.GeneratePdf();
        }

        // ── Delivery Receipt ───────────────────────────────────────────

        public byte[] GenerateDeliveryReceiptPdf(
            DeliveryReceipt dr,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName    = dr.Branch?.Name ?? tenant?.Name ?? settings?.BusinessName ?? "HardBuild POS";
            var bizAddress = dr.Branch?.Address ?? tenant?.Address ?? settings?.BusinessAddress ?? "";
            var bizPhone   = dr.Branch?.ContactNumber ?? tenant?.Phone ?? settings?.ContactNumber ?? "";

            var absLogoPath = ResolveLogoPath(logoPath);

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogoPath != null)
                            {
                                row.ConstantItem(60).PaddingRight(10)
                                    .Image(absLogoPath).FitHeight();
                            }

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName).FontSize(16).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));
                                if (!string.IsNullOrWhiteSpace(bizAddress))
                                    left.Item().Text(bizAddress).FontSize(9).FontColor(Colors.Grey.Darken1);
                                if (!string.IsNullOrWhiteSpace(bizPhone))
                                    left.Item().Text($"Tel: {bizPhone}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });

                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("DELIVERY RECEIPT").FontSize(13).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight().Text($"DR No: {dr.DRNumber}").FontSize(11).SemiBold();
                                right.Item().AlignRight()
                                    .Text($"Date: {dr.DeliveryDate:MMMM dd, yyyy}").FontSize(9);
                                right.Item().AlignRight()
                                    .Text($"Status: {dr.Status.ToUpper()}").FontSize(9)
                                    .FontColor(dr.Status == "Delivered"
                                        ? Color.FromHex("#065f46")
                                        : dr.Status == "Cancelled"
                                            ? Color.FromHex("#991b1b")
                                            : Colors.Black);
                            });
                        });

                        col.Item().PaddingTop(5)
                            .LineHorizontal(2).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        // Info row
                        col.Item().PaddingBottom(8).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("CONSIGNEE / CUSTOMER").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(dr.Customer?.CustomerName ?? "Walk-in Customer")
                                    .FontSize(12).Bold();
                                if (!string.IsNullOrWhiteSpace(dr.DeliveryAddress))
                                    c.Item().Text(dr.DeliveryAddress).FontSize(9);
                            });

                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("DRIVER").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(dr.DriverName ?? "—").FontSize(11).Bold();

                                if (dr.SalesHeader != null)
                                {
                                    c.Item().PaddingTop(4).Text("REFERENCE SALE").FontSize(8).Bold()
                                        .FontColor(Colors.Grey.Darken1);
                                    c.Item().Text(dr.SalesHeader.SalesNumber).FontSize(10);
                                }
                            });
                        });

                        if (!string.IsNullOrWhiteSpace(dr.Notes))
                        {
                            col.Item().PaddingBottom(8).Column(c =>
                            {
                                c.Item().Text("NOTES / INSTRUCTIONS").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(dr.Notes).FontSize(10);
                            });
                        }

                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        // Items table
                        col.Item().PaddingVertical(6).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(25);
                                cols.RelativeColumn(4);
                                cols.RelativeColumn();
                                cols.RelativeColumn();
                                cols.RelativeColumn(1.5f);
                            });

                            table.Header(h =>
                            {
                                var hStyle = TextStyle.Default.FontSize(9).Bold().FontColor(Colors.White);
                                var hBg = Color.FromHex("#1e3a5f");

                                h.Cell().Background(hBg).Padding(5).Text("#").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).Text("Description").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).AlignRight().Text("Qty").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).Text("Unit").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).Text("Received (Qty)").Style(hStyle);
                            });

                            int n = 1;
                            foreach (var item in dr.Items.OrderBy(i => i.SortOrder))
                            {
                                var bg = n % 2 == 0 ? Color.FromHex("#f8f9fb") : Colors.White;
                                table.Cell().Background(bg).Padding(5).Text(n.ToString()).FontSize(9);
                                table.Cell().Background(bg).Padding(5).Column(c =>
                                {
                                    c.Item().Text(item.Description).FontSize(10).Bold();
                                    if (item.Item != null)
                                        c.Item().Text($"[{item.Item.ItemCode}]").FontSize(8)
                                            .FontColor(Colors.Grey.Darken1);
                                });
                                table.Cell().Background(bg).Padding(5).AlignRight()
                                    .Text(item.Quantity.ToString("0.###")).FontSize(10);
                                table.Cell().Background(bg).Padding(5)
                                    .Text(item.Unit ?? item.Item?.Unit?.ShortName ?? "").FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text("").FontSize(10);
                                n++;
                            }
                        });

                        // Signature area
                        col.Item().PaddingTop(48).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                    .PaddingTop(4).AlignCenter()
                                    .Text("Prepared By").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                            r.ConstantItem(20);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                    .PaddingTop(4).AlignCenter()
                                    .Text("Delivered By (Driver)").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                            r.ConstantItem(20);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                    .PaddingTop(4).AlignCenter()
                                    .Text("Received By / Signature").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });
                    });

                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Text(txt =>
                        {
                            txt.Span(settings?.ReceiptFooter ?? "This document serves as proof of delivery.")
                                .FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                        row.RelativeItem().AlignRight().Text(txt =>
                        {
                            txt.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });

            return pdf.GeneratePdf();
        }

        // ── Transfer Slip ──────────────────────────────────────────────

        public byte[] GenerateTransferSlipPdf(
            BranchTransfer bt,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName = tenant?.Name ?? settings?.BusinessName ?? "HardBuild POS";

            var absLogoPath = ResolveLogoPath(logoPath);

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogoPath != null)
                            {
                                row.ConstantItem(60).PaddingRight(10)
                                    .Image(absLogoPath).FitHeight();
                            }

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName).FontSize(16).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));
                            });
                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("TRANSFER SLIP").FontSize(13).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight().Text($"No: {bt.TransferNumber}").FontSize(11).SemiBold();
                                right.Item().AlignRight()
                                    .Text($"Date: {bt.CreatedAtUtc.ToLocalTime():MMMM dd, yyyy}").FontSize(9);
                                right.Item().AlignRight()
                                    .Text($"Status: {bt.Status.ToUpper()}").FontSize(9);
                            });
                        });
                        col.Item().PaddingTop(5)
                            .LineHorizontal(2).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    page.Content().PaddingVertical(10).Column(col =>
                    {
                        col.Item().PaddingBottom(8).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("FROM BRANCH").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(bt.FromBranch?.Name ?? "—").FontSize(12).Bold();
                                if (!string.IsNullOrWhiteSpace(bt.FromBranch?.Address))
                                    c.Item().Text(bt.FromBranch.Address).FontSize(9);
                            });
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("TO BRANCH").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(bt.ToBranch?.Name ?? "—").FontSize(12).Bold();
                                if (!string.IsNullOrWhiteSpace(bt.ToBranch?.Address))
                                    c.Item().Text(bt.ToBranch.Address).FontSize(9);
                            });
                        });

                        if (!string.IsNullOrWhiteSpace(bt.Notes))
                        {
                            col.Item().PaddingBottom(8).Column(c =>
                            {
                                c.Item().Text("NOTES").FontSize(8).Bold()
                                    .FontColor(Colors.Grey.Darken1);
                                c.Item().Text(bt.Notes).FontSize(10);
                            });
                        }

                        col.Item().LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        col.Item().PaddingVertical(6).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(25);
                                cols.RelativeColumn(4);
                                cols.RelativeColumn();
                                cols.RelativeColumn();
                                cols.RelativeColumn();
                            });

                            table.Header(h =>
                            {
                                var hStyle = TextStyle.Default.FontSize(9).Bold().FontColor(Colors.White);
                                var hBg = Color.FromHex("#1e3a5f");

                                h.Cell().Background(hBg).Padding(5).Text("#").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).Text("Product").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).AlignRight().Text("Quantity").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).Text("Unit").Style(hStyle);
                                h.Cell().Background(hBg).Padding(5).Text("Received (Qty)").Style(hStyle);
                            });

                            int n = 1;
                            foreach (var item in bt.BranchTransferItems)
                            {
                                var bg = n % 2 == 0 ? Color.FromHex("#f8f9fb") : Colors.White;
                                table.Cell().Background(bg).Padding(5).Text(n.ToString()).FontSize(9);
                                table.Cell().Background(bg).Padding(5).Column(c =>
                                {
                                    c.Item().Text(item.Product?.ItemName ?? "—").FontSize(10).Bold();
                                    if (item.Product != null)
                                        c.Item().Text($"SKU: {item.Product.ItemCode}").FontSize(8)
                                            .FontColor(Colors.Grey.Darken1);
                                });
                                table.Cell().Background(bg).Padding(5).AlignRight()
                                    .Text(item.Quantity.ToString("0.###")).FontSize(10);
                                table.Cell().Background(bg).Padding(5)
                                    .Text(item.Product?.Unit?.ShortName ?? "").FontSize(10);
                                table.Cell().Background(bg).Padding(5).Text("").FontSize(10);
                                n++;
                            }
                        });

                        col.Item().PaddingTop(48).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                    .PaddingTop(4).AlignCenter()
                                    .Text("Released By").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                            r.ConstantItem(20);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                    .PaddingTop(4).AlignCenter()
                                    .Text("Received By").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                            r.ConstantItem(20);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().BorderTop(1).BorderColor(Colors.Grey.Medium)
                                    .PaddingTop(4).AlignCenter()
                                    .Text("Approved By").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });
                    });

                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Text(txt =>
                        {
                            txt.Span($"Generated {DateTime.Now:MMM dd, yyyy hh:mm tt}").FontSize(8)
                                .FontColor(Colors.Grey.Medium);
                        });
                        row.RelativeItem().AlignRight().Text(txt =>
                        {
                            txt.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });

            return pdf.GeneratePdf();
        }

        // ── Report table (generic, keeps ReportPdfService pattern) ─────

        public byte[] GenerateReportPdf(
            string reportTitle,
            string dateRange,
            string tenantName,
            string? branchName,
            List<string> headers,
            List<List<string>> rows)
        {
            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(tenantName).FontSize(14).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));
                                if (!string.IsNullOrWhiteSpace(branchName))
                                    left.Item().Text($"Branch: {branchName}").FontSize(9);
                            });
                            r.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text(reportTitle).FontSize(12).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight().Text(dateRange).FontSize(9)
                                    .FontColor(Colors.Grey.Darken1);
                                right.Item().AlignRight()
                                    .Text($"Generated: {DateTime.Now:MMM dd, yyyy hh:mm tt}")
                                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                        });
                        col.Item().PaddingTop(4)
                            .LineHorizontal(2).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    page.Content().PaddingVertical(10).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            foreach (var _ in headers)
                                cols.RelativeColumn();
                        });

                        table.Header(h =>
                        {
                            foreach (var hdr in headers)
                                h.Cell().Background(Color.FromHex("#1e3a5f")).Padding(5)
                                    .Text(hdr).FontSize(9).Bold().FontColor(Colors.White);
                        });

                        int rowIdx = 0;
                        foreach (var row in rows)
                        {
                            var bg = rowIdx++ % 2 == 0 ? Colors.White : Color.FromHex("#f8f9fb");
                            foreach (var cell in row)
                                table.Cell().Background(bg).BorderBottom(0.5f)
                                    .BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cell).FontSize(9);
                        }
                    });

                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Text(txt =>
                        {
                            txt.Span(tenantName).FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.Span(" · ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.Span(reportTitle).FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                        row.RelativeItem().AlignRight().Text(txt =>
                        {
                            txt.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });

            return pdf.GeneratePdf();
        }

        // ── Purchase Order ─────────────────────────────────────────────

        public byte[] GeneratePurchaseOrderPdf(
            PurchaseOrder po,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName    = tenant?.Name    ?? settings?.BusinessName    ?? "HardBuild POS";
            var bizAddress = tenant?.Address ?? settings?.BusinessAddress ?? "";
            var bizPhone   = tenant?.Phone   ?? settings?.ContactNumber   ?? "";
            var currency   = settings?.CurrencySymbol ?? "₱";

            var absLogoPath = ResolveLogoPath(logoPath);

            var pdf = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    // ── Header ──────────────────────────────────────────
                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogoPath != null)
                            {
                                row.ConstantItem(60).PaddingRight(10)
                                    .Image(absLogoPath).FitHeight();
                            }

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName)
                                    .FontSize(16).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                if (!string.IsNullOrWhiteSpace(bizAddress))
                                    left.Item().Text(bizAddress).FontSize(9).FontColor(Colors.Grey.Darken1);
                                if (!string.IsNullOrWhiteSpace(bizPhone))
                                    left.Item().Text($"Tel: {bizPhone}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });

                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("PURCHASE ORDER")
                                    .FontSize(18).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight().Text($"PO #: {po.PONumber}")
                                    .FontSize(11).Bold();
                                right.Item().AlignRight().Text($"Date: {po.PODate:MMMM dd, yyyy}")
                                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                                if (po.ExpectedDeliveryDate.HasValue)
                                    right.Item().AlignRight().Text($"Expected: {po.ExpectedDeliveryDate:MMMM dd, yyyy}")
                                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });

                        col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    // ── Content ─────────────────────────────────────────
                    page.Content().PaddingTop(12).Column(col =>
                    {
                        // Supplier block
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(sup =>
                            {
                                sup.Item().Text("BILL TO / SUPPLIER")
                                    .FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                sup.Item().Text(po.Supplier?.SupplierName ?? "—")
                                    .FontSize(11).Bold();
                                if (!string.IsNullOrWhiteSpace(po.Supplier?.ContactNumber))
                                    sup.Item().Text($"Tel: {po.Supplier.ContactNumber}").FontSize(9);
                                if (!string.IsNullOrWhiteSpace(po.Supplier?.Email))
                                    sup.Item().Text(po.Supplier.Email).FontSize(9);
                                if (!string.IsNullOrWhiteSpace(po.Supplier?.Address))
                                    sup.Item().Text(po.Supplier.Address).FontSize(9);
                            });

                            row.RelativeItem().Column(info =>
                            {
                                info.Item().Text("DELIVERY INFO")
                                    .FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                info.Item().Text($"Branch: {po.Branch?.Name ?? "Main"}")
                                    .FontSize(9);
                                info.Item().Text($"Status: {po.Status}")
                                    .FontSize(9);
                                if (!string.IsNullOrWhiteSpace(po.Notes))
                                    info.Item().Text($"Notes: {po.Notes}").FontSize(9);
                            });
                        });

                        col.Item().PaddingVertical(10).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                        // Items table
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(24);    // #
                                cols.RelativeColumn(4);     // Description
                                cols.RelativeColumn(1.5f);  // Qty
                                cols.RelativeColumn(1);     // Unit
                                cols.RelativeColumn(2);     // Unit Cost
                                cols.RelativeColumn(2);     // Total
                            });

                            // Header row
                            static IContainer HeaderCell(IContainer c) =>
                                c.Background(Color.FromHex("#1e3a5f"))
                                 .Padding(5).AlignMiddle();

                            table.Header(h =>
                            {
                                h.Cell().Element(HeaderCell)
                                    .Text("#").FontSize(9).Bold().FontColor(Colors.White);
                                h.Cell().Element(HeaderCell)
                                    .Text("Description").FontSize(9).Bold().FontColor(Colors.White);
                                h.Cell().Element(HeaderCell).AlignRight()
                                    .Text("Qty").FontSize(9).Bold().FontColor(Colors.White);
                                h.Cell().Element(HeaderCell)
                                    .Text("Unit").FontSize(9).Bold().FontColor(Colors.White);
                                h.Cell().Element(HeaderCell).AlignRight()
                                    .Text("Unit Cost").FontSize(9).Bold().FontColor(Colors.White);
                                h.Cell().Element(HeaderCell).AlignRight()
                                    .Text("Total").FontSize(9).Bold().FontColor(Colors.White);
                            });

                            int lineNo = 1;
                            foreach (var pi in po.Items.OrderBy(i => i.Id))
                            {
                                static IContainer DataCell(IContainer c, bool alt) =>
                                    c.Background(alt ? Color.FromHex("#f8f9fb") : Colors.White)
                                     .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                     .Padding(5).AlignMiddle();

                                bool alt = lineNo % 2 == 0;

                                table.Cell().Element(c => DataCell(c, alt))
                                    .Text(lineNo.ToString()).FontSize(9).FontColor(Colors.Grey.Darken1);
                                table.Cell().Element(c => DataCell(c, alt)).Column(d =>
                                {
                                    d.Item().Text(pi.Item?.ItemName ?? $"Item #{pi.ItemId}")
                                        .FontSize(10).Bold();
                                    if (pi.Item != null)
                                        d.Item().Text(pi.Item.ItemCode).FontSize(8).FontColor(Colors.Grey.Darken1);
                                });
                                table.Cell().Element(c => DataCell(c, alt)).AlignRight()
                                    .Text(pi.Quantity.ToString("0.###")).FontSize(9);
                                table.Cell().Element(c => DataCell(c, alt))
                                    .Text(pi.Item?.Unit?.ShortName ?? "—").FontSize(9);
                                table.Cell().Element(c => DataCell(c, alt)).AlignRight()
                                    .Text($"{currency}{pi.UnitCost:N2}").FontSize(9);
                                table.Cell().Element(c => DataCell(c, alt)).AlignRight()
                                    .Text($"{currency}{pi.TotalCost:N2}").FontSize(10).Bold();

                                lineNo++;
                            }
                        });

                        // Total
                        col.Item().PaddingTop(8).AlignRight().Row(totRow =>
                        {
                            totRow.AutoItem().PaddingRight(20).Column(lbl =>
                            {
                                lbl.Item().Text("TOTAL AMOUNT").FontSize(11).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));
                            });
                            totRow.AutoItem().Column(val =>
                            {
                                val.Item().Text($"{currency}{po.TotalAmount:N2}")
                                    .FontSize(14).Bold().FontColor(Color.FromHex("#1e3a5f"));
                            });
                        });

                        // Signature area
                        col.Item().PaddingTop(48).Row(sig =>
                        {
                            static IContainer SigBox(IContainer c) =>
                                c.BorderTop(1).BorderColor(Colors.Grey.Medium)
                                 .PaddingTop(4).AlignCenter();

                            sig.RelativeItem().Element(SigBox)
                                .Text("Prepared By").FontSize(9).FontColor(Colors.Grey.Darken1);
                            sig.ConstantItem(20);
                            sig.RelativeItem().Element(SigBox)
                                .Text("Approved By").FontSize(9).FontColor(Colors.Grey.Darken1);
                            sig.ConstantItem(20);
                            sig.RelativeItem().Element(SigBox)
                                .Text("Received By / Supplier").FontSize(9).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    // ── Footer ──────────────────────────────────────────
                    page.Footer().AlignCenter().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                        txt.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                        txt.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                        txt.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                });
            });

            return pdf.GeneratePdf();
        }

        // ── Customer Statement of Account ──────────────────────────────

        public byte[] GenerateCustomerSOAPdf(
            CustomerSOAViewModel vm,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName    = tenant?.Name ?? settings?.BusinessName ?? "HardBuild POS";
            var bizAddress = tenant?.Address ?? settings?.BusinessAddress ?? "";
            var bizPhone   = tenant?.Phone ?? settings?.ContactNumber ?? "";
            var currency   = settings?.CurrencySymbol ?? "₱";
            var absLogo    = ResolveLogoPath(logoPath);

            var pdf = Document.Create(c =>
            {
                c.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogo != null)
                                row.ConstantItem(60).PaddingRight(10).Image(absLogo).FitHeight();

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName).FontSize(16).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                if (!string.IsNullOrWhiteSpace(bizAddress))
                                    left.Item().Text(bizAddress).FontSize(9).FontColor(Colors.Grey.Darken1);
                                if (!string.IsNullOrWhiteSpace(bizPhone))
                                    left.Item().Text($"Tel: {bizPhone}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });

                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("STATEMENT OF ACCOUNT")
                                    .FontSize(16).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight()
                                    .Text($"Period: {vm.DateFrom:MMM dd, yyyy} — {vm.DateTo:MMM dd, yyyy}")
                                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });
                        col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    page.Content().PaddingTop(12).Column(col =>
                    {
                        // Customer block
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(cust =>
                            {
                                cust.Item().Text("CUSTOMER").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                cust.Item().Text(vm.Customer.CustomerName).FontSize(12).Bold();
                                if (!string.IsNullOrWhiteSpace(vm.Customer.ContactNumber))
                                    cust.Item().Text($"Tel: {vm.Customer.ContactNumber}").FontSize(9);
                                if (!string.IsNullOrWhiteSpace(vm.Customer.Email))
                                    cust.Item().Text(vm.Customer.Email).FontSize(9);
                                if (!string.IsNullOrWhiteSpace(vm.Customer.Address))
                                    cust.Item().Text(vm.Customer.Address).FontSize(9);
                            });
                            row.RelativeItem().Column(bal =>
                            {
                                bal.Item().AlignRight().Text("OUTSTANDING BALANCE")
                                    .FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                bal.Item().AlignRight().Text($"{currency}{vm.ClosingBalance:N2}")
                                    .FontSize(18).Bold()
                                    .FontColor(vm.ClosingBalance > 0 ? Color.FromHex("#dc2626") : Color.FromHex("#16a34a"));
                            });
                        });

                        col.Item().PaddingVertical(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                        // Beginning balance
                        col.Item().Row(row =>
                        {
                            row.RelativeItem(3).Text("Beginning Balance").FontSize(9).Italic();
                            row.RelativeItem().AlignRight().Text($"{currency}{vm.BeginningBalance:N2}").FontSize(9).Bold();
                        });

                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(1.5f); // Date
                                cols.RelativeColumn(1.5f); // Type
                                cols.RelativeColumn(2);    // Reference
                                cols.RelativeColumn(3);    // Remarks
                                cols.RelativeColumn(1.5f); // Debit
                                cols.RelativeColumn(1.5f); // Credit
                                cols.RelativeColumn(1.5f); // Balance
                            });

                            static IContainer Hdr(IContainer c) =>
                                c.Background(Color.FromHex("#1e3a5f")).Padding(5).AlignMiddle();

                            table.Header(h =>
                            {
                                foreach (var lbl in new[] { "Date", "Type", "Reference", "Remarks", "Debit", "Credit", "Balance" })
                                    h.Cell().Element(Hdr).Text(lbl).FontSize(8).Bold().FontColor(Colors.White);
                            });

                            int lineNo = 1;
                            foreach (var t in vm.Transactions)
                            {
                                bool alt = lineNo++ % 2 == 0;
                                static IContainer Cell(IContainer c, bool a) =>
                                    c.Background(a ? Color.FromHex("#f8f9fb") : Colors.White)
                                     .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                     .Padding(4).AlignMiddle();

                                table.Cell().Element(c => Cell(c, alt))
                                    .Text(t.TransactionDate.ToString("MM/dd/yy")).FontSize(9);
                                table.Cell().Element(c => Cell(c, alt))
                                    .Text(t.TransactionType).FontSize(9);
                                table.Cell().Element(c => Cell(c, alt))
                                    .Text(t.ReferenceNumber).FontSize(9);
                                table.Cell().Element(c => Cell(c, alt))
                                    .Text(t.Remarks ?? "").FontSize(8).FontColor(Colors.Grey.Darken1);
                                table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                    .Text(t.DebitAmount > 0 ? $"{currency}{t.DebitAmount:N2}" : "").FontSize(9);
                                table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                    .Text(t.CreditAmount > 0 ? $"{currency}{t.CreditAmount:N2}" : "").FontSize(9)
                                    .FontColor(Colors.Green.Medium);
                                table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                    .Text($"{currency}{t.RunningBalance:N2}").FontSize(9).Bold();
                            }
                        });

                        col.Item().PaddingTop(8).Row(totRow =>
                        {
                            totRow.RelativeItem(3);
                            totRow.RelativeItem().Column(summary =>
                            {
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Total Charges:").FontSize(9);
                                    r.AutoItem().Text($"{currency}{vm.TotalDebits:N2}").FontSize(9).Bold();
                                });
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Total Payments:").FontSize(9);
                                    r.AutoItem().Text($"{currency}{vm.TotalCredits:N2}").FontSize(9).Bold()
                                        .FontColor(Colors.Green.Medium);
                                });
                                summary.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Closing Balance:").FontSize(10).Bold();
                                    r.AutoItem().Text($"{currency}{vm.ClosingBalance:N2}").FontSize(12).Bold()
                                        .FontColor(vm.ClosingBalance > 0 ? Color.FromHex("#dc2626") : Color.FromHex("#16a34a"));
                                });
                            });
                        });
                    });

                    page.Footer().Row(foot =>
                    {
                        foot.RelativeItem().Text($"Printed by: {DateTime.Now:MMM dd, yyyy hh:mm tt}")
                            .FontSize(8).FontColor(Colors.Grey.Medium);
                        foot.AutoItem().AlignRight().Text(txt =>
                        {
                            txt.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });

            return pdf.GeneratePdf();
        }

        // ── Supplier Statement PDF ─────────────────────────────────────

        public byte[] GenerateSupplierStatementPdf(
            SupplierStatementViewModel vm,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName    = tenant?.Name ?? settings?.BusinessName ?? "HardBuild POS";
            var bizAddress = tenant?.Address ?? settings?.BusinessAddress ?? "";
            var bizPhone   = tenant?.Phone ?? settings?.ContactNumber ?? "";
            var currency   = settings?.CurrencySymbol ?? "₱";
            var absLogo    = ResolveLogoPath(logoPath);

            var pdf = Document.Create(c =>
            {
                c.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily(Fonts.Arial));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogo != null)
                                row.ConstantItem(60).PaddingRight(10).Image(absLogo).FitHeight();

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName).FontSize(16).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                if (!string.IsNullOrWhiteSpace(bizAddress))
                                    left.Item().Text(bizAddress).FontSize(9).FontColor(Colors.Grey.Darken1);
                                if (!string.IsNullOrWhiteSpace(bizPhone))
                                    left.Item().Text($"Tel: {bizPhone}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });

                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("SUPPLIER STATEMENT")
                                    .FontSize(16).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight()
                                    .Text($"Period: {vm.DateFrom:MMM dd, yyyy} — {vm.DateTo:MMM dd, yyyy}")
                                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });
                        col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    page.Content().PaddingTop(12).Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(sup =>
                            {
                                sup.Item().Text("SUPPLIER").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                sup.Item().Text(vm.Supplier.SupplierName).FontSize(12).Bold();
                                if (!string.IsNullOrWhiteSpace(vm.Supplier.ContactNumber))
                                    sup.Item().Text($"Tel: {vm.Supplier.ContactNumber}").FontSize(9);
                                if (!string.IsNullOrWhiteSpace(vm.Supplier.Email))
                                    sup.Item().Text(vm.Supplier.Email).FontSize(9);
                                if (!string.IsNullOrWhiteSpace(vm.Supplier.Address))
                                    sup.Item().Text(vm.Supplier.Address).FontSize(9);
                            });
                            row.RelativeItem().Column(bal =>
                            {
                                bal.Item().AlignRight().Text("OUTSTANDING BALANCE")
                                    .FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
                                bal.Item().AlignRight().Text($"{currency}{vm.ClosingBalance:N2}")
                                    .FontSize(18).Bold()
                                    .FontColor(vm.ClosingBalance > 0 ? Color.FromHex("#dc2626") : Color.FromHex("#16a34a"));
                            });
                        });

                        col.Item().PaddingVertical(8).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem(3).Text("Opening Balance").FontSize(9).Italic();
                            row.RelativeItem().AlignRight().Text($"{currency}{vm.OpeningBalance:N2}").FontSize(9).Bold();
                        });

                        col.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(1.5f);
                                cols.RelativeColumn(1.5f);
                                cols.RelativeColumn(1.5f);
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(1.5f);
                                cols.RelativeColumn(1.5f);
                                cols.RelativeColumn(1.5f);
                            });

                            static IContainer Hdr(IContainer c) =>
                                c.Background(Color.FromHex("#1e3a5f")).Padding(5).AlignMiddle();

                            table.Header(h =>
                            {
                                foreach (var lbl in new[] { "Date", "Type", "Reference", "Description", "Debit", "Credit", "Balance" })
                                    h.Cell().Element(Hdr).Text(lbl).FontSize(8).Bold().FontColor(Colors.White);
                            });

                            int lineNo = 1;
                            foreach (var line in vm.Lines)
                            {
                                bool alt = lineNo++ % 2 == 0;
                                static IContainer Cell(IContainer c, bool a) =>
                                    c.Background(a ? Color.FromHex("#f8f9fb") : Colors.White)
                                     .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                     .Padding(4).AlignMiddle();

                                table.Cell().Element(c => Cell(c, alt))
                                    .Text(line.Date.ToString("MM/dd/yy")).FontSize(9);
                                table.Cell().Element(c => Cell(c, alt))
                                    .Text(line.Type).FontSize(9);
                                table.Cell().Element(c => Cell(c, alt))
                                    .Text(line.ReferenceNo).FontSize(9);
                                table.Cell().Element(c => Cell(c, alt))
                                    .Text(line.Description).FontSize(8).FontColor(Colors.Grey.Darken1);
                                table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                    .Text(line.Debit > 0 ? $"{currency}{line.Debit:N2}" : "").FontSize(9);
                                table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                    .Text(line.Credit > 0 ? $"{currency}{line.Credit:N2}" : "").FontSize(9)
                                    .FontColor(Colors.Green.Medium);
                                table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                    .Text($"{currency}{line.RunningBalance:N2}").FontSize(9).Bold();
                            }
                        });

                        col.Item().PaddingTop(8).Row(totRow =>
                        {
                            totRow.RelativeItem(3);
                            totRow.RelativeItem().Column(summary =>
                            {
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Total Purchases:").FontSize(9);
                                    r.AutoItem().Text($"{currency}{vm.TotalPurchases:N2}").FontSize(9).Bold();
                                });
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Total Payments:").FontSize(9);
                                    r.AutoItem().Text($"{currency}{vm.TotalPayments:N2}").FontSize(9).Bold()
                                        .FontColor(Colors.Green.Medium);
                                });
                                summary.Item().LineHorizontal(1).LineColor(Colors.Grey.Darken1);
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Closing Balance:").FontSize(10).Bold();
                                    r.AutoItem().Text($"{currency}{vm.ClosingBalance:N2}").FontSize(12).Bold()
                                        .FontColor(vm.ClosingBalance > 0 ? Color.FromHex("#dc2626") : Color.FromHex("#16a34a"));
                                });
                            });
                        });
                    });

                    page.Footer().Row(foot =>
                    {
                        foot.RelativeItem().Text($"Printed by: {DateTime.Now:MMM dd, yyyy hh:mm tt}")
                            .FontSize(8).FontColor(Colors.Grey.Medium);
                        foot.AutoItem().AlignRight().Text(txt =>
                        {
                            txt.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.Span(" of ").FontSize(8).FontColor(Colors.Grey.Medium);
                            txt.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                        });
                    });
                });
            });

            return pdf.GeneratePdf();
        }
    }
}
