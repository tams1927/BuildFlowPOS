using HardwareManagementSystem.Models;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Hosting;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HardwareManagementSystem.Services.Pdf
{
    /// <summary>
    /// Generates tabular report PDFs with a standard tenant/branch header and page numbering.
    /// Signature is backward-compatible; tenantName and branchName default to null.
    /// </summary>
    public class ReportPdfService
    {
        private readonly IWebHostEnvironment _env;

        public ReportPdfService(IWebHostEnvironment env)
        {
            _env = env;
        }

        private string? ResolveLogoPath(string? logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath)) return null;
            var abs = Path.Combine(_env.WebRootPath, logoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(abs) ? abs : null;
        }

        // ── Customer Aging PDF ─────────────────────────────────────────

        public byte[] GenerateCustomerAgingPdf(
            CustomerAgingViewModel vm,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName    = tenant?.Name ?? settings?.BusinessName ?? "HardBuild POS";
            var currency   = settings?.CurrencySymbol ?? "₱";
            var absLogo    = ResolveLogoPath(logoPath);

            var pdf = Document.Create(c =>
            {
                c.Page(page =>
                {
                    page.Margin(25);
                    page.Size(PageSizes.A4.Landscape());
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogo != null)
                                row.ConstantItem(50).PaddingRight(8).Image(absLogo).FitHeight();

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName).FontSize(14).Bold().FontColor(Color.FromHex("#1e3a5f"));
                            });

                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("ACCOUNTS RECEIVABLE AGING")
                                    .FontSize(13).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight().Text($"As of {vm.AsOfDate:MMMM dd, yyyy}")
                                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                                right.Item().AlignRight()
                                    .Text($"Generated: {DateTime.Now:MMM dd, yyyy hh:mm tt}")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                            });
                        });

                        col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    page.Content().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(3);   // Customer
                            cols.RelativeColumn(2);   // Current
                            cols.RelativeColumn(2);   // 1-30
                            cols.RelativeColumn(2);   // 31-60
                            cols.RelativeColumn(2);   // 61-90
                            cols.RelativeColumn(2);   // 90+
                            cols.RelativeColumn(2);   // Total
                        });

                        static IContainer Hdr(IContainer c) =>
                            c.Background(Color.FromHex("#1e3a5f")).Padding(5).AlignMiddle();

                        table.Header(h =>
                        {
                            h.Cell().Element(Hdr).Text("Customer").FontSize(8).Bold().FontColor(Colors.White);
                            foreach (var lbl in new[] { "Current (0–30)", "31–60 Days", "61–90 Days", "91–120 Days", "120+ Days", "Total" })
                                h.Cell().Element(Hdr).AlignRight().Text(lbl).FontSize(8).Bold().FontColor(Colors.White);
                        });

                        int line = 1;
                        foreach (var r in vm.Rows)
                        {
                            bool alt = line++ % 2 == 0;
                            static IContainer Cell(IContainer c, bool a) =>
                                c.Background(a ? Color.FromHex("#f8f9fb") : Colors.White)
                                 .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                 .Padding(4).AlignMiddle();

                            table.Cell().Element(c => Cell(c, alt))
                                .Text(r.CustomerName).FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Current > 0 ? $"{currency}{r.Current:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Days1_30 > 0 ? $"{currency}{r.Days1_30:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Days31_60 > 0 ? $"{currency}{r.Days31_60:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Days61_90 > 0 ? $"{currency}{r.Days61_90:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Over90 > 0 ? $"{currency}{r.Over90:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text($"{currency}{r.TotalOutstanding:N2}").FontSize(9).Bold();
                        }

                        // Totals row
                        static IContainer TotCell(IContainer c) =>
                            c.Background(Color.FromHex("#1e3a5f")).Padding(4);

                        table.Cell().Element(TotCell).Text("TOTAL").FontSize(9).Bold().FontColor(Colors.White);
                        foreach (var amt in new[] { vm.TotalCurrent, vm.TotalDays1_30, vm.TotalDays31_60, vm.TotalDays61_90, vm.TotalOver90, vm.TotalBalance })
                            table.Cell().Element(TotCell).AlignRight()
                                .Text($"{currency}{amt:N2}").FontSize(9).Bold().FontColor(Colors.White);
                    });

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

        // ── Supplier Aging PDF ─────────────────────────────────────────

        public byte[] GenerateSupplierAgingPdf(
            SupplierAgingViewModel vm,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName  = tenant?.Name ?? settings?.BusinessName ?? "HardBuild POS";
            var currency = settings?.CurrencySymbol ?? "₱";
            var absLogo  = ResolveLogoPath(logoPath);

            var pdf = Document.Create(c =>
            {
                c.Page(page =>
                {
                    page.Margin(25);
                    page.Size(PageSizes.A4.Landscape());
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogo != null)
                                row.ConstantItem(50).PaddingRight(8).Image(absLogo).FitHeight();

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName).FontSize(14).Bold().FontColor(Color.FromHex("#1e3a5f"));
                            });

                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("ACCOUNTS PAYABLE AGING")
                                    .FontSize(13).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight().Text($"As of {vm.AsOfDate:MMMM dd, yyyy}")
                                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                                right.Item().AlignRight()
                                    .Text($"Generated: {DateTime.Now:MMM dd, yyyy hh:mm tt}")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                            });
                        });
                        col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    page.Content().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(3);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                            cols.RelativeColumn(2);
                        });

                        static IContainer Hdr(IContainer c) =>
                            c.Background(Color.FromHex("#1e3a5f")).Padding(5).AlignMiddle();

                        table.Header(h =>
                        {
                            h.Cell().Element(Hdr).Text("Supplier").FontSize(8).Bold().FontColor(Colors.White);
                            foreach (var lbl in new[] { "Current (0–30)", "31–60 Days", "61–90 Days", "91–120 Days", "120+ Days", "Total" })
                                h.Cell().Element(Hdr).AlignRight().Text(lbl).FontSize(8).Bold().FontColor(Colors.White);
                        });

                        int line = 1;
                        foreach (var r in vm.Rows)
                        {
                            bool alt = line++ % 2 == 0;
                            static IContainer Cell(IContainer c, bool a) =>
                                c.Background(a ? Color.FromHex("#f8f9fb") : Colors.White)
                                 .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                 .Padding(4).AlignMiddle();

                            table.Cell().Element(c => Cell(c, alt)).Text(r.SupplierName).FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Current > 0 ? $"{currency}{r.Current:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Days1_30 > 0 ? $"{currency}{r.Days1_30:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Days31_60 > 0 ? $"{currency}{r.Days31_60:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Days61_90 > 0 ? $"{currency}{r.Days61_90:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text(r.Over90 > 0 ? $"{currency}{r.Over90:N2}" : "—").FontSize(9);
                            table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                .Text($"{currency}{r.TotalOutstanding:N2}").FontSize(9).Bold();
                        }

                        static IContainer TotCell(IContainer c) =>
                            c.Background(Color.FromHex("#1e3a5f")).Padding(4);

                        table.Cell().Element(TotCell).Text("TOTAL").FontSize(9).Bold().FontColor(Colors.White);
                        foreach (var amt in new[] { vm.TotalCurrent, vm.TotalDays1_30, vm.TotalDays31_60, vm.TotalDays61_90, vm.TotalOver90, vm.TotalBalance })
                            table.Cell().Element(TotCell).AlignRight()
                                .Text($"{currency}{amt:N2}").FontSize(9).Bold().FontColor(Colors.White);
                    });

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

        // ── Inventory Valuation PDF (grouped by category) ──────────────

        public byte[] GenerateInventoryValuationPdf(
            InventoryValuationReport vm,
            SystemSetting? settings,
            Tenant? tenant,
            string? logoPath = null)
        {
            var bizName  = tenant?.Name ?? settings?.BusinessName ?? "HardBuild POS";
            var currency = settings?.CurrencySymbol ?? "₱";
            var absLogo  = ResolveLogoPath(logoPath);

            var pdf = Document.Create(c =>
            {
                c.Page(page =>
                {
                    page.Margin(25);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily(Fonts.Arial));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            if (absLogo != null)
                                row.ConstantItem(50).PaddingRight(8).Image(absLogo).FitHeight();

                            row.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName).FontSize(14).Bold().FontColor(Color.FromHex("#1e3a5f"));
                            });

                            row.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text("INVENTORY VALUATION REPORT")
                                    .FontSize(12).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                right.Item().AlignRight().Text($"As of {vm.AsOfDate:MMMM dd, yyyy}")
                                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                                right.Item().AlignRight()
                                    .Text($"Generated: {DateTime.Now:MMM dd, yyyy hh:mm tt}")
                                    .FontSize(8).FontColor(Colors.Grey.Medium);
                            });
                        });
                        col.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    page.Content().PaddingTop(10).Column(content =>
                    {
                        foreach (var group in vm.Groups)
                        {
                            // Category header
                            content.Item().PaddingTop(6)
                                .Background(Color.FromHex("#e8eef7"))
                                .Padding(5)
                                .Row(r =>
                                {
                                    r.RelativeItem().Text(group.Category.ToUpper())
                                        .FontSize(9).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                    r.AutoItem().AlignRight()
                                        .Text($"Subtotal: {currency}{group.Subtotal:N2}")
                                        .FontSize(9).Bold().FontColor(Color.FromHex("#1e3a5f"));
                                });

                            content.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(4);
                                    cols.RelativeColumn(1);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(2);
                                });

                                static IContainer Hdr(IContainer c) =>
                                    c.Background(Color.FromHex("#1e3a5f")).Padding(4).AlignMiddle();

                                table.Header(h =>
                                {
                                    foreach (var lbl in new[] { "SKU", "Item Name", "Unit", "Qty On Hand", "Avg Cost", "Value" })
                                        h.Cell().Element(Hdr).Text(lbl).FontSize(8).Bold().FontColor(Colors.White);
                                });

                                int line = 1;
                                foreach (var item in group.Items)
                                {
                                    bool alt = line++ % 2 == 0;
                                    static IContainer Cell(IContainer c, bool a) =>
                                        c.Background(a ? Color.FromHex("#f8f9fb") : Colors.White)
                                         .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2)
                                         .Padding(3).AlignMiddle();

                                    table.Cell().Element(c => Cell(c, alt)).Text(item.ItemCode).FontSize(8);
                                    table.Cell().Element(c => Cell(c, alt)).Text(item.ItemName).FontSize(8);
                                    table.Cell().Element(c => Cell(c, alt)).Text(item.Unit).FontSize(8);
                                    table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                        .Text(item.QtyOnHand.ToString("N3")).FontSize(8);
                                    table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                        .Text($"{currency}{item.AverageCost:N2}").FontSize(8);
                                    table.Cell().Element(c => Cell(c, alt)).AlignRight()
                                        .Text($"{currency}{item.InventoryValue:N2}").FontSize(9).Bold();
                                }
                            });
                        }

                        content.Item().PaddingTop(10).Row(totRow =>
                        {
                            totRow.RelativeItem(3);
                            totRow.RelativeItem().Column(summary =>
                            {
                                summary.Item().LineHorizontal(1.5f).LineColor(Color.FromHex("#1e3a5f"));
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Total Qty On Hand:").FontSize(10).Bold();
                                    r.AutoItem().Text(vm.GrandTotalQty.ToString("N3")).FontSize(10).Bold();
                                });
                                summary.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Grand Total Value:").FontSize(12).Bold()
                                        .FontColor(Color.FromHex("#1e3a5f"));
                                    r.AutoItem().Text($"{currency}{vm.GrandTotal:N2}").FontSize(14).Bold()
                                        .FontColor(Color.FromHex("#1e3a5f"));
                                });
                            });
                        });
                    });

                    page.Footer().Row(foot =>
                    {
                        foot.RelativeItem().Text($"Printed: {DateTime.Now:MMM dd, yyyy hh:mm tt}")
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

        public byte[] GenerateSimpleReportPdf(
            string reportTitle,
            string dateRange,
            List<string> headers,
            List<List<string>> rows,
            string? tenantName  = null,
            string? branchName  = null)
        {
            var bizName = tenantName ?? "HardBuild POS";

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
                        col.Item().Row(r =>
                        {
                            r.RelativeItem(2).Column(left =>
                            {
                                left.Item().Text(bizName)
                                    .FontSize(16).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));

                                if (!string.IsNullOrWhiteSpace(branchName))
                                    left.Item().Text($"Branch: {branchName}")
                                        .FontSize(9).FontColor(Colors.Grey.Darken1);
                            });

                            r.RelativeItem().Column(right =>
                            {
                                right.Item().AlignRight().Text(reportTitle)
                                    .FontSize(13).Bold()
                                    .FontColor(Color.FromHex("#1e3a5f"));

                                right.Item().AlignRight().Text(dateRange)
                                    .FontSize(9).FontColor(Colors.Grey.Darken1);

                                right.Item().AlignRight()
                                    .Text($"Generated: {DateTime.Now:MMM dd, yyyy hh:mm tt}")
                                    .FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                        });

                        col.Item().PaddingTop(4)
                            .LineHorizontal(2).LineColor(Color.FromHex("#1e3a5f"));
                    });

                    // ── Content table ────────────────────────────────────
                    page.Content().PaddingVertical(12).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            foreach (var _ in headers)
                                columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            foreach (var h in headers)
                            {
                                header.Cell()
                                    .Background(Color.FromHex("#1e3a5f"))
                                    .Padding(5)
                                    .Text(h)
                                    .Bold()
                                    .FontSize(9)
                                    .FontColor(Colors.White);
                            }
                        });

                        int rowIdx = 0;
                        foreach (var row in rows)
                        {
                            var bg = rowIdx++ % 2 == 0
                                ? Colors.White
                                : Color.FromHex("#f8f9fb");

                            foreach (var cell in row)
                            {
                                table.Cell()
                                    .Background(bg)
                                    .BorderBottom(0.5f)
                                    .BorderColor(Colors.Grey.Lighten2)
                                    .Padding(5)
                                    .Text(cell)
                                    .FontSize(9);
                            }
                        }
                    });

                    // ── Footer ───────────────────────────────────────────
                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Text(txt =>
                        {
                            txt.Span($"{bizName} · {reportTitle}").FontSize(8)
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
    }
}
