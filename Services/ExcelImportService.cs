using ClosedXML.Excel;
using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HardwareManagementSystem.Services
{
    public class ExcelImportService
    {
        // Phase 5.0D.4 ? all import reads (catalog validation) and writes (Items/Customers/
        // Suppliers, ImportBatchRows, batch counts) are tenant operational data. They route to
        // the tenant's dedicated database when routing is active. The provider is scoped and
        // caches one context per request, so this service and ImportController share the same
        // instance (a batch created by the controller is visible to the service immediately).
        private readonly ITenantOperationalContextProvider _operationalContextProvider;
        private readonly TenantGuard _tenantGuard;

        // ============================================
        // SYSTEM FIELDS PER IMPORT TYPE
        // ============================================

        public static readonly Dictionary<string, List<string>> RequiredFields = new()
        {
            ["Products"] = ["ItemName", "UnitName", "SellingPrice"],
            ["OpeningStock"] = ["ItemName", "Quantity"],
            ["Customers"] = ["CustomerName"],
            ["Suppliers"] = ["SupplierName"]
        };

        public static readonly Dictionary<string, List<string>> OptionalFields = new()
        {
            ["Products"] = ["ItemCode", "Barcode", "CategoryName", "SupplierName", "CostPrice", "OpeningQtyToAdd", "ReorderLevel", "Description", "BaseUnit", "PurchaseUnit", "ConversionQuantity"],
            ["OpeningStock"] = ["ItemCode"],
            ["Customers"] = ["ContactNumber", "Email", "Address", "CustomerType"],
            ["Suppliers"] = ["ContactPerson", "ContactNumber", "Email", "Address", "Remarks"]
        };

        /// <summary>Human-readable label shown in UI for each system field name.</summary>
        public static readonly Dictionary<string, string> FieldDisplayNames = new()
        {
            ["ItemName"]        = "Item Name",
            ["UnitName"]        = "Unit",
            ["SellingPrice"]    = "Selling Price",
            ["ItemCode"]        = "Item Code",
            ["Barcode"]         = "Barcode",
            ["CategoryName"]    = "Category",
            ["SupplierName"]    = "Supplier",
            ["CostPrice"]       = "Cost Price",
            ["OpeningQtyToAdd"] = "Opening Qty / Qty to Add",
            ["ReorderLevel"]    = "Reorder Level",
            ["Description"]     = "Description",
            ["Quantity"]        = "Quantity to Add",
            ["CustomerName"]    = "Customer Name",
            ["ContactNumber"]   = "Contact Number",
            ["Email"]           = "Email",
            ["Address"]         = "Address",
            ["CustomerType"]    = "Customer Type",
            ["ContactPerson"]   = "Contact Person",
            ["Remarks"]         = "Remarks"
        };

        public static string GetDisplayName(string field)
            => FieldDisplayNames.TryGetValue(field, out var label) ? label : field;

        public static List<string> GetAllFields(string importType)
        {
            var all = new List<string>();
            if (RequiredFields.TryGetValue(importType, out var req)) all.AddRange(req);
            if (OptionalFields.TryGetValue(importType, out var opt)) all.AddRange(opt);
            return all;
        }

        public ExcelImportService(
            ITenantOperationalContextProvider operationalContextProvider,
            TenantGuard tenantGuard)
        {
            _operationalContextProvider = operationalContextProvider;
            _tenantGuard = tenantGuard;
        }

        // ============================================
        // READ HEADERS
        // ============================================

        public List<string> ReadHeaders(Stream excelStream)
        {
            using var wb = new XLWorkbook(excelStream);
            var ws = wb.Worksheets.First();
            var headers = new List<string>();

            var headerRow = ws.Row(1);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            for (int col = 1; col <= lastCol; col++)
            {
                var val = headerRow.Cell(col).GetString().Trim();
                if (!string.IsNullOrWhiteSpace(val))
                    headers.Add(val);
            }
            return headers;
        }

        // ============================================
        // BUILD PREVIEW (validate + map)
        // ============================================

        public async Task<ImportPreviewViewModel> BuildPreviewAsync(
    int batchId,
    string importType,
    Stream excelStream,
    Dictionary<string, string> mapping)
        {
            using var wb = new XLWorkbook(excelStream);
            var ws = wb.Worksheets.First();

            var headerRow = ws.Row(1);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;

            var colIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int c = 1; c <= lastCol; c++)
            {
                var h = headerRow.Cell(c).GetString().Trim();

                if (!string.IsNullOrWhiteSpace(h) && !colIndex.ContainsKey(h))
                    colIndex[h] = c;
            }

            int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;

            var preview = new ImportPreviewViewModel
            {
                BatchId = batchId,
                ImportType = importType,
                SystemFields = GetAllFields(importType)
            };

            var effectiveTenantId =
                await _tenantGuard.GetEffectiveTenantIdAsync();

            var db = await _operationalContextProvider.GetContextAsync();

            var unitNames = await db.Units
                .AsNoTracking()
                .Where(u =>
                    effectiveTenantId == null ||
                    u.TenantId == effectiveTenantId ||
                    u.TenantId == null)
                .Where(u => u.IsActive)
                .Select(u => u.UnitName.ToLower())
                .ToListAsync();

            var categoryNames = await db.Categories
                .AsNoTracking()
                .Where(c =>
                    effectiveTenantId == null ||
                    c.TenantId == effectiveTenantId ||
                    c.TenantId == null)
                .Where(c => c.IsActive)
                .Select(c => c.CategoryName.ToLower())
                .ToListAsync();

            var supplierNames = await db.Suppliers
                .AsNoTracking()
                .Where(s =>
                    effectiveTenantId == null ||
                    s.TenantId == effectiveTenantId ||
                    s.TenantId == null)
                .Where(s => s.IsActive)
                .Select(s => s.SupplierName.ToLower())
                .ToListAsync();

            var existingItemCodes = await db.Items
                .AsNoTracking()
                .Where(i =>
                    effectiveTenantId == null ||
                    i.TenantId == effectiveTenantId ||
                    i.TenantId == null)
                .Select(i => i.ItemCode.ToLower())
                .ToListAsync();

            var existingItemNames = await db.Items
                .AsNoTracking()
                .Where(i =>
                    effectiveTenantId == null ||
                    i.TenantId == effectiveTenantId ||
                    i.TenantId == null)
                .Select(i => i.ItemName.ToLower())
                .ToListAsync();

            var existingCustomerNames = await db.Customers
                .AsNoTracking()
                .Where(c =>
                    effectiveTenantId == null ||
                    c.TenantId == effectiveTenantId ||
                    c.TenantId == null)
                .Select(c => c.CustomerName.ToLower())
                .ToListAsync();

            var existingSupplierNames = await db.Suppliers
                .AsNoTracking()
                .Where(s =>
                    effectiveTenantId == null ||
                    s.TenantId == effectiveTenantId ||
                    s.TenantId == null)
                .Select(s => s.SupplierName.ToLower())
                .ToListAsync();

            for (int row = 2; row <= lastRow; row++)
            {
                var excelRow = ws.Row(row);

                bool isEmpty = true;

                for (int c = 1; c <= lastCol; c++)
                {
                    if (!string.IsNullOrWhiteSpace(excelRow.Cell(c).GetString()))
                    {
                        isEmpty = false;
                        break;
                    }
                }

                if (isEmpty) continue;

                var previewRow = new ImportPreviewRow
                {
                    RowNumber = row
                };

                foreach (var (sysField, excelHeader) in mapping)
                {
                    if (string.IsNullOrWhiteSpace(excelHeader))
                        continue;

                    if (!colIndex.TryGetValue(excelHeader, out int colNum))
                        continue;

                    var cellVal =
                        excelRow.Cell(colNum).GetString().Trim();

                    previewRow.Fields[sysField] = cellVal;
                }

                ValidateRow(
                    importType,
                    previewRow,
                    unitNames,
                    categoryNames,
                    supplierNames,
                    existingItemCodes,
                    existingItemNames,
                    existingCustomerNames,
                    existingSupplierNames);

                preview.Rows.Add(previewRow);
            }

            return preview;
        }

        private static void ValidateRow(
            string importType,
            ImportPreviewRow row,
            List<string> unitNames,
            List<string> categoryNames,
            List<string> supplierNames,
            List<string> existingItemCodes,
            List<string> existingItemNames,
            List<string> existingCustomerNames,
            List<string> existingSupplierNames)
        {
            string Get(string f) => row.Fields.TryGetValue(f, out var v) ? v : "";

            switch (importType)
            {
                case "Products":
                    if (string.IsNullOrWhiteSpace(Get("ItemName")))
                        row.Errors.Add("Item Name is required.");

                    var unitName = Get("UnitName");
                    if (string.IsNullOrWhiteSpace(unitName))
                        row.Errors.Add("Unit is required.");
                    else if (!unitNames.Contains(unitName.ToLower()))
                        row.Errors.Add($"Unit '{unitName}' not found in system.");

                    var spRaw = Get("SellingPrice");
                    if (string.IsNullOrWhiteSpace(spRaw))
                        row.Errors.Add("Selling Price is required.");
                    else if (!decimal.TryParse(spRaw, out _))
                        row.Errors.Add("Selling Price must be a number.");

                    var catName = Get("CategoryName");
                    if (!string.IsNullOrWhiteSpace(catName) && !categoryNames.Contains(catName.ToLower()))
                        row.Errors.Add($"Category '{catName}' not found in system.");

                    var supName = Get("SupplierName");
                    if (!string.IsNullOrWhiteSpace(supName) && !supplierNames.Contains(supName.ToLower()))
                        row.Errors.Add($"Supplier '{supName}' not found in system.");

                    var code = Get("ItemCode");
                    if (!string.IsNullOrWhiteSpace(code) && existingItemCodes.Contains(code.ToLower()))
                        row.Errors.Add($"Item Code '{code}' already exists.");

                    var iName = Get("ItemName");
                    if (!string.IsNullOrWhiteSpace(iName) &&
                        string.IsNullOrWhiteSpace(code) &&
                        string.IsNullOrWhiteSpace(Get("Barcode")) &&
                        existingItemNames.Contains(iName.ToLower()))
                        row.Errors.Add($"Product Name '{iName}' already exists (duplicate).");

                    var oqRaw = Get("OpeningQtyToAdd");
                    if (!string.IsNullOrWhiteSpace(oqRaw) && !decimal.TryParse(oqRaw, out _))
                        row.Errors.Add("Opening Qty / Qty to Add must be a number.");

                    break;

                case "OpeningStock":
                    if (string.IsNullOrWhiteSpace(Get("ItemName")))
                        row.Errors.Add("Item Name is required.");
                    var qtyRaw = Get("Quantity");
                    if (string.IsNullOrWhiteSpace(qtyRaw))
                        row.Errors.Add("Quantity is required.");
                    else if (!decimal.TryParse(qtyRaw, out _))
                        row.Errors.Add("Quantity must be a number.");
                    break;

                case "Customers":
                    if (string.IsNullOrWhiteSpace(Get("CustomerName")))
                        row.Errors.Add("Customer Name is required.");
                    else if (existingCustomerNames.Contains(Get("CustomerName").ToLower()))
                        row.Errors.Add($"Customer '{Get("CustomerName")}' already exists.");
                    break;

                case "Suppliers":
                    if (string.IsNullOrWhiteSpace(Get("SupplierName")))
                        row.Errors.Add("Supplier Name is required.");
                    else if (existingSupplierNames.Contains(Get("SupplierName").ToLower()))
                        row.Errors.Add($"Supplier '{Get("SupplierName")}' already exists.");
                    break;
            }
        }

        // ============================================
        // SAVE PREVIEW ROWS TO DB
        // ============================================

        public async Task SavePreviewRowsAsync(int batchId, List<ImportPreviewRow> rows)
        {
            var db = await _operationalContextProvider.GetContextAsync();

            var batch = await db.ImportBatches.FindAsync(batchId)
                ?? throw new InvalidOperationException("Import batch not found.");

            // ============================================
            // TENANT ACCESS GUARD
            // ============================================
            if (!await _tenantGuard.CanAccessTenantAsync(batch.TenantId))
            {
                throw new UnauthorizedAccessException(
                    "Cross-tenant import access denied.");
            }

            // ============================================
            // IDEMPOTENCY GUARD
            // ============================================
            // Safety: never overwrite rows for a batch that has already been confirmed
            if (batch.Status == "Completed" || batch.Status == "Processing")
                throw new InvalidOperationException(
                    "This import batch has already been processed and cannot be re-mapped.");

            // ============================================
            // REMOVE EXISTING PREVIEW ROWS
            // ============================================
            // Remove any prior rows for this batch (re-preview scenario)
            var existing = db.ImportBatchRows
                .Where(r => r.ImportBatchId == batchId);

            db.ImportBatchRows.RemoveRange(existing);

            // ============================================
            // SAVE NEW PREVIEW ROWS
            // ============================================
            var batchRows = rows.Select(r => new ImportBatchRow
            {
                ImportBatchId = batchId,
                RowNumber = r.RowNumber,
                RawJson = JsonSerializer.Serialize(r.Fields),
                Status = r.HasErrors ? "Skipped" : "Pending",
                ErrorMessage = r.HasErrors
                    ? string.Join("; ", r.Errors)
                    : null,
                CreatedAtUtc = DateTime.UtcNow
            }).ToList();

            await db.ImportBatchRows.AddRangeAsync(batchRows);

            // ============================================
            // UPDATE BATCH COUNTS
            // ============================================
            batch.TotalRows = rows.Count;
            batch.FailedRows = rows.Count(r => r.HasErrors);
            batch.SuccessRows = 0; // not yet imported

            await db.SaveChangesAsync();
        }

        // ============================================
        // CONFIRM IMPORT ? process valid rows
        // ============================================

        public async Task<ImportBatch> ConfirmImportAsync(int batchId, string userId)
        {
            var db = await _operationalContextProvider.GetContextAsync();

            var batch = await db.ImportBatches
                .Include(b => b.ImportBatchRows)
                .FirstOrDefaultAsync(b => b.Id == batchId)
                ?? throw new InvalidOperationException("Import batch not found.");

            // ============================================
            // TENANT ACCESS GUARD
            // ============================================
            if (!await _tenantGuard.CanAccessTenantAsync(batch.TenantId))
            {
                throw new UnauthorizedAccessException(
                    "Cross-tenant import confirmation denied.");
            }

            // ============================================
            // IDEMPOTENCY GUARD ? one batch, one import
            // ============================================
            if (batch.Status == "Completed")
                throw new InvalidOperationException(
                    $"Batch #{batchId} has already been completed on " +
                    $"{batch.CompletedAtUtc?.ToLocalTime():MMM dd, yyyy hh:mm tt}. " +
                    "It cannot be confirmed again.");

            if (batch.Status == "Processing")
                throw new InvalidOperationException(
                    $"Batch #{batchId} is currently being processed. Please wait.");

            // ============================================
            // SET PROCESSING STATUS
            // ============================================
            batch.Status = "Processing";

            await db.SaveChangesAsync();

            // ============================================
            // ONLY PROCESS VALID ROWS
            // ============================================
            var validRows = batch.ImportBatchRows
                .Where(r => r.Status == "Pending")
                .ToList();

            int success = 0;
            int failed = 0;

            // ============================================
            // PROCESS BY IMPORT TYPE
            // ============================================
            switch (batch.ImportType)
            {
                case "Products":
                    (success, failed) = await ImportProductsAsync(validRows);
                    break;

                case "OpeningStock":
                    (success, failed) = await ImportOpeningStockAsync(validRows);
                    break;

                case "Customers":
                    (success, failed) = await ImportCustomersAsync(validRows);
                    break;

                case "Suppliers":
                    (success, failed) = await ImportSuppliersAsync(validRows);
                    break;
            }

            // ============================================
            // FINALIZE COUNTS
            // ============================================
            batch.SuccessRows = success;

            // FailedRows = skipped rows + runtime failures
            batch.FailedRows = batch.ImportBatchRows.Count(r =>
                r.Status == "Skipped" ||
                r.Status == "Failed");

            batch.Status = "Completed";
            batch.CompletedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync();

            return batch;
        }

        // ============================================
        // IMPORT PRODUCTS
        // ============================================

        private async Task<(int success, int failed)> ImportProductsAsync(List<ImportBatchRow> rows)
        {
            int success = 0, failed = 0;

            var effectiveTenantId =
                await _tenantGuard.GetEffectiveTenantIdAsync();

            var db = await _operationalContextProvider.GetContextAsync();

            var units = await db.Units
                .AsNoTracking()
                .Where(u =>
                    effectiveTenantId == null ||
                    u.TenantId == effectiveTenantId ||
                    u.TenantId == null)
                .ToListAsync();

            var categories = await db.Categories
                .AsNoTracking()
                .Where(c =>
                    effectiveTenantId == null ||
                    c.TenantId == effectiveTenantId ||
                    c.TenantId == null)
                .ToListAsync();

            var suppliers = await db.Suppliers
                .AsNoTracking()
                .Where(s =>
                    effectiveTenantId == null ||
                    s.TenantId == effectiveTenantId ||
                    s.TenantId == null)
                .ToListAsync();

            var existingCodes = await db.Items
                .AsNoTracking()
                .Where(i =>
                    effectiveTenantId == null ||
                    i.TenantId == effectiveTenantId ||
                    i.TenantId == null)
                .Select(i => i.ItemCode)
                .ToListAsync();

            int counter = existingCodes
                .Where(c => c.StartsWith("PRD-"))
                .Select(c =>
                {
                    int.TryParse(c.Replace("PRD-", ""), out int n);
                    return n;
                })
                .DefaultIfEmpty(0)
                .Max();

            foreach (var row in rows)
            {
                try
                {
                    var fields =
                        JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawJson)
                        ?? new();

                    string Get(string k) =>
                        fields.TryGetValue(k, out var v)
                            ? v.Trim()
                            : "";

                    var unitName = Get("UnitName");
                    var baseUnitName = Get("BaseUnit");
                    if (string.IsNullOrWhiteSpace(baseUnitName))
                        baseUnitName = unitName;

                    var unit = units.FirstOrDefault(u =>
                        u.UnitName.Equals(unitName, StringComparison.OrdinalIgnoreCase));

                    if (unit == null)
                    {
                        row.Status = "Failed";
                        row.ErrorMessage = $"Unit '{unitName}' not found.";
                        failed++;
                        continue;
                    }

                    var baseUnit = units.FirstOrDefault(u =>
                        u.UnitName.Equals(baseUnitName, StringComparison.OrdinalIgnoreCase))
                        ?? unit;

                    var catName = Get("CategoryName");

                    var cat = string.IsNullOrWhiteSpace(catName)
                        ? null
                        : categories.FirstOrDefault(c =>
                            c.CategoryName.Equals(catName,
                                StringComparison.OrdinalIgnoreCase));

                    var supName = Get("SupplierName");

                    var sup = string.IsNullOrWhiteSpace(supName)
                        ? null
                        : suppliers.FirstOrDefault(s =>
                            s.SupplierName.Equals(supName,
                                StringComparison.OrdinalIgnoreCase));

                    var itemCode = Get("ItemCode");

                    if (string.IsNullOrWhiteSpace(itemCode))
                    {
                        counter++;
                        itemCode = $"PRD-{counter:D4}";
                    }

                    decimal.TryParse(Get("SellingPrice"), out decimal sp);
                    decimal.TryParse(Get("CostPrice"), out decimal cp);
                    decimal.TryParse(Get("OpeningQtyToAdd"), out decimal oq);
                    decimal.TryParse(Get("ReorderLevel"), out decimal rl);

                    var item = new Item
                    {
                        TenantId = effectiveTenantId,
                        ItemCode = itemCode,
                        ItemName = Get("ItemName"),
                        CategoryId = cat?.Id ?? (categories.FirstOrDefault()?.Id ?? 1),
                        UnitId = unit.Id,
                        BaseUnitId = baseUnit.Id,
                        SupplierId = sup?.Id,
                        CostPrice = cp,
                        SellingPrice = sp,
                        ReorderLevel = rl,
                        CurrentStock = oq,
                        Description = Get("Description"),
                        Status = "Active",
                        CreatedAt = DateTime.Now
                    };

                    db.Items.Add(item);
                    await db.SaveChangesAsync();

                    db.ItemUnitConversions.Add(new ItemUnitConversion
                    {
                        TenantId = effectiveTenantId,
                        ItemId = item.Id,
                        UnitId = baseUnit.Id,
                        ConversionQuantity = 1,
                        IsDefaultPurchaseUnit = true,
                        IsActive = true,
                        CreatedAtUtc = DateTime.UtcNow,
                        UpdatedAtUtc = DateTime.UtcNow
                    });

                    var purchaseUnitName = Get("PurchaseUnit");
                    if (!string.IsNullOrWhiteSpace(purchaseUnitName))
                    {
                        var pu = units.FirstOrDefault(u =>
                            u.UnitName.Equals(purchaseUnitName, StringComparison.OrdinalIgnoreCase));
                        if (pu != null && pu.Id != baseUnit.Id)
                        {
                            decimal.TryParse(Get("ConversionQuantity"), out var convQty);
                            if (convQty <= 0) convQty = 1;
                            db.ItemUnitConversions.Add(new ItemUnitConversion
                            {
                                TenantId = effectiveTenantId,
                                ItemId = item.Id,
                                UnitId = pu.Id,
                                ConversionQuantity = convQty,
                                IsDefaultPurchaseUnit = true,
                                IsActive = true,
                                CreatedAtUtc = DateTime.UtcNow,
                                UpdatedAtUtc = DateTime.UtcNow
                            });
                        }
                    }

                    row.Status = "Imported";
                    success++;
                }
                catch (Exception ex)
                {
                    row.Status = "Failed";
                    row.ErrorMessage = ex.Message;
                    failed++;
                }
            }

            await db.SaveChangesAsync();

            return (success, failed);
        }

        // ============================================
        // IMPORT OPENING STOCK
        // ============================================

        private async Task<(int success, int failed)> ImportOpeningStockAsync(List<ImportBatchRow> rows)
        {
            int success = 0, failed = 0;

            var effectiveTenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var db = await _operationalContextProvider.GetContextAsync();

            var itemsQuery = db.Items.AsQueryable();

            if (effectiveTenantId.HasValue)
            {
                itemsQuery = itemsQuery.Where(i =>
                    i.TenantId == effectiveTenantId ||
                    i.TenantId == null);
            }

            var items = await itemsQuery.ToListAsync();

            foreach (var row in rows)
            {
                try
                {
                    var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawJson) ?? new();

                    string Get(string k) => fields.TryGetValue(k, out var v) ? v.Trim() : "";

                    var itemName = Get("ItemName");
                    var itemCode = Get("ItemCode");

                    Item? item = null;

                    if (!string.IsNullOrWhiteSpace(itemCode))
                        item = items.FirstOrDefault(i => i.ItemCode.Equals(itemCode, StringComparison.OrdinalIgnoreCase));

                    if (item == null)
                        item = items.FirstOrDefault(i => i.ItemName.Equals(itemName, StringComparison.OrdinalIgnoreCase));

                    if (item == null)
                    {
                        row.Status = "Failed";
                        row.ErrorMessage = $"Item '{itemName}' not found.";
                        failed++;
                        continue;
                    }

                    if (!await _tenantGuard.CanAccessTenantAsync(item.TenantId))
                    {
                        row.Status = "Failed";
                        row.ErrorMessage = "Cross-tenant stock modification denied.";
                        failed++;
                        continue;
                    }

                    decimal.TryParse(Get("Quantity"), out decimal qty);

                    item.CurrentStock += qty;

                    if (!item.TenantId.HasValue && effectiveTenantId.HasValue)
                        item.TenantId = effectiveTenantId;

                    row.Status = "Imported";
                    success++;
                }
                catch (Exception ex)
                {
                    row.Status = "Failed";
                    row.ErrorMessage = ex.Message;
                    failed++;
                }
            }

            await db.SaveChangesAsync();

            return (success, failed);
        }

        // ============================================
        // IMPORT CUSTOMERS
        // ============================================

        private async Task<(int success, int failed)> ImportCustomersAsync(List<ImportBatchRow> rows)
        {
            int success = 0, failed = 0;
            var effectiveTenantId =    await _tenantGuard.GetEffectiveTenantIdAsync();

            var db = await _operationalContextProvider.GetContextAsync();

            foreach (var row in rows)
            {
                try
                {
                    var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawJson) ?? new();
                    string Get(string k) => fields.TryGetValue(k, out var v) ? v.Trim() : "";

                    var customer = new Customer
                    {
                        CustomerName = Get("CustomerName"),
                        ContactNumber = Get("ContactNumber").NullIfEmpty(),
                        Email = Get("Email").NullIfEmpty(),
                        Address = Get("Address").NullIfEmpty(),
                        CustomerType = Get("CustomerType").NullIfEmpty() ?? "Walk-in",
                        IsActive = true,
                        TenantId = effectiveTenantId,
                        CreatedAt = DateTime.Now

                    };

                    db.Customers.Add(customer);
                    row.Status = "Imported";
                    success++;
                }
                catch (Exception ex)
                {
                    row.Status = "Failed";
                    row.ErrorMessage = ex.Message;
                    failed++;
                }
            }

            await db.SaveChangesAsync();
            return (success, failed);
        }

        // ============================================
        // IMPORT SUPPLIERS
        // ============================================

        private async Task<(int success, int failed)> ImportSuppliersAsync(List<ImportBatchRow> rows)
        {
            int success = 0, failed = 0;
            var effectiveTenantId =    await _tenantGuard.GetEffectiveTenantIdAsync();

            var db = await _operationalContextProvider.GetContextAsync();

            foreach (var row in rows)
            {
                try
                {
                    var fields = JsonSerializer.Deserialize<Dictionary<string, string>>(row.RawJson) ?? new();
                    string Get(string k) => fields.TryGetValue(k, out var v) ? v.Trim() : "";

                    var supplier = new Supplier
                    {
                        SupplierName = Get("SupplierName"),
                        ContactPerson = Get("ContactPerson").NullIfEmpty(),
                        ContactNumber = Get("ContactNumber").NullIfEmpty(),
                        Email = Get("Email").NullIfEmpty(),
                        Address = Get("Address").NullIfEmpty(),
                        Remarks = Get("Remarks").NullIfEmpty(),
                        IsActive = true,
                        TenantId = effectiveTenantId,
                        CreatedAt = DateTime.Now
                    };

                    db.Suppliers.Add(supplier);
                    row.Status = "Imported";
                    success++;
                }
                catch (Exception ex)
                {
                    row.Status = "Failed";
                    row.ErrorMessage = ex.Message;
                    failed++;
                }
            }

            await db.SaveChangesAsync();
            return (success, failed);
        }

        // ============================================
        // TEMPLATE GENERATION
        // ============================================

        public byte[] GenerateTemplate(string importType)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(importType);

            var headers = GetAllFields(importType);
            for (int i = 0; i < headers.Count; i++)
            {
                var cell = ws.Cell(1, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
                cell.Style.Font.FontColor = XLColor.White;
            }

            ws.Columns().AdjustToContents();

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }
    }

    internal static class StringExtensions
    {
        public static string? NullIfEmpty(this string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
