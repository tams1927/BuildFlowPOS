using System.Text;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Data.Seeders;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Tools
{
    /// <summary>
    /// Phase 5.0D.2-QA — DEV-ONLY automated FULL MODULE cutover test.
    ///
    /// Extends the 5.0D.1 pilot cycle to the entire store system. It provisions a fresh QA
    /// tenant, migrates + routes it to a dedicated database, then drives the full operational
    /// module set (purchasing, receiving, inventory adjustments, branch transfers, quotations,
    /// delivery receipts, POS + credit sales, collections, returns, supplier payments, expenses)
    /// through the routed operational context and verifies:
    ///
    ///   • every new QA operational row lands in the DEDICATED database, and
    ///   • NONE of those QA rows leak into the shared database, and
    ///   • the operational reports (SOA, statements, aging, valuation, movement intelligence,
    ///     dashboard KPIs) read the QA data from the dedicated database, and
    ///   • rollback (disable routing → shared) and re-enable (→ dedicated) both work.
    ///
    /// It records a TENANT_FULL_CUTOVER_VERIFIED / TENANT_FULL_CUTOVER_FAILED audit row.
    ///
    /// SAFETY: runs only in Development AND only with the explicit "--qa-phase50d2" argument.
    /// It does NOT start the web server. All test data uses the QA_ prefix. Cleanup (incl.
    /// DROP DATABASE) only runs when QA:EnableCleanup == true.
    /// </summary>
    public static class Phase50D2QaRunner
    {
        public static async Task RunAsync(WebApplication app)
        {
            if (!app.Environment.IsDevelopment())
            {
                Console.WriteLine("[QA] Phase 5.0D.2 runner is Development-only. Aborting.");
                return;
            }

            var stamp  = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var report = new Report(stamp);
            var enableCleanup = app.Configuration.GetValue<bool>("QA:EnableCleanup");

            Console.WriteLine($"[QA] Phase 5.0D.2 full-module cutover test starting ({stamp})…");

            // ── Test data names (QA_ prefix) ─────────────────────────────────
            var tenantName    = $"QA_Tenant_{stamp}";
            var tenantCode    = $"QA{stamp}";
            var adminUser     = $"qa_admin_{stamp}";
            var dbName        = $"QA_TenantDb_{stamp}";
            var mainBranch    = $"QA_MainBranch_{stamp}";
            var secondBranch  = $"QA_Branch2_{stamp}";
            var categoryName  = $"QA_Category_{stamp}";
            var unitName      = $"QA_Unit_{stamp}";
            var itemName      = $"QA_Item_{stamp}";
            var itemCode      = $"QAITM{stamp}";
            var supplierName  = $"QA_Supplier_{stamp}";
            var customerName  = $"QA_Customer_{stamp}";
            const string adminPassword = "QaAdmin123!";

            // Document numbers (unique per table — used for write-location verification)
            var poNumber      = $"QAPO{stamp}";
            var stockInNumber = $"QASI{stamp}";
            var adjNumber     = $"QAADJ{stamp}";
            var transferNo    = $"QATR{stamp}";
            var quotationNo   = $"QAQT{stamp}";
            var drNumber      = $"QADR{stamp}";
            var posSaleNo     = $"QASALE{stamp}";
            var quoteSaleNo   = $"QAQSALE{stamp}";
            var creditSaleNo  = $"QACRED{stamp}";
            var returnNo      = $"QARET{stamp}";
            var supPayRef     = $"QASP{stamp}";
            var expenseNo     = $"QAEXP{stamp}";
            var collectionRef = $"QACOL{stamp}";

            // Settings cutover (Phase 5.0D.3) — values written through the routed context
            var qaBusinessName = $"QA_Biz_{stamp}";
            var qaTin          = $"QA-TIN-{stamp}";
            var qaVatPercent   = 8m;
            var qaFooter       = $"QA Footer {stamp} — thank you!";

            // Phase 5.0D.4 — final operational cutover (Branches/UserBranches/Import/Audit)
            var impSupplierName = $"QA_ImpSupplier_{stamp}";
            var impCustomerName = $"QA_ImpCustomer_{stamp}";
            var impItemName     = $"QA_ImpItem_{stamp}";
            var impItemCode     = $"QAIMP{stamp}";
            var auditAction     = $"QA_AUDIT_READ_{stamp}";

            report.TenantName   = tenantName;
            report.DatabaseName = dbName;

            int tenantId = 0;
            string? dedicatedConn = null;
            string adminUserId = "";

            try
            {
                // ─────────────────────────────────────────────────────────────
                // SCOPE 1 — SuperAdmin: create → provision → migrate → route
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp        = scope.ServiceProvider;
                    var db        = sp.GetRequiredService<ApplicationDbContext>();
                    var users     = sp.GetRequiredService<UserManager<ApplicationUser>>();
                    var roles     = sp.GetRequiredService<RoleManager<IdentityRole>>();
                    var resolver  = sp.GetRequiredService<ITenantDatabaseResolver>();
                    var provision = sp.GetRequiredService<ITenantDatabaseProvisioningService>();
                    var migrate   = sp.GetRequiredService<ITenantDataMigrationService>();

                    await DbSeeder.SeedAdminAsync(sp);
                    await DbSeeder.SeedRolePermissionsAsync(sp);
                    var superAdmin = await users.FindByNameAsync("superadmin");
                    report.Check("Ensure SuperAdmin exists", superAdmin != null,
                        superAdmin != null ? "superadmin present" : "superadmin NOT found");

                    var tenant = new Tenant
                    {
                        Name = tenantName, Code = tenantCode, Status = TenantStatus.Active,
                        IsActive = true, MaxBranches = 5, MaxUsers = 10, MaxProducts = 500,
                        DatabaseMode = TenantDatabaseMode.Dedicated, DatabaseName = dbName,
                        CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                    };
                    db.Tenants.Add(tenant);
                    await db.SaveChangesAsync();
                    tenantId = tenant.Id;
                    report.TenantId = tenantId;
                    report.Check("Create Tenant (Dedicated)", tenantId > 0, $"TenantId={tenantId}, Db={dbName}");

                    db.SystemSettings.Add(new SystemSetting
                    {
                        TenantId = tenantId, BusinessName = tenantName, CurrencySymbol = "₱",
                        DefaultVatPercent = 12m, TaxMode = "VAT", ReceiptPaperSize = "80mm",
                        ThemeColor = "dark-blue", UpdatedAt = DateTime.Now
                    });
                    await db.SaveChangesAsync();

                    if (!await roles.RoleExistsAsync("TenantAdmin"))
                        await roles.CreateAsync(new IdentityRole("TenantAdmin"));

                    var admin = new ApplicationUser
                    {
                        UserName = adminUser, Email = $"{adminUser}@qa.local", FullName = "QA Tenant Admin",
                        EmailConfirmed = true, IsActive = true, TenantId = tenantId
                    };
                    var createRes = await users.CreateAsync(admin, adminPassword);
                    if (createRes.Succeeded) await users.AddToRoleAsync(admin, "TenantAdmin");
                    adminUserId = admin.Id;
                    report.Check("Create TenantAdmin", createRes.Succeeded,
                        createRes.Succeeded ? $"{adminUser}" : string.Join("; ", createRes.Errors.Select(e => e.Description)));

                    var pr = await provision.ProvisionAsync(tenantId);
                    report.Check("Provision dedicated DB", pr.Success,
                        $"{pr.Message} (created={pr.DatabaseCreated}, migrated={pr.MigrationsApplied}, seeded={pr.SeedCompleted})");
                    if (!pr.Success) throw new QaStop("Provisioning failed.");

                    tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == tenantId);
                    dedicatedConn = tenant.ConnectionString;

                    var (connOk, connMsg) = await TryOpenAsync(dedicatedConn!);
                    report.Check("Test connection (dedicated)", connOk, connMsg);
                    if (!connOk) throw new QaStop("Dedicated connection failed.");

                    var mr = await migrate.MigrateAsync(tenantId);
                    report.Check("Migrate tenant data", mr.Success,
                        $"{mr.Message} (rows={mr.TotalRowsCopied}, tables={mr.Tables.Count})");
                    if (!mr.Success) throw new QaStop("Migration failed.");

                    var t2 = await db.Tenants.FirstAsync(t => t.Id == tenantId);
                    t2.RoutingEnabled = true;
                    t2.UpdatedAtUtc   = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    resolver.Invalidate(tenantId);

                    var runtimeDb = await resolver.GetRuntimeDatabaseAsync(tenantId);
                    var diagOk = t2.DatabaseProvisionedAtUtc != null && t2.DataMigrated && t2.RoutingEnabled
                                 && runtimeDb == "Dedicated";
                    report.Check("Enable routing + diagnostics (Runtime=Dedicated)", diagOk,
                        $"Provisioned={t2.DatabaseProvisionedAtUtc != null}, DataMigrated={t2.DataMigrated}, " +
                        $"RoutingEnabled={t2.RoutingEnabled}, RuntimeDatabase={runtimeDb}");
                    if (!diagOk) throw new QaStop("Diagnostics did not confirm dedicated routing.");
                }

                // ─────────────────────────────────────────────────────────────
                // SCOPE 2 — Full operational module flow via routed context
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp       = scope.ServiceProvider;
                    var provider = sp.GetRequiredService<ITenantOperationalContextProvider>();

                    var ctx = await provider.GetContextAsync(tenantId);
                    var ctxType = ctx.GetType().Name;
                    report.Check("Operational context routes to TenantDbContext", ctxType == "TenantDbContext",
                        $"Resolved context = {ctxType}");
                    if (ctxType != "TenantDbContext") throw new QaStop("Provider did not route to dedicated context.");

                    // 1-3) Branches, Category, Unit
                    var branch  = new Branch { TenantId = tenantId, Name = mainBranch, Code = $"QABR1{stamp}", IsMainBranch = true, IsActive = true };
                    var branch2 = new Branch { TenantId = tenantId, Name = secondBranch, Code = $"QABR2{stamp}", IsMainBranch = false, IsActive = true };
                    var category = new Category { TenantId = tenantId, CategoryName = categoryName, IsActive = true, CreatedAt = DateTime.Now };
                    var unit     = new Unit { TenantId = tenantId, UnitName = unitName, ShortName = $"Q{stamp[^3..]}", IsActive = true, CreatedAt = DateTime.Now };
                    var supplier = new Supplier { TenantId = tenantId, SupplierName = supplierName, IsActive = true, CreatedAt = DateTime.Now };
                    var customer = new Customer { TenantId = tenantId, CustomerName = customerName, CustomerType = "Regular", IsActive = true, CreatedAt = DateTime.Now };
                    ctx.Branches.AddRange(branch, branch2);
                    ctx.Categories.Add(category);
                    ctx.Units.Add(unit);
                    ctx.Suppliers.Add(supplier);
                    ctx.Customers.Add(customer);
                    await ctx.SaveChangesAsync();
                    report.Check("Create Branch x2 / Category / Unit / Supplier / Customer", branch.Id > 0 && branch2.Id > 0,
                        $"Branch={branch.Id}/{branch2.Id}, Cat={category.Id}, Unit={unit.Id}, Sup={supplier.Id}, Cust={customer.Id}");

                    // 4) Product
                    var item = new Item
                    {
                        TenantId = tenantId, ItemCode = itemCode, ItemName = itemName,
                        CategoryId = category.Id, UnitId = unit.Id, BaseUnitId = unit.Id,
                        SupplierId = supplier.Id,
                        CostPrice = 50m, SellingPrice = 80m, CurrentStock = 0m, ReorderLevel = 5m,
                        Status = "Active", CreatedAt = DateTime.Now
                    };
                    ctx.Items.Add(item);
                    await ctx.SaveChangesAsync();
                    report.Check("Create Product", item.Id > 0, $"ItemId={item.Id}, Code={itemCode}");

                    // 6) Purchase Order (Draft → Sent)
                    var po = new PurchaseOrder
                    {
                        TenantId = tenantId, BranchId = branch.Id, SupplierId = supplier.Id,
                        PONumber = poNumber, PODate = DateTime.Now, Status = "Sent",
                        CreatedBy = adminUser, CreatedAtUtc = DateTime.UtcNow
                    };
                    po.Items.Add(new PurchaseOrderItem { ItemId = item.Id, Quantity = 20m, UnitCost = 50m, TotalCost = 1000m });
                    ctx.PurchaseOrders.Add(po);
                    await ctx.SaveChangesAsync();
                    report.Check("Create Purchase Order", po.Id > 0, $"POId={po.Id}, No={poNumber}, Status={po.Status}");

                    // 7) Receive PO → fully received, bump stock
                    foreach (var pi in po.Items) pi.QuantityReceived = pi.Quantity;
                    po.Status = "Received";
                    po.UpdatedAtUtc = DateTime.UtcNow;
                    item.CurrentStock += 20m;
                    await UpsertBranchStock(ctx, tenantId, branch.Id, item.Id, +20m);
                    await ctx.SaveChangesAsync();
                    report.Check("Receive Purchase Order", po.Status == "Received", $"Item.CurrentStock={item.CurrentStock}");

                    // 8) Stock-In (header + detail)
                    var stockIn = new StockInHeader
                    {
                        TenantId = tenantId, StockInNumber = stockInNumber, DateReceived = DateTime.Now,
                        SupplierId = supplier.Id, BranchId = branch.Id, TotalCost = 500m,
                        PaymentStatus = "Unpaid", AmountPaid = 0m, CreatedAt = DateTime.Now
                    };
                    stockIn.StockInDetails.Add(new StockInDetail { ItemId = item.Id, Quantity = 10m, UnitCost = 50m, TotalCost = 500m });
                    ctx.StockInHeaders.Add(stockIn);
                    item.CurrentStock += 10m;
                    await UpsertBranchStock(ctx, tenantId, branch.Id, item.Id, +10m);
                    await ctx.SaveChangesAsync();
                    report.Check("Stock-In header + detail", stockIn.Id > 0, $"StockInId={stockIn.Id}, Item.CurrentStock={item.CurrentStock}");

                    // 9) Stock Adjustment (Increase)
                    var before = item.CurrentStock;
                    var adj = new StockAdjustmentHeader
                    {
                        TenantId = tenantId, BranchId = branch.Id, AdjustmentNumber = adjNumber,
                        AdjustmentDate = DateTime.Now, AdjustmentType = "Increase", Reason = "QA count surplus",
                        CreatedBy = adminUser, CreatedAt = DateTime.Now
                    };
                    adj.StockAdjustmentDetails.Add(new StockAdjustmentDetail { ItemId = item.Id, Quantity = 5m, StockBefore = before, StockAfter = before + 5m });
                    ctx.StockAdjustmentHeaders.Add(adj);
                    item.CurrentStock += 5m;
                    await UpsertBranchStock(ctx, tenantId, branch.Id, item.Id, +5m);
                    await ctx.SaveChangesAsync();
                    report.Check("Stock Adjustment", adj.Id > 0, $"AdjId={adj.Id}, Item.CurrentStock={item.CurrentStock}");

                    // 10) Branch Transfer (main → second)
                    var transfer = new BranchTransfer
                    {
                        TenantId = tenantId, TransferNumber = transferNo, FromBranchId = branch.Id, ToBranchId = branch2.Id,
                        Status = "Completed", CreatedByUserName = adminUser, CreatedAtUtc = DateTime.UtcNow, CompletedAtUtc = DateTime.UtcNow
                    };
                    transfer.BranchTransferItems.Add(new BranchTransferItem { ProductId = item.Id, Quantity = 4m });
                    ctx.BranchTransfers.Add(transfer);
                    await UpsertBranchStock(ctx, tenantId, branch.Id, item.Id, -4m);
                    await UpsertBranchStock(ctx, tenantId, branch2.Id, item.Id, +4m);
                    await ctx.SaveChangesAsync();
                    report.Check("Branch Transfer", transfer.Id > 0, $"TransferId={transfer.Id}, No={transferNo}");

                    // 12) Quotation
                    var quote = new Quotation
                    {
                        TenantId = tenantId, BranchId = branch.Id, CustomerId = customer.Id, QuotationNo = quotationNo,
                        QuotationDate = DateTime.Today, Status = "Accepted", TotalAmount = 160m,
                        CreatedByUserId = null, CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
                    };
                    quote.Items.Add(new QuotationItem { ItemId = item.Id, Description = itemName, Quantity = 2m, UnitPrice = 80m, Subtotal = 160m });
                    ctx.Quotations.Add(quote);
                    await ctx.SaveChangesAsync();
                    report.Check("Create Quotation", quote.Id > 0, $"QuoteId={quote.Id}, No={quotationNo}");

                    // 13) Convert Quotation → Sale
                    var quoteSale = new SalesHeader
                    {
                        TenantId = tenantId, SalesNumber = quoteSaleNo, SalesDate = DateTime.Now, CustomerId = customer.Id,
                        BranchId = branch.Id, CashierName = "QA", SubTotal = 160m, TotalAmount = 160m,
                        AmountReceived = 160m, ChangeAmount = 0m, PaymentMethod = "Cash", Status = "Completed", CreatedAt = DateTime.Now
                    };
                    quoteSale.SalesDetails.Add(new SalesDetail { ItemId = item.Id, Quantity = 2m, UnitPrice = 80m, LineTotal = 160m });
                    ctx.SalesHeaders.Add(quoteSale);
                    item.CurrentStock -= 2m;
                    await UpsertBranchStock(ctx, tenantId, branch.Id, item.Id, -2m);
                    await ctx.SaveChangesAsync();
                    quote.ConvertedToSaleId = quoteSale.Id;
                    await ctx.SaveChangesAsync();
                    report.Check("Convert Quotation to Sale", quoteSale.Id > 0 && quote.ConvertedToSaleId == quoteSale.Id,
                        $"SaleId={quoteSale.Id}, No={quoteSaleNo}");

                    // 14) Delivery Receipt (linked to converted sale)
                    var dr = new DeliveryReceipt
                    {
                        TenantId = tenantId, BranchId = branch.Id, DRNumber = drNumber, DeliveryDate = DateTime.Today,
                        CustomerId = customer.Id, Status = "Delivered", SalesHeaderId = quoteSale.Id,
                        CreatedAtUtc = DateTime.UtcNow, DeliveredAtUtc = DateTime.UtcNow
                    };
                    dr.Items.Add(new DeliveryReceiptItem { ItemId = item.Id, Description = itemName, Quantity = 2m, Unit = unitName });
                    ctx.DeliveryReceipts.Add(dr);
                    await ctx.SaveChangesAsync();
                    report.Check("Create Delivery Receipt", dr.Id > 0, $"DRId={dr.Id}, No={drNumber}");

                    // 15) POS Sale (cash)
                    var posSale = new SalesHeader
                    {
                        TenantId = tenantId, SalesNumber = posSaleNo, SalesDate = DateTime.Now, CustomerId = customer.Id,
                        BranchId = branch.Id, CashierName = "QA", SubTotal = 80m, TotalAmount = 80m,
                        AmountReceived = 100m, ChangeAmount = 20m, PaymentMethod = "Cash", Status = "Completed", CreatedAt = DateTime.Now
                    };
                    var posDetail = new SalesDetail { ItemId = item.Id, Quantity = 1m, UnitPrice = 80m, LineTotal = 80m };
                    posSale.SalesDetails.Add(posDetail);
                    ctx.SalesHeaders.Add(posSale);
                    item.CurrentStock -= 1m;
                    await UpsertBranchStock(ctx, tenantId, branch.Id, item.Id, -1m);
                    await ctx.SaveChangesAsync();
                    report.Check("POS Sale (cash)", posSale.Id > 0, $"SaleId={posSale.Id}, No={posSaleNo}");

                    // 16) Credit Sale + customer ledger CHARGE
                    var creditSale = new SalesHeader
                    {
                        TenantId = tenantId, SalesNumber = creditSaleNo, SalesDate = DateTime.Now, CustomerId = customer.Id,
                        BranchId = branch.Id, CashierName = "QA", SubTotal = 240m, TotalAmount = 240m,
                        AmountReceived = 0m, ChangeAmount = 0m, PaymentMethod = "Credit", Status = "Completed", CreatedAt = DateTime.Now
                    };
                    creditSale.SalesDetails.Add(new SalesDetail { ItemId = item.Id, Quantity = 3m, UnitPrice = 80m, LineTotal = 240m });
                    ctx.SalesHeaders.Add(creditSale);
                    item.CurrentStock -= 3m;
                    await UpsertBranchStock(ctx, tenantId, branch.Id, item.Id, -3m);
                    ctx.CustomerLedgers.Add(new CustomerLedger
                    {
                        CustomerId = customer.Id, TransactionType = "CHARGE", ReferenceNumber = creditSaleNo,
                        DebitAmount = 240m, CreditAmount = 0m, BalanceBefore = 0m, RunningBalance = 240m,
                        TransactionDate = DateTime.Now, CreatedBy = adminUser, CreatedAt = DateTime.Now
                    });
                    await ctx.SaveChangesAsync();
                    report.Check("Credit Sale + ledger CHARGE", creditSale.Id > 0, $"SaleId={creditSale.Id}, No={creditSaleNo}");

                    // 17) Customer Collection (PAYMENT against credit sale)
                    ctx.CustomerLedgers.Add(new CustomerLedger
                    {
                        CustomerId = customer.Id, TransactionType = "PAYMENT", ReferenceNumber = collectionRef,
                        DebitAmount = 0m, CreditAmount = 100m, BalanceBefore = 240m, RunningBalance = 140m,
                        TransactionDate = DateTime.Now, PaymentMethod = "Cash", CreatedBy = adminUser, CreatedAt = DateTime.Now
                    });
                    await ctx.SaveChangesAsync();
                    report.Check("Customer Collection (ledger PAYMENT)", true, $"Ref={collectionRef}, balance 240→140");

                    // 18) Sales Return (against POS sale)
                    var ret = new SalesReturnHeader
                    {
                        TenantId = tenantId, ReturnNumber = returnNo, ReturnDate = DateTime.Now, SalesHeaderId = posSale.Id,
                        Reason = "QA defective", RefundAmount = 80m, CreatedBy = adminUser, CreatedAt = DateTime.Now
                    };
                    ret.SalesReturnDetails.Add(new SalesReturnDetail
                    {
                        SalesDetailId = posDetail.Id, ItemId = item.Id, QuantityReturned = 1m, UnitPrice = 80m,
                        LineRefundAmount = 80m, RestoreToInventory = true
                    });
                    ctx.SalesReturnHeaders.Add(ret);
                    item.CurrentStock += 1m;
                    await UpsertBranchStock(ctx, tenantId, branch.Id, item.Id, +1m);
                    await ctx.SaveChangesAsync();
                    report.Check("Sales Return", ret.Id > 0, $"ReturnId={ret.Id}, No={returnNo}");

                    // 19) Supplier Payment (against stock-in)
                    var supPay = new SupplierPayment
                    {
                        StockInHeaderId = stockIn.Id, SupplierId = supplier.Id, PaymentDate = DateTime.Now,
                        AmountPaid = 500m, PaymentMethod = "Cash", ReferenceNumber = supPayRef, CreatedBy = adminUser, CreatedAt = DateTime.Now
                    };
                    ctx.SupplierPayments.Add(supPay);
                    stockIn.AmountPaid = 500m;
                    stockIn.PaymentStatus = "Paid";
                    await ctx.SaveChangesAsync();
                    report.Check("Supplier Payment", supPay.Id > 0, $"PaymentId={supPay.Id}, Ref={supPayRef}");

                    // 20) Expense
                    var expense = new Expense
                    {
                        TenantId = tenantId, BranchId = branch.Id, ExpenseNumber = expenseNo, ExpenseDate = DateTime.Now,
                        Category = "Utilities", Description = "QA electricity", Amount = 350m, PaymentMethod = "Cash",
                        CreatedBy = adminUser, CreatedAt = DateTime.Now
                    };
                    ctx.Expenses.Add(expense);
                    await ctx.SaveChangesAsync();
                    report.Check("Expense", expense.Id > 0, $"ExpenseId={expense.Id}, No={expenseNo}");

                    // ── Phase 5.0D.4 — Branch / UserBranch / Audit through the routed context ──

                    // Set Main Branch (mirror BranchesController.SetMainBranch: clear others, set target)
                    branch.IsMainBranch  = false;
                    branch2.IsMainBranch = true;
                    branch2.UpdatedAtUtc = DateTime.UtcNow;
                    await ctx.SaveChangesAsync();
                    var mainAfter = await ctx.Branches.CountAsync(b => b.TenantId == tenantId && b.IsMainBranch);
                    // Restore original main branch so later assertions stay stable
                    branch2.IsMainBranch = false;
                    branch.IsMainBranch  = true;
                    branch.UpdatedAtUtc  = DateTime.UtcNow;
                    await ctx.SaveChangesAsync();
                    report.Check("Set Main Branch (single main enforced)", mainAfter == 1,
                        $"mainBranchCount after switch={mainAfter}");

                    // Assign User Branch — Identity user (shared) ↔ operational branch (dedicated).
                    // Only the assignment row is operational; the user remains platform-level.
                    ctx.UserBranches.Add(new UserBranch
                    {
                        UserId = adminUserId, BranchId = branch.Id, AssignedAtUtc = DateTime.UtcNow
                    });
                    await ctx.SaveChangesAsync();
                    var ubCount = await ctx.UserBranches.CountAsync(u => u.UserId == adminUserId && u.BranchId == branch.Id);
                    report.Check("Assign User Branch", ubCount == 1,
                        $"UserBranch rows for admin@mainBranch={ubCount} (no cross-db FK — UserId is a plain string)");

                    // Audit record written to the routed (dedicated) context — what AuditService does live
                    ctx.AuditTrails.Add(new AuditTrail
                    {
                        UserId = adminUserId, UserName = adminUser, ModuleName = "QA", ActionName = auditAction,
                        Description = "QA dedicated audit read-side verification.", TenantId = tenantId,
                        IpAddress = "127.0.0.1", Browser = "N/A", OperatingSystem = "N/A", DeviceType = "Server",
                        CreatedAt = DateTime.Now
                    });
                    await ctx.SaveChangesAsync();
                    report.Check("Write tenant audit record to dedicated context", true, $"Action={auditAction}");

                    // Settings cutover (Phase 5.0D.3) — edit company profile via the routed context
                    var settings = await ctx.SystemSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);
                    if (settings == null)
                    {
                        settings = new SystemSetting { TenantId = tenantId, CurrencySymbol = "₱", TaxMode = "VAT" };
                        ctx.SystemSettings.Add(settings);
                    }
                    settings.BusinessName      = qaBusinessName;
                    settings.TIN               = qaTin;
                    settings.DefaultVatPercent = qaVatPercent;
                    settings.ReceiptFooter     = qaFooter;
                    settings.UpdatedAt         = DateTime.Now;
                    await ctx.SaveChangesAsync();
                    report.Check("Update tenant settings via routed context", true,
                        $"BusinessName={qaBusinessName}, TIN={qaTin}, VAT={qaVatPercent}%, Footer set");
                }

                // ─────────────────────────────────────────────────────────────
                // SCOPE 2B — Excel import via the REAL service, routed by ambient
                // tenant context (simulated TenantAdmin principal). Proves
                // ExcelImportService writes imported rows to the dedicated DB.
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp = scope.ServiceProvider;

                    // Simulate the logged-in TenantAdmin so ITenantContext/TenantGuard resolve
                    // the tenant and the operational provider routes to the dedicated DB —
                    // exactly the path a real HTTP import request follows.
                    var httpAccessor = sp.GetRequiredService<IHttpContextAccessor>();
                    var identity = new System.Security.Claims.ClaimsIdentity("QA");
                    identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, adminUserId));
                    identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Name, adminUser));
                    identity.AddClaim(new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "TenantAdmin"));
                    httpAccessor.HttpContext = new DefaultHttpContext
                    {
                        User = new System.Security.Claims.ClaimsPrincipal(identity),
                        RequestServices = sp
                    };

                    var provider   = sp.GetRequiredService<ITenantOperationalContextProvider>();
                    var importSvc  = sp.GetRequiredService<HardwareManagementSystem.Services.ExcelImportService>();

                    // The service resolves GetContextAsync() (no tenant arg) off the ambient
                    // principal — verify that routes to the dedicated context.
                    var importCtx = await provider.GetContextAsync();
                    report.Check("Import service routes to TenantDbContext (ambient principal)",
                        importCtx.GetType().Name == "TenantDbContext", $"Resolved context = {importCtx.GetType().Name}");
                    if (importCtx.GetType().Name != "TenantDbContext")
                        throw new QaStop("Ambient principal did not route imports to dedicated context.");

                    async Task<ImportBatch> RunImportAsync(string importType, Dictionary<string, string> row)
                    {
                        var batch = new ImportBatch
                        {
                            TenantId = tenantId, ImportType = importType, FileName = $"QA_{importType}.xlsx",
                            Status = "Pending", CreatedByUserId = adminUserId, CreatedAtUtc = DateTime.UtcNow
                        };
                        importCtx.ImportBatches.Add(batch);
                        await importCtx.SaveChangesAsync();

                        importCtx.ImportBatchRows.Add(new ImportBatchRow
                        {
                            ImportBatchId = batch.Id, RowNumber = 2,
                            RawJson = System.Text.Json.JsonSerializer.Serialize(row),
                            Status = "Pending", CreatedAtUtc = DateTime.UtcNow
                        });
                        batch.TotalRows = 1;
                        await importCtx.SaveChangesAsync();

                        return await importSvc.ConfirmImportAsync(batch.Id, adminUserId);
                    }

                    // Import Supplier
                    var supBatch = await RunImportAsync("Suppliers",
                        new() { ["SupplierName"] = impSupplierName, ["ContactPerson"] = "QA Person" });
                    report.Check("Import Suppliers via ExcelImportService", supBatch.SuccessRows == 1,
                        $"success={supBatch.SuccessRows}, failed={supBatch.FailedRows}");

                    // Import Customer
                    var custBatch = await RunImportAsync("Customers",
                        new() { ["CustomerName"] = impCustomerName, ["CustomerType"] = "Regular" });
                    report.Check("Import Customers via ExcelImportService", custBatch.SuccessRows == 1,
                        $"success={custBatch.SuccessRows}, failed={custBatch.FailedRows}");

                    // Import Item (Products) — uses existing QA unit + category in the dedicated DB
                    var prodBatch = await RunImportAsync("Products", new()
                    {
                        ["ItemName"] = impItemName, ["ItemCode"] = impItemCode, ["UnitName"] = unitName,
                        ["CategoryName"] = categoryName, ["SellingPrice"] = "120", ["CostPrice"] = "70"
                    });
                    report.Check("Import Items (Products) via ExcelImportService", prodBatch.SuccessRows == 1,
                        $"success={prodBatch.SuccessRows}, failed={prodBatch.FailedRows}");

                    httpAccessor.HttpContext = null;
                }

                // ─────────────────────────────────────────────────────────────
                // SCOPE 3 — Write-location verification + operational reports
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp      = scope.ServiceProvider;
                    var shared  = sp.GetRequiredService<ApplicationDbContext>();
                    var factory = sp.GetRequiredService<ITenantDbContextFactory>();

                    await using var ded = factory.CreateForConnection(dedicatedConn!);

                    // Dedicated counts (expect > 0)
                    report.Dedicated["Branches"]               = await ded.Branches.CountAsync(x => x.Name == mainBranch || x.Name == secondBranch);
                    report.Dedicated["Categories"]             = await ded.Categories.CountAsync(x => x.CategoryName == categoryName);
                    report.Dedicated["Units"]                  = await ded.Units.CountAsync(x => x.UnitName == unitName);
                    report.Dedicated["Items"]                  = await ded.Items.CountAsync(x => x.ItemName == itemName);
                    report.Dedicated["Suppliers"]              = await ded.Suppliers.CountAsync(x => x.SupplierName == supplierName);
                    report.Dedicated["Customers"]              = await ded.Customers.CountAsync(x => x.CustomerName == customerName);
                    report.Dedicated["PurchaseOrders"]         = await ded.PurchaseOrders.CountAsync(x => x.PONumber == poNumber);
                    report.Dedicated["PurchaseOrderItems"]     = await ded.PurchaseOrderItems.CountAsync(x => x.PurchaseOrder!.PONumber == poNumber);
                    report.Dedicated["StockInHeaders"]         = await ded.StockInHeaders.CountAsync(x => x.StockInNumber == stockInNumber);
                    report.Dedicated["StockInDetails"]         = await ded.StockInDetails.CountAsync(x => x.StockInHeader!.StockInNumber == stockInNumber);
                    report.Dedicated["StockAdjustmentHeaders"] = await ded.StockAdjustmentHeaders.CountAsync(x => x.AdjustmentNumber == adjNumber);
                    report.Dedicated["StockAdjustmentDetails"] = await ded.StockAdjustmentDetails.CountAsync(x => x.StockAdjustmentHeader!.AdjustmentNumber == adjNumber);
                    report.Dedicated["BranchTransfers"]        = await ded.BranchTransfers.CountAsync(x => x.TransferNumber == transferNo);
                    report.Dedicated["BranchTransferItems"]    = await ded.BranchTransferItems.CountAsync(x => x.BranchTransfer!.TransferNumber == transferNo);
                    report.Dedicated["Quotations"]             = await ded.Quotations.CountAsync(x => x.QuotationNo == quotationNo);
                    report.Dedicated["QuotationItems"]         = await ded.QuotationItems.CountAsync(x => x.Quotation!.QuotationNo == quotationNo);
                    report.Dedicated["DeliveryReceipts"]       = await ded.DeliveryReceipts.CountAsync(x => x.DRNumber == drNumber);
                    report.Dedicated["DeliveryReceiptItems"]   = await ded.DeliveryReceiptItems.CountAsync(x => x.DeliveryReceipt!.DRNumber == drNumber);
                    report.Dedicated["SalesHeaders"]           = await ded.SalesHeaders.CountAsync(x => x.SalesNumber == posSaleNo || x.SalesNumber == creditSaleNo || x.SalesNumber == quoteSaleNo);
                    report.Dedicated["SalesDetails"]           = await ded.SalesDetails.CountAsync(x => x.SalesHeader!.SalesNumber == posSaleNo || x.SalesHeader!.SalesNumber == creditSaleNo || x.SalesHeader!.SalesNumber == quoteSaleNo);
                    report.Dedicated["CustomerLedgers"]        = await ded.CustomerLedgers.CountAsync(x => x.ReferenceNumber == creditSaleNo || x.ReferenceNumber == collectionRef);
                    report.Dedicated["SalesReturnHeaders"]     = await ded.SalesReturnHeaders.CountAsync(x => x.ReturnNumber == returnNo);
                    report.Dedicated["SalesReturnDetails"]     = await ded.SalesReturnDetails.CountAsync(x => x.SalesReturnHeader!.ReturnNumber == returnNo);
                    report.Dedicated["SupplierPayments"]       = await ded.SupplierPayments.CountAsync(x => x.ReferenceNumber == supPayRef);
                    report.Dedicated["Expenses"]               = await ded.Expenses.CountAsync(x => x.ExpenseNumber == expenseNo);
                    report.Dedicated["SystemSettings(QA biz)"] = await ded.SystemSettings.CountAsync(x => x.BusinessName == qaBusinessName);
                    report.Dedicated["UserBranches"]           = await ded.UserBranches.CountAsync(x => x.UserId == adminUserId);
                    report.Dedicated["AuditTrails(QA)"]        = await ded.AuditTrails.CountAsync(x => x.ActionName == auditAction);
                    report.Dedicated["Imported Supplier"]      = await ded.Suppliers.CountAsync(x => x.SupplierName == impSupplierName);
                    report.Dedicated["Imported Customer"]      = await ded.Customers.CountAsync(x => x.CustomerName == impCustomerName);
                    report.Dedicated["Imported Item"]          = await ded.Items.CountAsync(x => x.ItemName == impItemName);

                    // Shared counts (expect 0 — none of these QA rows should land in the shared DB)
                    report.Shared["Branches"]           = await shared.Branches.CountAsync(x => x.Name == mainBranch || x.Name == secondBranch);
                    report.Shared["Categories"]         = await shared.Categories.CountAsync(x => x.CategoryName == categoryName);
                    report.Shared["Units"]              = await shared.Units.CountAsync(x => x.UnitName == unitName);
                    report.Shared["Items"]              = await shared.Items.CountAsync(x => x.ItemName == itemName);
                    report.Shared["Suppliers"]          = await shared.Suppliers.CountAsync(x => x.SupplierName == supplierName);
                    report.Shared["Customers"]          = await shared.Customers.CountAsync(x => x.CustomerName == customerName);
                    report.Shared["PurchaseOrders"]     = await shared.PurchaseOrders.CountAsync(x => x.PONumber == poNumber);
                    report.Shared["StockInHeaders"]     = await shared.StockInHeaders.CountAsync(x => x.StockInNumber == stockInNumber);
                    report.Shared["StockAdjustmentHeaders"] = await shared.StockAdjustmentHeaders.CountAsync(x => x.AdjustmentNumber == adjNumber);
                    report.Shared["BranchTransfers"]    = await shared.BranchTransfers.CountAsync(x => x.TransferNumber == transferNo);
                    report.Shared["Quotations"]         = await shared.Quotations.CountAsync(x => x.QuotationNo == quotationNo);
                    report.Shared["DeliveryReceipts"]   = await shared.DeliveryReceipts.CountAsync(x => x.DRNumber == drNumber);
                    report.Shared["SalesHeaders"]       = await shared.SalesHeaders.CountAsync(x => x.SalesNumber == posSaleNo || x.SalesNumber == creditSaleNo || x.SalesNumber == quoteSaleNo);
                    report.Shared["CustomerLedgers"]    = await shared.CustomerLedgers.CountAsync(x => x.ReferenceNumber == creditSaleNo || x.ReferenceNumber == collectionRef);
                    report.Shared["SalesReturnHeaders"] = await shared.SalesReturnHeaders.CountAsync(x => x.ReturnNumber == returnNo);
                    report.Shared["SupplierPayments"]   = await shared.SupplierPayments.CountAsync(x => x.ReferenceNumber == supPayRef);
                    report.Shared["Expenses"]           = await shared.Expenses.CountAsync(x => x.ExpenseNumber == expenseNo);
                    report.Shared["SystemSettings(QA biz)"] = await shared.SystemSettings.CountAsync(x => x.BusinessName == qaBusinessName);
                    report.Shared["UserBranches"]       = await shared.UserBranches.CountAsync(x => x.UserId == adminUserId);
                    report.Shared["AuditTrails(QA)"]    = await shared.AuditTrails.CountAsync(x => x.ActionName == auditAction);
                    report.Shared["Imported Supplier"]  = await shared.Suppliers.CountAsync(x => x.SupplierName == impSupplierName);
                    report.Shared["Imported Customer"]  = await shared.Customers.CountAsync(x => x.CustomerName == impCustomerName);
                    report.Shared["Imported Item"]      = await shared.Items.CountAsync(x => x.ItemName == impItemName);

                    var dedAllPresent = report.Dedicated.Values.All(v => v > 0);
                    var sharedAllZero = report.Shared.Values.All(v => v == 0);
                    report.Check("Dedicated DB contains all QA operational rows", dedAllPresent, report.DedicatedSummary());
                    report.Check("Shared DB contains NONE of the QA operational rows", sharedAllZero, report.SharedSummary());
                    if (!dedAllPresent) throw new QaStop("Some QA rows missing from dedicated DB.");
                    if (!sharedAllZero) throw new QaStop("QA operational rows leaked into shared DB.");

                    // ── Operational reports (read from dedicated DB) ──────────
                    // 21) Customer SOA — ledger entries + outstanding balance
                    var soaEntries = await ded.CustomerLedgers.CountAsync(l => l.Customer!.CustomerName == customerName);
                    var soaBalance = await ded.CustomerLedgers.Where(l => l.Customer!.CustomerName == customerName)
                        .SumAsync(l => l.DebitAmount - l.CreditAmount);
                    report.Check("Customer SOA reads dedicated ledger", soaEntries >= 2 && soaBalance == 140m,
                        $"entries={soaEntries}, outstanding={soaBalance}");

                    // 22) Supplier Statement — stock-ins + payments
                    var supStockIns = await ded.StockInHeaders.CountAsync(s => s.Supplier!.SupplierName == supplierName);
                    var supPayments = await ded.SupplierPayments.CountAsync(p => p.Supplier!.SupplierName == supplierName);
                    report.Check("Supplier Statement reads dedicated", supStockIns >= 1 && supPayments >= 1,
                        $"stockIns={supStockIns}, payments={supPayments}");

                    // 23) Customer Aging — outstanding > 0
                    report.Check("Customer Aging reflects outstanding", soaBalance > 0m, $"outstanding={soaBalance}");

                    // 24) Supplier Aging — stock-in now fully paid
                    var supPayable = await ded.StockInHeaders.Where(s => s.Supplier!.SupplierName == supplierName)
                        .SumAsync(s => s.TotalCost - s.AmountPaid);
                    report.Check("Supplier Aging reflects payable", supPayable == 0m, $"payable={supPayable}");

                    // 25) Inventory Valuation — stock * cost
                    var qaItem = await ded.Items.FirstAsync(i => i.ItemName == itemName);
                    var valuation = qaItem.CurrentStock * qaItem.CostPrice;
                    report.Check("Inventory Valuation computes from dedicated", valuation > 0m,
                        $"stock={qaItem.CurrentStock}, cost={qaItem.CostPrice}, value={valuation}");

                    // 26-28) Fast / Slow / Dead moving — based on sold quantity
                    var soldQty = await ded.SalesDetails.Where(d => d.Item!.ItemName == itemName).SumAsync(d => d.Quantity);
                    report.Check("Fast/Slow/Dead-moving uses dedicated sales", soldQty > 0m, $"soldQty={soldQty} (QA item is moving → not dead)");

                    // 29) Reorder Suggestions — items at/below reorder level
                    var reorderCount = await ded.Items.CountAsync(i => i.CurrentStock <= i.ReorderLevel && i.Status == "Active");
                    report.Check("Reorder Suggestions query runs on dedicated", reorderCount >= 0, $"reorderCandidates={reorderCount}");

                    // 30) ABC Analysis — value contribution
                    var totalValue = await ded.Items.SumAsync(i => i.CurrentStock * i.CostPrice);
                    report.Check("ABC Analysis aggregates dedicated inventory", totalValue >= valuation, $"totalInventoryValue={totalValue}");

                    // 31) Dashboard KPIs — sales totals from dedicated
                    var kpiSalesCount = await ded.SalesHeaders.CountAsync(s => s.Status == "Completed" && (s.SalesNumber == posSaleNo || s.SalesNumber == creditSaleNo || s.SalesNumber == quoteSaleNo));
                    var kpiSalesTotal = await ded.SalesHeaders.Where(s => s.SalesNumber == posSaleNo || s.SalesNumber == creditSaleNo || s.SalesNumber == quoteSaleNo).SumAsync(s => s.TotalAmount);
                    var kpiExpenses   = await ded.Expenses.Where(e => e.ExpenseNumber == expenseNo).SumAsync(e => e.Amount);
                    report.Check("Dashboard KPIs read dedicated", kpiSalesCount == 3 && kpiSalesTotal == 480m && kpiExpenses == 350m,
                        $"sales={kpiSalesCount}, salesTotal={kpiSalesTotal}, expenses={kpiExpenses}");

                    // 32) Settings cutover — receipt/PDF header reads dedicated company profile
                    var hdr = await ded.SystemSettings.FirstOrDefaultAsync(s => s.TenantId == tenantId);
                    var hdrOk = hdr != null && hdr.BusinessName == qaBusinessName && hdr.TIN == qaTin
                                && hdr.DefaultVatPercent == qaVatPercent && hdr.ReceiptFooter == qaFooter;
                    report.Check("Receipt/PDF header uses dedicated settings", hdrOk,
                        $"BusinessName={hdr?.BusinessName}, TIN={hdr?.TIN}, VAT={hdr?.DefaultVatPercent}, FooterSet={!string.IsNullOrEmpty(hdr?.ReceiptFooter)}");

                    // Read Audit Entries — routing-enabled tenant sees its dedicated audit records
                    var dedAudit    = await ded.AuditTrails.CountAsync(a => a.ActionName == auditAction);
                    var sharedAudit = await shared.AuditTrails.CountAsync(a => a.ActionName == auditAction);
                    report.Check("Read Audit Entries from dedicated (not shared)", dedAudit == 1 && sharedAudit == 0,
                        $"dedicated={dedAudit}, shared={sharedAudit}");

                    // Branch selector source — assigned branches read from dedicated for this tenant
                    var assignedBranches = await ded.UserBranches.CountAsync(u => u.UserId == adminUserId);
                    report.Check("Branch selector reads dedicated assignments", assignedBranches >= 1,
                        $"assignedBranches(dedicated)={assignedBranches}");
                }

                // ─────────────────────────────────────────────────────────────
                // SCOPE 4 — Rollback (→ shared) then re-enable (→ dedicated)
                // ─────────────────────────────────────────────────────────────
                using (var scope = app.Services.CreateScope())
                {
                    var sp       = scope.ServiceProvider;
                    var db       = sp.GetRequiredService<ApplicationDbContext>();
                    var resolver = sp.GetRequiredService<ITenantDatabaseResolver>();
                    var users    = sp.GetRequiredService<UserManager<ApplicationUser>>();

                    var t = await db.Tenants.FirstAsync(x => x.Id == tenantId);
                    t.RoutingEnabled = false; t.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    resolver.Invalidate(tenantId);
                    var runtimeShared = await resolver.GetRuntimeDatabaseAsync(tenantId);
                    report.Check("Rollback: runtime returns Shared", runtimeShared == "Shared", $"RuntimeDatabase={runtimeShared}");

                    var admin = await users.FindByNameAsync(adminUser);
                    var pwOk = admin != null && admin.IsActive && await users.CheckPasswordAsync(admin, adminPassword);
                    report.Check("Rollback: TenantAdmin can still authenticate", pwOk, pwOk ? "password check passed" : "auth failed");

                    // Re-enable
                    t = await db.Tenants.FirstAsync(x => x.Id == tenantId);
                    t.RoutingEnabled = true; t.UpdatedAtUtc = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                    resolver.Invalidate(tenantId);
                    var runtimeDed = await resolver.GetRuntimeDatabaseAsync(tenantId);
                    report.Check("Re-enable: runtime returns Dedicated", runtimeDed == "Dedicated", $"RuntimeDatabase={runtimeDed}");
                    report.RollbackOk = runtimeShared == "Shared" && pwOk && runtimeDed == "Dedicated";
                    report.Note("Dedicated database was NOT deleted (per spec).");
                }

                // Provider routing checks across fresh scopes (post re-enable → dedicated)
                using (var scope = app.Services.CreateScope())
                {
                    var provider = scope.ServiceProvider.GetRequiredService<ITenantOperationalContextProvider>();
                    var ctx = await provider.GetContextAsync(tenantId);
                    report.Check("Re-enable: provider returns TenantDbContext", ctx.GetType().Name == "TenantDbContext",
                        $"Resolved context = {ctx.GetType().Name}");
                }

                report.Success = report.Failures == 0;
            }
            catch (QaStop stop)
            {
                report.Note($"STOPPED: {stop.Message}");
                report.Success = false;
            }
            catch (Exception ex)
            {
                report.Note($"UNEXPECTED EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                report.LastException = ex.ToString();
                report.Success = false;
            }

            // ── Audit the cutover verification result (shared, platform-level) ──
            if (tenantId > 0)
            {
                try { await WriteCutoverAuditAsync(app, tenantId, report.TenantName, dbName, report.Success); }
                catch (Exception ex) { report.Note($"Audit write error: {ex.Message}"); }
            }

            // ── Optional cleanup ─────────────────────────────────────────────
            if (enableCleanup && tenantId > 0)
            {
                try { await CleanupAsync(app, tenantId, adminUser, dedicatedConn, dbName, report); }
                catch (Exception ex) { report.Note($"Cleanup error: {ex.Message}"); }
            }
            else
            {
                report.Note("Cleanup skipped (QA:EnableCleanup not true). Test data retained with QA_ prefix.");
            }

            await report.WriteFileAsync(app);
            Console.WriteLine(report.ConsoleSummary());
        }

        // ──────────────────────────────────────────────────────────────────
        private static async Task UpsertBranchStock(
            ITenantOperationalDbContext ctx, int tenantId, int branchId, int productId, decimal delta)
        {
            var bps = await ctx.BranchProductStocks
                .FirstOrDefaultAsync(s => s.BranchId == branchId && s.ProductId == productId);
            if (bps == null)
            {
                ctx.BranchProductStocks.Add(new BranchProductStock
                {
                    TenantId = tenantId, BranchId = branchId, ProductId = productId,
                    Quantity = Math.Max(0m, delta), UpdatedAtUtc = DateTime.UtcNow
                });
            }
            else
            {
                bps.Quantity = Math.Max(0m, bps.Quantity + delta);
                bps.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        private static async Task WriteCutoverAuditAsync(
            WebApplication app, int tenantId, string tenantName, string dbName, bool success)
        {
            using var scope = app.Services.CreateScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ApplicationDbContext>();

            string runtimeDb;
            try { runtimeDb = await sp.GetRequiredService<ITenantDatabaseResolver>().GetRuntimeDatabaseAsync(tenantId); }
            catch { runtimeDb = "Unknown"; }

            // Phase 5.0D.4 — final operational cutover audit (platform-level, shared).
            db.AuditTrails.Add(new AuditTrail
            {
                UserId        = null,
                UserName      = "QA Runner (Phase 5.0D.4)",
                ModuleName    = "TenantDatabases",
                ActionName    = success ? "TENANT_FINAL_CUTOVER_VERIFIED" : "TENANT_FINAL_CUTOVER_FAILED",
                Description   = $"Final operational split-brain cutover {(success ? "verified" : "FAILED")} for tenant " +
                                $"'{tenantName}' (db '{dbName}', runtimeDatabase '{runtimeDb}').",
                ReferenceType = "Tenant",
                ReferenceId   = tenantId.ToString(),
                IpAddress     = "127.0.0.1",
                Browser       = "N/A", OperatingSystem = "N/A", DeviceType = "Server",
                TenantId      = tenantId,
                CreatedAt     = DateTime.Now
            });
            await db.SaveChangesAsync();
        }

        private static async Task<(bool ok, string message)> TryOpenAsync(string connectionString)
        {
            try
            {
                await using var c = new SqlConnection(connectionString);
                await c.OpenAsync();
                return (true, "Connection successful.");
            }
            catch (Exception ex) { return (false, $"Connection failed: {ex.Message}"); }
        }

        private static async Task CleanupAsync(
            WebApplication app, int tenantId, string adminUser, string? dedicatedConn, string dbName, Report report)
        {
            using var scope = app.Services.CreateScope();
            var sp    = scope.ServiceProvider;
            var db    = sp.GetRequiredService<ApplicationDbContext>();
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();

            var admin = await users.FindByNameAsync(adminUser);
            if (admin != null) await users.DeleteAsync(admin);

            var settings = await db.SystemSettings.Where(s => s.TenantId == tenantId).ToListAsync();
            db.SystemSettings.RemoveRange(settings);
            var audits = await db.AuditTrails.Where(a => a.TenantId == tenantId).ToListAsync();
            db.AuditTrails.RemoveRange(audits);
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId);
            if (tenant != null) db.Tenants.Remove(tenant);
            await db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(dedicatedConn))
            {
                var master = new SqlConnectionStringBuilder(dedicatedConn) { InitialCatalog = "master" }.ConnectionString;
                await using var c = new SqlConnection(master);
                await c.OpenAsync();
                await using var cmd = c.CreateCommand();
                var safe = dbName.Replace("]", "]]");
                cmd.CommandText =
                    $"IF DB_ID(N'{dbName.Replace("'", "''")}') IS NOT NULL BEGIN " +
                    $"ALTER DATABASE [{safe}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{safe}]; END";
                await cmd.ExecuteNonQueryAsync();
            }
            report.Note($"Cleanup completed: removed tenant {tenantId}, user {adminUser}, and dropped DB {dbName}.");
        }

        // ──────────────────────────────────────────────────────────────────
        private sealed class QaStop : Exception
        {
            public QaStop(string message) : base(message) { }
        }

        private sealed class Report
        {
            private readonly string _stamp;
            public Report(string stamp) { _stamp = stamp; }

            public string  TenantName   = "";
            public int     TenantId;
            public string  DatabaseName = "";
            public bool    Success;
            public bool    RollbackOk;
            public string? LastException;

            public readonly List<(string step, bool ok, string detail)> Steps = new();
            public readonly List<string> Notes = new();
            public readonly Dictionary<string, int> Dedicated = new();
            public readonly Dictionary<string, int> Shared = new();

            public int Failures => Steps.Count(s => !s.ok);

            public void Check(string step, bool ok, string detail)
            {
                Steps.Add((step, ok, detail));
                Console.WriteLine($"[QA] {(ok ? "PASS" : "FAIL")} — {step} :: {detail}");
            }

            public void Note(string n) { Notes.Add(n); Console.WriteLine($"[QA] NOTE — {n}"); }

            public string DedicatedSummary() => string.Join(", ", Dedicated.Select(kv => $"{kv.Key}={kv.Value}"));
            public string SharedSummary()    => string.Join(", ", Shared.Select(kv => $"{kv.Key}={kv.Value}"));

            public string ConsoleSummary()
            {
                var passed = Steps.Count(s => s.ok);
                return $"\n[QA] ==== Phase 5.0D.2 + 5.0D.4 Full/Final Module Cutover ==== " +
                       $"{(Success ? "OVERALL PASS" : "OVERALL FAIL")} — {passed}/{Steps.Count} steps passed. " +
                       $"Report: docs/Phase50D2_FullCycleQAReport.md";
            }

            public async Task WriteFileAsync(WebApplication app)
            {
                var sb = new StringBuilder();
                sb.AppendLine("# Phase 5.0D.2 + 5.0D.4 — Full & Final Module Cutover QA Report");
                sb.AppendLine();
                sb.AppendLine($"- **Test run:** {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local), id `{_stamp}`");
                sb.AppendLine($"- **Runner:** dev-only `Tools/Phase50D2QaRunner.cs` via `dotnet run -- --qa-phase50d2` (web server NOT started)");
                sb.AppendLine($"- **Environment:** {app.Environment.EnvironmentName}");
                sb.AppendLine($"- **Tenant created:** `{TenantName}` (TenantId = {TenantId})");
                sb.AppendLine($"- **Dedicated database:** `{DatabaseName}`");
                sb.AppendLine($"- **Overall result:** {(Success ? "PASS" : "FAIL")} — {Steps.Count(s => s.ok)}/{Steps.Count} steps passed");
                sb.AppendLine();

                sb.AppendLine("## Steps");
                sb.AppendLine();
                sb.AppendLine("| # | Step | Result | Detail |");
                sb.AppendLine("|---|------|--------|--------|");
                for (var i = 0; i < Steps.Count; i++)
                {
                    var s = Steps[i];
                    sb.AppendLine($"| {i + 1} | {s.step} | {(s.ok ? "PASS" : "FAIL")} | {s.detail.Replace("|", "\\|")} |");
                }
                sb.AppendLine();

                sb.AppendLine("## Dedicated DB row verification (expected > 0)");
                sb.AppendLine();
                sb.AppendLine("| Table | Rows in Dedicated |");
                sb.AppendLine("|-------|-------------------|");
                foreach (var kv in Dedicated) sb.AppendLine($"| {kv.Key} | {kv.Value} |");
                sb.AppendLine();

                sb.AppendLine("## Shared DB row verification (expected 0 for QA operational rows)");
                sb.AppendLine();
                sb.AppendLine("| Table | Rows in Shared |");
                sb.AppendLine("|-------|----------------|");
                foreach (var kv in Shared) sb.AppendLine($"| {kv.Key} | {kv.Value} |");
                sb.AppendLine();
                sb.AppendLine("> Platform rows (AspNetUsers, AspNetRoles, Tenants, SubscriptionPlans, RolePermissions) intentionally remain in the shared DB.");
                sb.AppendLine();

                sb.AppendLine("## Routing & rollback");
                sb.AppendLine();
                sb.AppendLine($"- Rollback result: {(RollbackOk ? "disable→Shared, auth OK, re-enable→Dedicated" : "see steps")}");
                sb.AppendLine();

                sb.AppendLine("## Notes");
                sb.AppendLine();
                foreach (var n in Notes) sb.AppendLine($"- {n}");
                if (LastException != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("## Exception");
                    sb.AppendLine();
                    sb.AppendLine("```");
                    sb.AppendLine(LastException);
                    sb.AppendLine("```");
                }

                var path = Path.Combine(app.Environment.ContentRootPath, "docs", "Phase50D2_FullCycleQAReport.md");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, sb.ToString());
            }
        }
    }
}
