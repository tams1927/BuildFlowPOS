using HardwareManagementSystem.Data;
using HardwareManagementSystem.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using HardwareManagementSystem.Data.Seeders;
using HardwareManagementSystem.Filters;
using HardwareManagementSystem.Services;
using HardwareManagementSystem.Services.TenantDatabases;
using QuestPDF.Infrastructure;
using HardwareManagementSystem.Services.Pdf;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

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
builder.Services.AddScoped<DocumentPdfService>();

builder.Services.AddScoped<ExcelImportService>();

builder.Services.AddScoped<BranchService>();

builder.Services.AddScoped<ItemUnitConversionService>();
builder.Services.AddScoped<ICurrencyFormatter, CurrencyFormatter>();

builder.Services.AddScoped<ITenantContext, TenantContext>();

builder.Services.AddScoped<TenantService>();

builder.Services.AddScoped<TenantGuard>();

builder.Services.AddScoped<TenantLimitGuard>();

builder.Services.AddScoped<TenantStatusFilter>();

// ── Phase 5.0B: Database-per-tenant connection resolver layer ──────────────
// These services are registered for future Phase 5.0C routing. They are NOT
// injected into any runtime controller and do NOT replace ApplicationDbContext.
// All tenants remain in Shared mode, so the resolver returns DefaultConnection.
builder.Services.AddScoped<ITenantDatabaseResolver, TenantDatabaseResolver>();
builder.Services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
builder.Services.AddScoped<ITenantDatabaseProvisioningService, TenantDatabaseProvisioningService>();
builder.Services.AddScoped<ITenantDataMigrationService, TenantDataMigrationService>();
builder.Services.AddScoped<ITenantOperationalContextProvider, TenantOperationalContextProvider>();

builder.Services.AddHttpContextAccessor();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromHours(8);
    options.Cookie.Name       = ".HardBuild.Session";
    options.Cookie.HttpOnly   = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite   = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Services.AddMemoryCache();

// ============================================
// RATE LIMITING
// ============================================
// Global policy: 300 req/min per IP — conservative enough for normal POS usage.
// Login policy: 10 req/min per IP — reduces brute-force risk.
// Identity lockout (5 attempts / 15 min) remains the primary auth guard.

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Global fallback: 300 requests per minute per IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit          = 300,
            Window               = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit           = 0
        });
    });

    // Stricter policy for the login endpoint
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit          = 10;
        opt.Window               = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit           = 0;
    });
});

// ============================================
// REQUEST SIZE LIMITS
// ============================================
// Global max body size: 15 MB — protects against abuse on normal forms.
// The Excel import controller already enforces its own 10 MB limit via
//   [RequestSizeLimit(10_485_760)] on the relevant action; the 15 MB
//   global ceiling deliberately stays above that so the import still works.
// Note: IIS has its own maxAllowedContentLength; set it >= 15728640 in web.config.

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(opt =>
{
    opt.MultipartBodyLengthLimit = 15 * 1024 * 1024; // 15 MB
    opt.ValueLengthLimit         = 4 * 1024 * 1024;  //  4 MB (form fields)
});
builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 15 * 1024 * 1024; // 15 MB
});

// ============================================
// HEALTH CHECKS
// ============================================

builder.Services.AddHealthChecks();

// ============================================
// MVC
// ============================================

builder.Services.AddControllersWithViews(options =>
{
    // Globally enforces tenant suspension/expiry mid-session for all MVC actions.
    // SuperAdmin, Account controller, and [AllowAnonymous] actions are exempt.
    options.Filters.Add<TenantStatusFilter>();
    options.Filters.Add<TenantCurrencyFilter>();
});

// ============================================
// IDENTITY
// ============================================

builder.Services
.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    // Pilot-hardened password policy (Phase 4.7).
    // Existing password HASHES are unaffected; policy only applies when
    // setting or changing a password, not when verifying an existing hash.
    options.Password.RequireDigit            = true;
    options.Password.RequireUppercase        = false;   // relaxed for POS operators
    options.Password.RequireNonAlphanumeric  = false;   // relaxed for POS operators
    options.Password.RequiredLength          = 8;

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
    options.LoginPath         = "/Account/Login";
    options.AccessDeniedPath  = "/Account/AccessDenied";

    options.Cookie.Name       = ".HardBuild.Auth";
    options.Cookie.HttpOnly   = true;
    options.Cookie.SameSite   = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;

    options.ExpireTimeSpan    = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

