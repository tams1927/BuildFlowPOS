using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using HardwareManagementSystem.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Import", "View")]
    public class ImportController : OperationalDbController
    {
        private readonly AuditService _auditService;
        private readonly ExcelImportService _importService;
        private readonly ITenantContext _tenantContext;
        private readonly TenantGuard _tenantGuard;

        private static readonly string[] AllowedImportTypes =
            ["Products", "OpeningStock", "Customers", "Suppliers"];

        private const long MaxUploadSizeBytes = 10 * 1024 * 1024; // 10MB

        private static readonly string[] AllowedExtensions =
            [".xlsx", ".xls"];

        private static readonly string[] AllowedContentTypes =
        [
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-excel",
            "application/octet-stream"
        ];

        private string? ValidateExcelUpload(IFormFile file, out string? sanitizedFileName)
        {
            sanitizedFileName = null;

            if (file == null || file.Length <= 0)
            {
                return "Please select an Excel file to upload.";
            }

            if (file.Length > MaxUploadSizeBytes)
            {
                return "File exceeds the 10MB upload limit.";
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

            if (!AllowedExtensions.Contains(ext))
            {
                return "Invalid file type. Only .xlsx and .xls files are allowed.";
            }

            if (!AllowedContentTypes.Contains(file.ContentType))
            {
                return "Invalid file content type. Please upload a valid Excel document.";
            }

            sanitizedFileName = Path.GetFileName(file.FileName);

            if (string.IsNullOrWhiteSpace(sanitizedFileName))
            {
                return "Invalid filename.";
            }

            return null;
        }

        private async Task LogRejectedImportUploadAsync(string reason, string? fileName = null)
        {
            var description = string.IsNullOrWhiteSpace(fileName)
                ? $"Import upload rejected: {reason}"
                : $"Import upload rejected: {reason} File: {fileName}";

            await _auditService.LogAsync(
                User,
                "Import",
                "UPLOAD REJECTED",
                description,
                "ImportBatch",
                null,
                HttpContext.Connection.RemoteIpAddress?.ToString());
        }

        public ImportController(
            ITenantOperationalContextProvider ctxProvider,
            AuditService auditService,
            ExcelImportService importService,
            ITenantContext tenantContext,
            TenantGuard tenantGuard)
            : base(ctxProvider)
        {
            _auditService = auditService;
            _importService = importService;
            _tenantContext = tenantContext;
            _tenantGuard = tenantGuard;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult DownloadTemplate(string importType)
        {
            if (!AllowedImportTypes.Contains(importType))
                return BadRequest("Invalid import type.");

            var bytes = _importService.GenerateTemplate(importType);
            var fileName = $"Template_{importType}.xlsx";

            return File(
                bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        [HttpPost]
        [PermissionAuthorize("Import", "Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upload(IFormFile file, string importType)
        {
            if (!AllowedImportTypes.Contains(importType))
            {
                TempData["ErrorMessage"] = "Invalid import type selected.";
                return RedirectToAction(nameof(Index));
            }

            if (file == null || file.Length <= 0)
            {
                TempData["ErrorMessage"] = "Please select an Excel file to upload.";
                return RedirectToAction(nameof(Index));
            }

            var validationError = ValidateExcelUpload(file, out var sanitizedFileName);

            if (validationError != null)
            {
                if (validationError.Contains("Invalid file") ||
                    validationError.Contains("content type") ||
                    validationError.Contains("10MB") ||
                    validationError.Contains("filename"))
                {
                    await LogRejectedImportUploadAsync(
                        validationError,
                        Path.GetFileName(file.FileName));
                }

                TempData["ErrorMessage"] = validationError;
                return RedirectToAction(nameof(Index));
            }

            using var stream = file.OpenReadStream();

            List<string> headers;

            try
            {
                headers = _importService.ReadHeaders(stream);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Could not read Excel file: {ex.Message}";
                return RedirectToAction(nameof(Index));
            }

            if (headers.Count == 0)
            {
                TempData["ErrorMessage"] = "The Excel file has no column headers in row 1.";
                return RedirectToAction(nameof(Index));
            }

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var tenantId = await _tenantGuard.GetEffectiveTenantIdAsync();

            var batch = new ImportBatch
            {
                TenantId = tenantId,
                ImportType = importType,
                FileName = sanitizedFileName!,
                Status = "Pending",
                CreatedByUserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            };

            _context.ImportBatches.Add(batch);
            await _context.SaveChangesAsync();

            TempData["ImportHeaders"] =
                System.Text.Json.JsonSerializer.Serialize(headers);

            TempData["ImportBatchId"] = batch.Id;
            TempData["ImportType"] = importType;

            return RedirectToAction(nameof(Mapping), new { batchId = batch.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Mapping(int batchId)
        {
            var batch = await _context.ImportBatches.FindAsync(batchId);

            if (batch == null)
                return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(batch.TenantId))
                return Forbid();

            List<string>? headers = null;

            if (TempData["ImportHeaders"] is string json)
            {
                headers =
                    System.Text.Json.JsonSerializer
                        .Deserialize<List<string>>(json);
            }

            if (headers == null || headers.Count == 0)
            {
                TempData["ErrorMessage"] =
                    "Session expired. Please upload the file again.";

                return RedirectToAction(nameof(Index));
            }

            TempData.Keep("ImportHeaders");
            TempData.Keep("ImportBatchId");
            TempData.Keep("ImportType");

            var vm = new ImportMappingViewModel
            {
                BatchId = batchId,
                ImportType = batch.ImportType,
                ExcelHeaders = headers,
                Mapping = ExcelImportService
                    .GetAllFields(batch.ImportType)
                    .ToDictionary(f => f, _ => string.Empty)
            };

            foreach (var field in vm.Mapping.Keys.ToList())
            {
                var match = headers.FirstOrDefault(h =>
                    h.Equals(field, StringComparison.OrdinalIgnoreCase) ||
                    h.Replace(" ", "")
                     .Equals(field, StringComparison.OrdinalIgnoreCase));

                if (match != null)
                    vm.Mapping[field] = match;
            }

            return View(vm);
        }

        [HttpPost]
        [PermissionAuthorize("Import", "Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Mapping(
            int batchId,
            IFormFile previewFile,
            IFormCollection form)
        {
            var batch = await _context.ImportBatches.FindAsync(batchId);

            if (batch == null)
                return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(batch.TenantId))
                return Forbid();

            if (batch.Status == "Completed" ||
                batch.Status == "Processing")
            {
                TempData["ErrorMessage"] =
                    "This import batch has already been confirmed.";

                return RedirectToAction(nameof(Result), new { batchId });
            }

            if (previewFile == null || previewFile.Length == 0)
            {
                TempData["ErrorMessage"] =
                    "Please re-upload the Excel file.";

                return RedirectToAction(nameof(Mapping), new { batchId });
            }

            var validationError = ValidateExcelUpload(previewFile, out _);

            if (validationError != null)
            {
                if (validationError.Contains("Invalid file") ||
                    validationError.Contains("content type") ||
                    validationError.Contains("10MB") ||
                    validationError.Contains("filename"))
                {
                    await LogRejectedImportUploadAsync(
                        validationError,
                        Path.GetFileName(previewFile.FileName));
                }

                TempData["ErrorMessage"] = validationError;
                return RedirectToAction(nameof(Mapping), new { batchId });
            }

            var mapping = new Dictionary<string, string>();

            foreach (var field in ExcelImportService.GetAllFields(batch.ImportType))
            {
                var selected =
                    form[$"mapping_{field}"].ToString();

                mapping[field] = selected ?? string.Empty;
            }

            using var stream = previewFile.OpenReadStream();

            ImportPreviewViewModel preview;

            try
            {
                preview = await _importService.BuildPreviewAsync(
                    batchId,
                    batch.ImportType,
                    stream,
                    mapping);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] =
                    $"Error reading file: {ex.Message}";

                return RedirectToAction(nameof(Mapping), new { batchId });
            }

            if (preview.Rows.Count == 0)
            {
                TempData["ErrorMessage"] =
                    "No data rows found.";

                return RedirectToAction(nameof(Mapping), new { batchId });
            }

            await _importService.SavePreviewRowsAsync(batchId, preview.Rows);

            await _auditService.LogAsync(
                User,
                "Import",
                "Preview",
                $"Previewed {preview.Rows.Count} rows for batch #{batchId}");

            return RedirectToAction(nameof(Preview), new { batchId });
        }

        [HttpGet]
        public async Task<IActionResult> Preview(int batchId)
        {
            var batch = await _context.ImportBatches
                .Include(b => b.ImportBatchRows)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
                return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(batch.TenantId))
                return Forbid();

            ViewBag.AlreadyCompleted =
                batch.Status == "Completed" ||
                batch.Status == "Processing";

            var vm = new ImportPreviewViewModel
            {
                BatchId = batchId,
                ImportType = batch.ImportType,
                SystemFields = ExcelImportService.GetAllFields(batch.ImportType),

                Rows = batch.ImportBatchRows
                    .OrderBy(r => r.RowNumber)
                    .Select(r =>
                    {
                        var fields =
                            System.Text.Json.JsonSerializer
                                .Deserialize<Dictionary<string, string>>(r.RawJson)
                            ?? new Dictionary<string, string>();

                        return new ImportPreviewRow
                        {
                            RowNumber = r.RowNumber,
                            Fields = fields,
                            Errors = string.IsNullOrWhiteSpace(r.ErrorMessage)
                                ? new List<string>()
                                : r.ErrorMessage.Split("; ").ToList(),
                            RowDbStatus = r.Status
                        };
                    }).ToList()
            };

            return View(vm);
        }

        [HttpPost]
        [PermissionAuthorize("Import", "Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm(int batchId)
        {
            var batch = await _context.ImportBatches
                .Include(b => b.ImportBatchRows)
                .FirstOrDefaultAsync(b => b.Id == batchId);

            if (batch == null)
                return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(batch.TenantId))
                return Forbid();

            if (batch.Status == "Completed")
            {
                TempData["ErrorMessage"] =
                    "This batch was already confirmed.";

                return RedirectToAction(nameof(Result), new { batchId });
            }

            if (batch.Status == "Processing")
            {
                TempData["ErrorMessage"] =
                    "This batch is currently processing.";

                return RedirectToAction(nameof(Preview), new { batchId });
            }

            var validCount =
                batch.ImportBatchRows.Count(r => r.Status == "Pending");

            if (validCount == 0)
            {
                TempData["ErrorMessage"] =
                    "No valid rows to import.";

                return RedirectToAction(nameof(Preview), new { batchId });
            }

            var userId =
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";

            ImportBatch completed;

            try
            {
                completed =
                    await _importService.ConfirmImportAsync(batchId, userId);
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;

                return RedirectToAction(nameof(Preview), new { batchId });
            }

            await _auditService.LogAsync(
                User,
                "Import",
                "Confirm",
                $"Confirmed import batch #{batchId}");

            TempData["SuccessMessage"] =
                $"Import completed: {completed.SuccessRows} imported.";

            return RedirectToAction(nameof(Result), new { batchId });
        }

        [HttpGet]
        public async Task<IActionResult> Result(int batchId)
        {
            var batch =
                await _context.ImportBatches.FindAsync(batchId);

            if (batch == null)
                return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(batch.TenantId))
                return Forbid();

            var vm = new ImportResultViewModel
            {
                BatchId = batch.Id,
                ImportType = batch.ImportType,
                FileName = batch.FileName,
                TotalRows = batch.TotalRows,
                SuccessRows = batch.SuccessRows,
                FailedRows = batch.FailedRows,
                Status = batch.Status,
                CompletedAtUtc = batch.CompletedAtUtc
            };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> History(
            int pageNumber = 1,
            int pageSize = 10)
        {
            pageSize =
                PagedResult<object>.ValidatePageSize(pageSize);

            var tenantId =
                await _tenantGuard.GetEffectiveTenantIdAsync();

            var query = _context.ImportBatches
                .AsNoTracking()
                .AsQueryable();

            if (!_tenantContext.IsGlobalUser &&
                tenantId.HasValue)
            {
                query = query.Where(b =>
                    b.TenantId == tenantId ||
                    b.TenantId == null);
            }

            query = query.OrderByDescending(b => b.CreatedAtUtc);

            var totalRecords = await query.CountAsync();

            pageNumber =
                PagedResult<object>.ValidatePageNumber(
                    pageNumber,
                    (int)Math.Ceiling(totalRecords / (double)pageSize));

            var items = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(new PagedResult<ImportBatch>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(
            int batchId,
            int pageNumber = 1,
            int pageSize = 25)
        {
            pageSize =
                PagedResult<object>.ValidatePageSize(pageSize);

            var batch =
                await _context.ImportBatches.FindAsync(batchId);

            if (batch == null)
                return NotFound();

            if (!await _tenantGuard.CanAccessTenantAsync(batch.TenantId))
                return Forbid();

            ViewBag.Batch = batch;

            var query = _context.ImportBatchRows
                .AsNoTracking()
                .Where(r => r.ImportBatchId == batchId)
                .OrderBy(r => r.RowNumber);

            var totalRecords = await query.CountAsync();

            pageNumber =
                PagedResult<object>.ValidatePageNumber(
                    pageNumber,
                    (int)Math.Ceiling(totalRecords / (double)pageSize));

            var rowItems = await query
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var systemFields =
                ExcelImportService.GetAllFields(batch.ImportType);

            ViewBag.SystemFields = systemFields;

            ViewBag.PreviewRows = rowItems.Select(r =>
            {
                var fields =
                    System.Text.Json.JsonSerializer
                        .Deserialize<Dictionary<string, string>>(r.RawJson)
                    ?? new Dictionary<string, string>();

                return new ImportPreviewRow
                {
                    RowNumber = r.RowNumber,
                    Fields = fields,
                    RowDbStatus = r.Status,
                    Errors = string.IsNullOrWhiteSpace(r.ErrorMessage)
                        ? new List<string>()
                        : r.ErrorMessage.Split("; ").ToList()
                };
            }).ToList();

            return View(new PagedResult<ImportBatchRow>
            {
                Items = rowItems,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords
            });
        }
    }
}