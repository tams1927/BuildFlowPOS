using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace HardwareManagementSystem.Services
{
    public class PermissionAuthorizeAttribute
        : Attribute,
          IAsyncAuthorizationFilter
    {
        private readonly string _moduleName;
        private readonly string _permissionType;

        public PermissionAuthorizeAttribute(
            string moduleName,
            string permissionType = "View")
        {
            _moduleName = moduleName;
            _permissionType = permissionType;
        }

        public async Task OnAuthorizationAsync(
            AuthorizationFilterContext context)
        {
            var user = context.HttpContext.User;

            // ============================================
            // NOT LOGGED IN
            // ============================================

            if (user == null ||
                user.Identity == null ||
                !user.Identity.IsAuthenticated)
            {
                context.Result = new RedirectToActionResult(
                    "Login",
                    "Account",
                    null);

                return;
            }

            // ============================================
            // CHECK PERMISSION
            // ============================================

            var permissionService =
                context.HttpContext.RequestServices
                .GetRequiredService<PermissionService>();

            var hasPermission =
                await permissionService.HasPermissionAsync(
                    user,
                    _moduleName,
                    _permissionType
                );

            // ============================================
            // ACCESS DENIED
            // ============================================

            if (!hasPermission)
            {
                context.Result = new RedirectToActionResult(
                    "AccessDenied",
                    "Account",
                    null);

                return;
            }
        }
    }
}