var app = builder.Build();

// ============================================
// PHASE 5.0D.1-QA — DEV-ONLY FULL-CYCLE TEST RUNNER
// ============================================
// Runs ONLY when launched explicitly with "--qa-phase50d1" AND in Development.
// It executes the automated pilot test and exits WITHOUT starting the web server.
// Never runs during normal startup.
if (args.Contains("--qa-phase50d1"))
{
    await HardwareManagementSystem.Tools.Phase50D1QaRunner.RunAsync(app);
    return;
}

// ============================================
// PHASE 5.0D.2-QA — DEV-ONLY FULL MODULE CUTOVER TEST RUNNER
// ============================================
// Runs ONLY when launched explicitly with "--qa-phase50d2" AND in Development.
// Exercises the FULL operational module set against the dedicated database and exits
// WITHOUT starting the web server. Never runs during normal startup.
if (args.Contains("--qa-phase50d2"))
{
    await HardwareManagementSystem.Tools.Phase50D2QaRunner.RunAsync(app);
    return;
}

// ============================================
// PHASE UM-1 — DEV-ONLY DEMO DATA SEEDER (for user-manual screenshots)
// ============================================
// Runs ONLY when launched explicitly with "--seed-demo" AND in Development.
// Seeds a clean, professionally-named sample tenant and exits WITHOUT starting
// the web server. Idempotent. Never runs during normal startup.
if (args.Contains("--seed-demo"))
{
    await HardwareManagementSystem.Tools.DemoManualSeeder.RunAsync(app);
    return;
}

if (args.Contains("--qa-phase51"))
{
    await HardwareManagementSystem.Tools.Phase51QaRunner.RunAsync(app);
    return;
}

if (args.Contains("--qa-rc12"))
{
    await HardwareManagementSystem.Tools.Rc12QaRunner.RunAsync(app);
    return;
}

// ============================================
// MIDDLEWARE
// ============================================

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");

    app.UseHsts();
}

app.UseHttpsRedirection();

// ============================================
// SECURITY HEADERS
// ============================================

app.Use(async (context, next) =>
{
    context.Response.Headers.TryAdd("X-Frame-Options", "DENY");

    context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");

    context.Response.Headers.TryAdd(
        "Referrer-Policy",
        "strict-origin-when-cross-origin");

    context.Response.Headers.TryAdd(
        "Permissions-Policy",
        "camera=(), microphone=(), geolocation=()");

    // Content-Security-Policy: restrict resource origins to self.
    // unsafe-inline is required for Bootstrap/Falcon inline styles and
    // SweetAlert2 injected styles.  Adjust in Phase 5.0 once a nonce
    // strategy is available.
    context.Response.Headers.TryAdd(
        "Content-Security-Policy",
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' cdn.jsdelivr.net cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' cdn.jsdelivr.net cdnjs.cloudflare.com fonts.googleapis.com; " +
        "font-src 'self' data: fonts.gstatic.com cdn.jsdelivr.net cdnjs.cloudflare.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none';");

    await next();
});

app.UseStaticFiles();

app.UseRouting();

app.UseRateLimiter();

app.UseSession();

app.UseAuthentication();

app.UseAuthorization();

// ============================================
// HEALTH CHECK ENDPOINT
// ============================================

app.MapHealthChecks("/health").AllowAnonymous();

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

    // ─────────────────────────────────────────────────────────────────────
    // DEVELOPMENT DATA CLEANUP
    // Removes old / demo / test operational data from the dev database.
    //
    // SAFETY GATES (both must be true to run):
    //   1. ASPNETCORE_ENVIRONMENT == Development
    //   2. SeedSettings:EnableDemoDataCleanup == true  (default: false)
    //
    // Set the flag in appsettings.Development.json, then restart.
    // Reset the flag to false immediately after to prevent repeated wipes.
    //
    // NEVER enable in production.
    // ─────────────────────────────────────────────────────────────────────
    if (app.Environment.IsDevelopment())
    {
        var cleanupEnabled = app.Configuration
            .GetValue<bool>("SeedSettings:EnableDemoDataCleanup");

        if (cleanupEnabled)
        {
            await DevelopmentDataResetService.CleanDemoDataAsync(services);
        }
    }
}

app.Run();