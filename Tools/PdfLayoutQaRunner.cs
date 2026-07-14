using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.Pdf;
using HardwareManagementSystem.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Tools
{
    /// <summary>Layout stress matrix for PO and Supplier Statement PDFs. Run with --qa-pdf-layout</summary>
    public static class PdfLayoutQaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var pdf = sp.GetRequiredService<DocumentPdfService>();
            var appDb = sp.GetRequiredService<ApplicationDbContext>();

            var tenant = await appDb.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.RoutingEnabled && t.DatabaseMode == TenantDatabaseMode.Dedicated)
                ?? await appDb.Tenants.AsNoTracking().FirstAsync();

            var settings = await appDb.SystemSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TenantId == tenant.Id)
                ?? new SystemSetting { CurrencySymbol = "₱" };

            var logoPath = settings.LogoPath;
            var longName = new string('X', 120) + " — Supplier & Co. ₱";
            var longProduct = "Premium " + new string('Z', 100) + " Widget ½";
            var longRef = "INV-" + new string('9', 40) + "-REF";
            var peso = settings.CurrencySymbol ?? "₱";

            var scenarios = new (string Name, Action Po, Action Sup)[]
            {
                ("1-normal-short", () => GenPo(pdf, settings, tenant, logoPath, shortData: true),
                                   () => GenSup(pdf, settings, tenant, logoPath, shortData: true)),
                ("2-long-supplier-name", () => GenPo(pdf, settings, tenant, logoPath, supplierName: longName),
                                         () => GenSup(pdf, settings, tenant, logoPath, supplierName: longName)),
                ("3-long-product-name", () => GenPo(pdf, settings, tenant, logoPath, productName: longProduct),
                                        () => GenSup(pdf, settings, tenant, logoPath)),
                ("4-long-invoice-ref", () => GenPo(pdf, settings, tenant, logoPath, notes: longRef),
                                       () => GenSup(pdf, settings, tenant, logoPath, reference: longRef)),
                ("5-large-numeric", () => GenPo(pdf, settings, tenant, logoPath, qty: 999999.999m, cost: 9999999.99m),
                                    () => GenSup(pdf, settings, tenant, logoPath, amount: 999999999.99m)),
                ("6-many-rows", () => GenPo(pdf, settings, tenant, logoPath, lineCount: 45),
                                () => GenSup(pdf, settings, tenant, logoPath, lineCount: 45)),
                ("7-empty-optional", () => GenPo(pdf, settings, tenant, logoPath, emptyOptional: true),
                                     () => GenSup(pdf, settings, tenant, logoPath, emptyOptional: true)),
                ("8-special-chars-peso", () => GenPo(pdf, settings, tenant, logoPath, supplierName: $"Ñoño ₱ {peso} & <test>"),
                                          () => GenSup(pdf, settings, tenant, logoPath, supplierName: $"Ñoño ₱ {peso} & <test>")),
                ("9-dedicated-tenant-logo", () => GenPo(pdf, settings, tenant, logoPath),
                                            () => GenSup(pdf, settings, tenant, logoPath)),
            };

            int pass = 0, fail = 0;
            foreach (var (name, poAct, supAct) in scenarios)
            {
                try
                {
                    poAct();
                    supAct();
                    Console.WriteLine($"  [PASS] {name}");
                    pass++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [FAIL] {name}: {ex.GetType().Name} — {ex.Message}");
                    fail++;
                }
            }

            Console.WriteLine($"\n[QA-PDF] Done — {pass} passed, {fail} failed.");
            Environment.ExitCode = fail > 0 ? 1 : 0;
        }

        private static void GenPo(
            DocumentPdfService pdf, SystemSetting settings, Tenant tenant, string? logo,
            bool shortData = false, string? supplierName = null, string? productName = null,
            string? notes = null, decimal qty = 10, decimal cost = 50, int lineCount = 3,
            bool emptyOptional = false)
        {
            var po = new PurchaseOrder
            {
                PONumber = shortData ? "PO-001" : "PO-STRESS-RC184",
                PODate = DateTime.Today,
                ExpectedDeliveryDate = emptyOptional ? null : DateTime.Today.AddDays(7),
                Status = "Sent",
                Notes = emptyOptional ? null : (notes ?? "Standard remarks"),
                Supplier = new Supplier
                {
                    SupplierName = supplierName ?? (shortData ? "Acme Supply" : "Default Supplier Name"),
                    ContactNumber = emptyOptional ? null : "09171234567",
                    Email = emptyOptional ? null : "buyer@example.com",
                    Address = emptyOptional ? null : "123 Main St"
                },
                Branch = new Branch { Name = emptyOptional ? "Main" : "Warehouse Branch North" }
            };

            for (int i = 0; i < lineCount; i++)
            {
                po.Items.Add(new PurchaseOrderItem
                {
                    Id = i + 1,
                    Quantity = qty,
                    UnitCost = cost,
                    Item = new Item
                    {
                        ItemName = productName ?? $"Product line {i + 1}",
                        ItemCode = $"SKU-{i + 1:D4}",
                        Unit = new Unit { ShortName = emptyOptional ? "pc" : "box" }
                    }
                });
            }

            var bytes = pdf.GeneratePurchaseOrderPdf(po, settings, tenant, logo);
            if (bytes.Length < 500) throw new InvalidOperationException("PO PDF too small");
        }

        private static void GenSup(
            DocumentPdfService pdf, SystemSetting settings, Tenant tenant, string? logo,
            bool shortData = false, string? supplierName = null, string? reference = null,
            decimal amount = 1000, int lineCount = 5, bool emptyOptional = false)
        {
            var vm = new SupplierStatementViewModel
            {
                Supplier = new Supplier
                {
                    SupplierName = supplierName ?? (shortData ? "Acme Supply" : "Default Supplier"),
                    ContactNumber = emptyOptional ? null : "09171234567",
                    Email = emptyOptional ? null : "ap@example.com",
                    Address = emptyOptional ? null : "456 Vendor Ave"
                },
                DateFrom = DateTime.Today.AddMonths(-1),
                DateTo = DateTime.Today,
                OpeningBalance = 0,
                TotalPurchases = amount * lineCount,
                TotalPayments = emptyOptional ? 0 : amount / 2,
                ClosingBalance = amount * lineCount - (emptyOptional ? 0 : amount / 2),
                Lines = Enumerable.Range(1, lineCount).Select(i => new SupplierStatementLine
                {
                    Date = DateTime.Today.AddDays(-i),
                    Type = i % 2 == 0 ? "Payment" : "Purchase",
                    ReferenceNo = reference ?? (shortData ? $"REF-{i}" : $"REF-{i:D4}"),
                    Description = emptyOptional ? "" : $"Line description {i}",
                    Debit = i % 2 == 0 ? 0 : amount,
                    Credit = i % 2 == 0 ? amount / 2 : 0,
                    RunningBalance = amount * i
                }).ToList()
            };

            var bytes = pdf.GenerateSupplierStatementPdf(vm, settings, tenant, logo);
            if (bytes.Length < 500) throw new InvalidOperationException("Supplier PDF too small");
        }
    }
}
