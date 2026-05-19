using HardwareManagementSystem.Data;
using HardwareManagementSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HardwareManagementSystem.Controllers
{
    [Authorize]
    [PermissionAuthorize("Reports", "View")]
    public class AuditTrailController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AuditTrailController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var logs = await _context.AuditTrails
                .OrderByDescending(a => a.CreatedAt)
                .Take(300)
                .ToListAsync();

            return View(logs);
        }
    }
}