using System.Globalization;
using Accountant.DataAccess;
using Accountant.Email;
using Accountant.Identity.Models;
using Accountant.Jobs;
using Accountant.MySql;
using Accountant.Notifications;
using Accountant.Storage;
using Accountant.Web.Areas.Administration;
using Accountant.Web.Areas.Administration.Services;
using Accountant.Web.Areas.App.Middleware;
using Accountant.Web.Areas.App.Services;
using Hangfire;
using Microsoft.Extensions.Options;
using Braikov.Identity.Core;
using Braikov.Identity.Core.Resources;
using Braikov.Identity.Events.MySql;
using Braikov.Identity.Notifications;
using Braikov.Identity.ShortCodes.MySql;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Accountant")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Accountant is not configured. " +
        "Set it in appsettings.{Environment}.json or via dotnet user-secrets " +
        "(see useful_commands.md > Database).");
builder.Services.AddAccountantMySql(connectionString);

// Identity foundation now comes from Braikov.Identity.* packages.
// Cookie + password + lockout + sign-in options bound from the "Identity"
// section of appsettings.json. .UseNotificationDispatcher() routes auth
// email through Braikov.Notifications (audit + persistence + retry).
builder.Services
    .AddBraikovIdentity<ApplicationUser, IdentityRole<int>>(builder.Configuration)
    .AddEntityFrameworkStores<AccountantDbContext>()
    .AddDefaultTokenProviders()
    .UseNotificationDispatcher();

// Re-validate the auth cookie every 5 minutes. Without this, role changes
// (e.g. AdminRoleBootstrapService promoting a user) only take effect on the
// next login because the cookie carries a snapshot of the user's claims.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

// Localization — needed so resource-keyed ViewModels render translated
// labels and the controller's ModelState errors come back in BG/EN.
// NOTE: deliberately no ResourcesPath — the package's SharedResource is
// in the Resources sub-namespace and the .resx ships under /Resources/.
// Setting ResourcesPath here would double the prefix.
builder.Services.AddLocalization();

builder.Services
    .AddControllersWithViews()
    .AddDataAnnotationsLocalization(o =>
        o.DataAnnotationLocalizerProvider = (_, factory) =>
            factory.Create(typeof(SharedResource)))
    .AddViewLocalization();

builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var supported = new[] { new CultureInfo("bg-BG"), new CultureInfo("en-GB") };
    o.DefaultRequestCulture = new RequestCulture("bg-BG");
    o.SupportedCultures = supported;
    o.SupportedUICultures = supported;
});

// Email + notifications. Order matters: AddAccountantEmail must run BEFORE
// AddAccountantNotifications because the notifications adapter only wires
// the email channel sender if an IEmailSender is already registered.
builder.Services.AddAccountantEmail(builder.Configuration);
builder.Services.AddAccountantNotifications(builder.Configuration);

// Audit log — fresh `AccountEvents` table in the same MySQL DB.
// dotnet ef database update --context AccountEventDbContext applies the
// bundled migration.
builder.Services.AddBraikovIdentityEventsMySql(connectionString);

// Short-code-to-token mapping (6-digit code companion to long URLs in
// confirmation / reset emails). dotnet ef database update --context
// ShortCodeTokenDbContext applies the bundled migration.
builder.Services.AddBraikovIdentityShortCodesMySql(builder.Configuration, connectionString);

// Storage abstraction — LocalFileStore by default, persists uploads under
// `App_Data/uploads/yyyy/MM/`. Thumbnail renderers for images + PDF are
// also wired here.
builder.Services.AddAccountantStorage(builder.Configuration);

// Background jobs (Hangfire) — MySQL storage in the same `accountant` DB
// using `Hangfire_*` table prefix. Dashboard mounted at /Administration/Hangfire
// (admin role only — see AdminDashboardAuthorizationFilter).
builder.Services.AddAccountantJobs(builder.Configuration, connectionString);

// Tenant module — TenantService is the business logic; IActiveTenantAccessor
// is the per-request snapshot populated by ActiveTenantMiddleware.
builder.Services.AddScoped<TenantService>();
builder.Services.AddScoped<IActiveTenantAccessor, ActiveTenantAccessor>();
builder.Services.AddScoped<WorkspaceService>();
builder.Services.AddScoped<AdminDashboardService>();
builder.Services.AddScoped<AdminCatalogService>();
builder.Services.AddHostedService<AdminRoleBootstrapService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Public/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Active-tenant middleware. Must run AFTER auth (needs HttpContext.User) and
// BEFORE controllers (controllers read IActiveTenantAccessor.Current).
app.UseMiddleware<ActiveTenantMiddleware>();

// Hangfire dashboard. Auth filter rejects anonymous / non-Admin requests with 401.
var hangfireOptions = app.Services.GetRequiredService<IOptions<HangfireOptions>>().Value;
app.UseHangfireDashboard(hangfireOptions.DashboardPath, new DashboardOptions
{
    Authorization = new[] { app.Services.GetRequiredService<AdminDashboardAuthorizationFilter>() },
});

app.MapStaticAssets();

// Area-specific default for /App/ — WorkspaceController is the entry point
// (folder tree + documents grid). Must come BEFORE the generic area route
// so `/App` resolves here instead of defaulting to a non-existent HomeController.
app.MapAreaControllerRoute(
    name: "app_default",
    areaName: "App",
    pattern: "App/{controller=Workspace}/{action=Index}/{id?}")
    .WithStaticAssets();

// Public area route — handles /, /Public/Home/*, /Public/<controller>/<action>.
app.MapControllerRoute(
    name: "public_area",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

// Default landing page = Public area HomeController.Index. When the real product
// lands, this route will move to a different controller / area; the Public area
// can be removed as a unit (see Areas/Public/README.md).
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}",
    defaults: new { area = "Public", controller = "Home", action = "Index" })
    .WithStaticAssets();

// Dev-only attribute-routed controllers (DevDiagnosticsController etc.).
// MapControllers() picks up `[Route(...)]`-decorated actions; gating on
// IsDevelopment keeps /dev/* off production deployments entirely.
if (app.Environment.IsDevelopment())
{
    app.MapControllers();
}

app.Run();
