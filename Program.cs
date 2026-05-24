using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HardwareManagementSystem.Data.Seeders;
using HardwareManagementSystem.Services;
using QuestPDF.Infrastructure;
using HardwareManagementSystem.Services.Pdf;

var builder = WebApplication.CreateBuilder(args);

// ============================================
// QUESTPDF LICENSE
// ============================================

QuestPDF.Settings.License =
    LicenseType.Community;

// ============================================
// DATABASE
// ============================================

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")));

// ============================================
// SERVICES
// ============================================

builder.Services.AddScoped<PermissionService>();

builder.Services.AddScoped<AuditService>();

builder.Services.AddScoped<NotificationService>();

builder.Services.AddScoped<ReportPdfService>();

builder.Services.AddMemoryCache();

// ============================================
// MVC
// ============================================

builder.Services.AddControllersWithViews();

// ============================================
// IDENTITY
// ============================================

builder.Services
.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;

    // ============================================
    // LOCKOUT SETTINGS
    // ============================================

    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

// ============================================
// COOKIE
// ============================================

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";

    options.AccessDeniedPath = "/Account/AccessDenied";
});

var app = builder.Build();

// ============================================
// MIDDLEWARE
// ============================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

// ============================================
// ROUTES
// ============================================

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

// ============================================
// DB SEEDER
// ============================================

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;

    await DbSeeder.SeedAdminAsync(services);

    await DbSeeder.SeedRolePermissionsAsync(services);
}

app.Run();