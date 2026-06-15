using HardwareManagementSystem.Data;
using HardwareManagementSystem.Data.Seeders;
using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// DEV-ONLY demo data seeder used to produce clean, professionally-named sample data
    /// for the user-manual screenshots (Phase UM-1). It seeds into the SHARED
    /// ApplicationDbContext (no dedicated routing) so the demo tenant is simple and stable.
    ///
    /// SAFETY: runs only in Development AND only with the explicit "--seed-demo" argument.
    /// It does NOT start the web server. It is idempotent: if the demo tenant already exists
    /// it does nothing.
    /// </summary>
    public static class DemoManualSeeder
    {
        public const string TenantCode    = "HSC";
        public const string AdminUser     = "owner";
        public const string AdminPassword = "Owner123!";
        public const string CashierUser   = "cashier";
        public const string CashierPass   = "Cashier123!";

        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[SEED] Demo seeder is Development-only. Aborting.");
                return;
            }

            using var scope = app.Services.CreateScope();
            var sp    = scope.ServiceProvider;
            var db    = sp.GetRequiredService<ApplicationDbContext>();
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = sp.GetRequiredService<RoleManager<IdentityRole>>();

            await DbSeeder.SeedAdminAsync(sp);
            await DbSeeder.SeedRolePermissionsAsync(sp);

            if (await db.Tenants.AnyAsync(t => t.Code == TenantCode))
            {
                Console.WriteLine("[SEED] Demo tenant already exists. Nothing to do.");
                return;
            }

            Console.WriteLine("[SEED] Creating demo tenant 'Hardware Supply Co.'…");

            var tenant = new Tenant
            {
                Name = "Hardware Supply Co.", Code = TenantCode, Status = TenantStatus.Active,
                IsActive = true, MaxBranches = 5, MaxUsers = 15, MaxProducts = 1000,
                DatabaseMode = TenantDatabaseMode.Shared,
                CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
            };
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync();
            int tid = tenant.Id;

            db.SystemSettings.Add(new SystemSetting
            {
                TenantId = tid, BusinessName = "Hardware Supply Co.", CurrencySymbol = "₱",
                DefaultVatPercent = 12m, TaxMode = "VAT", ReceiptPaperSize = "80mm",
                ThemeColor = "dark-blue", TIN = "123-456-789-000",
                ReceiptFooter = "Thank you for shopping at Hardware Supply Co.!",
                UpdatedAt = DateTime.Now
            });

            // ── Users: TenantAdmin (owner) + Cashier ──────────────────────────
            foreach (var r in new[] { "TenantAdmin", "Cashier" })
                if (!await roles.RoleExistsAsync(r)) await roles.CreateAsync(new IdentityRole(r));

            var owner = new ApplicationUser
            {
                UserName = AdminUser, Email = "owner@hardwaresupply.local", FullName = "Maria Santos",
                EmailConfirmed = true, IsActive = true, TenantId = tid
            };
            if ((await users.CreateAsync(owner, AdminPassword)).Succeeded)
                await users.AddToRoleAsync(owner, "TenantAdmin");

            var cashier = new ApplicationUser
            {
                UserName = CashierUser, Email = "cashier@hardwaresupply.local", FullName = "Jose Reyes",
                EmailConfirmed = true, IsActive = true, TenantId = tid
            };
            if ((await users.CreateAsync(cashier, CashierPass)).Succeeded)
                await users.AddToRoleAsync(cashier, "Cashier");

            // ── Branches ──────────────────────────────────────────────────────
            var main = new Branch { TenantId = tid, Name = "Main Branch", Code = "MAIN", Address = "123 Rizal Ave, Manila", ContactNumber = "02-8123-4567", ManagerName = "Maria Santos", IsMainBranch = true, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            var quezon = new Branch { TenantId = tid, Name = "Quezon City Branch", Code = "QC", Address = "456 Commonwealth Ave, QC", ContactNumber = "02-8765-4321", ManagerName = "Pedro Cruz", IsMainBranch = false, IsActive = true, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow };
            db.Branches.AddRange(main, quezon);

            // ── Categories & Units ─────────────────────────────────────────────
            var cPower = new Category { TenantId = tid, CategoryName = "Power Tools", IsActive = true, CreatedAt = DateTime.Now };
            var cPlumb = new Category { TenantId = tid, CategoryName = "Plumbing", IsActive = true, CreatedAt = DateTime.Now };
            var cElec  = new Category { TenantId = tid, CategoryName = "Electrical", IsActive = true, CreatedAt = DateTime.Now };
            var cFast  = new Category { TenantId = tid, CategoryName = "Fasteners", IsActive = true, CreatedAt = DateTime.Now };
            db.Categories.AddRange(cPower, cPlumb, cElec, cFast);

            var uPc  = new Unit { TenantId = tid, UnitName = "Piece", ShortName = "pc", IsActive = true, CreatedAt = DateTime.Now };
            var uBox = new Unit { TenantId = tid, UnitName = "Box", ShortName = "box", IsActive = true, CreatedAt = DateTime.Now };
            var uM   = new Unit { TenantId = tid, UnitName = "Meter", ShortName = "m", IsActive = true, CreatedAt = DateTime.Now };
            db.Units.AddRange(uPc, uBox, uM);

            // ── Suppliers & Customers ──────────────────────────────────────────
            var sup1 = new Supplier { TenantId = tid, SupplierName = "Metro Tools Trading", ContactPerson = "Ana Lim", ContactNumber = "0917-111-2222", Email = "sales@metrotools.local", Address = "Pasig City", IsActive = true, CreatedAt = DateTime.Now };
            var sup2 = new Supplier { TenantId = tid, SupplierName = "Prime Builders Supply", ContactPerson = "Carlos Tan", ContactNumber = "0918-333-4444", Email = "orders@primebuilders.local", Address = "Caloocan City", IsActive = true, CreatedAt = DateTime.Now };
            db.Suppliers.AddRange(sup1, sup2);

            var cust1 = new Customer { TenantId = tid, CustomerName = "JRC Construction", ContactNumber = "0920-555-6666", Email = "info@jrc.local", Address = "Makati City", CustomerType = "Regular", IsActive = true, CreatedAt = DateTime.Now };
            var cust2 = new Customer { TenantId = tid, CustomerName = "Walk-in Customer", CustomerType = "Walk-in", IsActive = true, CreatedAt = DateTime.Now };
            db.Customers.AddRange(cust1, cust2);

            await db.SaveChangesAsync();

            // ── Items ──────────────────────────────────────────────────────────
            Item NewItem(string code, string name, Category cat, Unit unit, Supplier sup, decimal cost, decimal sell, decimal stock, decimal reorder) =>
                new() { TenantId = tid, ItemCode = code, ItemName = name, CategoryId = cat.Id, UnitId = unit.Id, BaseUnitId = unit.Id, SupplierId = sup.Id, CostPrice = cost, SellingPrice = sell, CurrentStock = stock, ReorderLevel = reorder, Status = "Active", CreatedAt = DateTime.Now };

            var items = new List<Item>
            {
                NewItem("PWR-0001", "Cordless Drill 18V", cPower, uPc, sup1, 1800m, 2750m, 24m, 5m),
                NewItem("PWR-0002", "Angle Grinder 4\"", cPower, uPc, sup1, 1200m, 1850m, 18m, 5m),
                NewItem("PWR-0003", "Circular Saw 7-1/4\"", cPower, uPc, sup1, 2400m, 3600m, 9m, 4m),
                NewItem("PLM-0001", "PVC Pipe 1/2\"", cPlumb, uM, sup2, 35m, 60m, 320m, 50m),
                NewItem("PLM-0002", "Ball Valve 1/2\"", cPlumb, uPc, sup2, 90m, 150m, 60m, 10m),
                NewItem("ELE-0001", "THHN Wire 3.5mm", cElec, uM, sup2, 28m, 48m, 500m, 100m),
                NewItem("ELE-0002", "Circuit Breaker 20A", cElec, uPc, sup2, 220m, 360m, 40m, 8m),
                NewItem("FST-0001", "Concrete Nail 3\" (Box)", cFast, uBox, sup1, 110m, 175m, 75m, 15m),
            };
            db.Items.AddRange(items);
            await db.SaveChangesAsync();

            // Branch stock for the main branch (so POS shows availability)
            foreach (var it in items)
                db.BranchProductStocks.Add(new BranchProductStock { TenantId = tid, BranchId = main.Id, ProductId = it.Id, Quantity = it.CurrentStock, UpdatedAtUtc = DateTime.UtcNow });
            await db.SaveChangesAsync();

            // ── A few completed sales + an expense so reports/dashboard render ──
            SalesHeader NewSale(string no, Customer cust, decimal total, (Item item, decimal qty, decimal price)[] lines, DateTime when)
            {
                var sh = new SalesHeader
                {
                    TenantId = tid, SalesNumber = no, SalesDate = when, CustomerId = cust.Id, BranchId = main.Id,
                    CashierName = "Jose Reyes", SubTotal = total, TotalAmount = total, AmountReceived = total,
                    ChangeAmount = 0m, PaymentMethod = "Cash", Status = "Completed", CreatedAt = when
                };
                foreach (var (item, qty, price) in lines)
                    sh.SalesDetails.Add(new SalesDetail { ItemId = item.Id, Quantity = qty, UnitPrice = price, LineTotal = qty * price });
                return sh;
            }

            var today = DateTime.Now;
            db.SalesHeaders.Add(NewSale("INV-1001", cust2, 2750m, new[] { (items[0], 1m, 2750m) }, today.AddDays(-2)));
            db.SalesHeaders.Add(NewSale("INV-1002", cust1, 1200m, new[] { (items[3], 20m, 60m) }, today.AddDays(-1)));
            db.SalesHeaders.Add(NewSale("INV-1003", cust2, 3700m, new[] { (items[1], 2m, 1850m) }, today));
            db.Expenses.Add(new Expense { TenantId = tid, BranchId = main.Id, ExpenseNumber = "EXP-1001", ExpenseDate = today.AddDays(-1), Category = "Utilities", Description = "Electricity bill", Amount = 4200m, PaymentMethod = "Cash", CreatedBy = AdminUser, CreatedAt = today.AddDays(-1) });
            await db.SaveChangesAsync();

            Console.WriteLine($"[SEED] Demo tenant created (TenantId={tid}). Admin: {AdminUser}/{AdminPassword}, Cashier: {CashierUser}/{CashierPass}.");
        }
    }
}
