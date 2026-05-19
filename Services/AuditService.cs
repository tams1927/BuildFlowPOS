using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using System.Security.Claims;

namespace HardwareManagementSystem.Services
{
    public class AuditService
    {
        private readonly ApplicationDbContext _context;

        public AuditService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(
            ClaimsPrincipal user,
            string moduleName,
            string actionName,
            string description,
            string? referenceType = null,
            string? referenceId = null,
            string? ipAddress = null)
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var userName = user.Identity?.Name ?? "Unknown";

            var audit = new AuditTrail
            {
                UserId = userId,
                UserName = userName,
                ModuleName = moduleName,
                ActionName = actionName,
                Description = description,
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                IpAddress = ipAddress,
                CreatedAt = DateTime.Now
            };

            _context.AuditTrails.Add(audit);
            await _context.SaveChangesAsync();
        }
    }
}