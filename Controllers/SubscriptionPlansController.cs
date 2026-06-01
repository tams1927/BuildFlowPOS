using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    public class SubscriptionPlansController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly AuditService _auditService;

        public SubscriptionPlansController(ApplicationDbContext context, AuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Subscription Plans";
            var plans = await _context.SubscriptionPlans
                .AsNoTracking()
                .OrderBy(p => p.SortOrder)
                .ThenBy(p => p.MonthlyPrice)
                .ToListAsync();
            return View(plans);
        }

        [HttpGet]
        public IActionResult Create()
        {
            ViewData["Title"] = "New Plan";
            return View(new SubscriptionPlan { IsActive = true, MaxBranches = 3, MaxUsers = 10, MaxProducts = 500 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(SubscriptionPlan plan)
        {
            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "New Plan";
                return View(plan);
            }

            plan.CreatedAtUtc = DateTime.UtcNow;
            _context.SubscriptionPlans.Add(plan);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "SubscriptionPlans", "Create",
                $"Plan created: {plan.Name} (₱{plan.MonthlyPrice:N2}/mo, MaxBranches={plan.MaxBranches}, MaxUsers={plan.MaxUsers})");

            TempData["SuccessMessage"] = $"Plan '{plan.Name}' created.";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            ViewData["Title"] = "Edit Plan";
            var plan = await _context.SubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound();
            return View(plan);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SubscriptionPlan plan)
        {
            if (id != plan.Id) return BadRequest();

            if (!ModelState.IsValid)
            {
                ViewData["Title"] = "Edit Plan";
                return View(plan);
            }

            var existing = await _context.SubscriptionPlans.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Name        = plan.Name;
            existing.Description = plan.Description;
            existing.MonthlyPrice = plan.MonthlyPrice;
            existing.MaxBranches = plan.MaxBranches;
            existing.MaxUsers    = plan.MaxUsers;
            existing.MaxProducts = plan.MaxProducts;
            existing.IsActive    = plan.IsActive;
            existing.SortOrder   = plan.SortOrder;

            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "SubscriptionPlans", "Edit",
                $"Plan updated: {existing.Name} (₱{existing.MonthlyPrice:N2}/mo, Active={existing.IsActive})");

            TempData["SuccessMessage"] = $"Plan '{existing.Name}' updated.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var plan = await _context.SubscriptionPlans.FindAsync(id);
            if (plan == null) return NotFound();

            var inUse = await _context.Tenants.AnyAsync(t => t.SubscriptionPlanId == id);
            if (inUse)
            {
                TempData["ErrorMessage"] = "Plan is assigned to one or more tenants and cannot be deleted. Deactivate it instead.";
                return RedirectToAction(nameof(Index));
            }

            var planName = plan.Name;
            _context.SubscriptionPlans.Remove(plan);
            await _context.SaveChangesAsync();

            await _auditService.LogAsync(User, "SubscriptionPlans", "Delete",
                $"Plan deleted: {planName} (Id={id})");

            TempData["SuccessMessage"] = $"Plan '{planName}' deleted.";
            return RedirectToAction(nameof(Index));
        }
    }
}
