using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using MNBEMART.Data;
using MNBEMART.Services;
using MNBEMART.Hubs;

var builder = WebApplication.CreateBuilder(args);

// ✅ Cấu hình DbContext với SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ✅ Cấu hình xác thực Cookie
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "BEMART.Auth";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
    });
// ✅ Cấu hình DI cho StockService
builder.Services.AddScoped<IStockService, StockService>();
builder.Services.AddScoped<IDocumentNumberingService, DocumentNumberingService>();
builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IExcelService, ExcelService>();
builder.Services.AddScoped<IImportService, ExcelImportService>();
builder.Services.AddScoped<IAutoOrderService>(sp => 
    new AutoOrderService(
        sp.GetRequiredService<AppDbContext>(),
        sp.GetRequiredService<IDocumentNumberingService>(),
        sp.GetRequiredService<ILogger<AutoOrderService>>(),
        sp));
builder.Services.AddHostedService<AutoOrderBackgroundService>();
builder.Services.AddHostedService<ExpiryAlertBackgroundService>();
builder.Services.AddHostedService<PeriodicReportBackgroundService>();
builder.Services.AddHostedService<UnconfirmedDocumentBackgroundService>();
builder.Services.AddHostedService<NotificationEmailDigestService>();
builder.Services.AddScoped<ILotManagementService, LotManagementService>();
builder.Services.AddScoped<ITransferOptimizationService, TransferOptimizationService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<INotificationSettingsService, NotificationSettingsService>();
builder.Services.AddScoped<IQuickActionService, QuickActionService>();
builder.Services.AddScoped<ICostingService, CostingService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IReportEmailService, ReportEmailService>();
builder.Services.AddScoped<IReportGenerationService, ReportGenerationService>();
builder.Services.AddScoped<IDemandForecastingService, DemandForecastingService>();
builder.Services.AddScoped<IOptimalOrderQuantityService, OptimalOrderQuantityService>();
builder.Services.AddScoped<IStockoutPredictionService, StockoutPredictionService>();
builder.Services.AddScoped<IImageAnalysisService, ImageAnalysisService>();
builder.Services.AddScoped<IDuplicateDetectionService, DuplicateDetectionService>();
builder.Services.AddScoped<IMaterialClassificationService, MaterialClassificationService>();

// ✅ Gemini Chatbot
builder.Services.AddHttpClient<IChatService, GeminiChatService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
});

// ✅ Image Analysis Service
builder.Services.AddHttpClient<IImageAnalysisService, ImageAnalysisService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
});

builder.Services.AddHttpContextAccessor();

// ✅ Memory Cache for static data (warehouses, materials, etc.)
builder.Services.AddMemoryCache();

builder.Services
    .AddLocalization(options => options.ResourcesPath = "Resources")
    .AddControllersWithViews(o =>
    {
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
        // Ghi audit cho mọi action thành công
        o.Filters.Add<MNBEMART.Filters.AuditActionFilter>();

        // Ngăn trình duyệt cache các trang MVC
        o.Filters.Add(new ResponseCacheAttribute
        {
            NoStore = true,
            Location = ResponseCacheLocation.None
        });
    })
    .AddViewLocalization()
    .AddDataAnnotationsLocalization();

builder.Services.AddAuthorization();

// ✅ SignalR for real-time notifications
builder.Services.AddSignalR();

// ✅ Response Compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.BrotliCompressionProvider>();
    options.Providers.Add<Microsoft.AspNetCore.ResponseCompression.GzipCompressionProvider>();
});

var app = builder.Build();

// Localization: chỉ dùng tiếng Việt
var supportedCultures = new[] { new CultureInfo("vi") };
var localizationOptions = new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("vi"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
};
localizationOptions.RequestCultureProviders.Clear();
app.UseRequestLocalization(localizationOptions);

// ✅ Response Compression for better performance
app.UseResponseCompression();

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static files for 7 days
        var durationInSeconds = 60 * 60 * 24 * 7;
        ctx.Context.Response.Headers.Append("Cache-Control", $"public,max-age={durationInSeconds}");
    }
});

app.UseRouting();

// ✅ Thứ tự quan trọng
app.UseAuthentication();
app.UseAuthorization();

// Map SignalR hub
app.MapHub<NotificationHub>("/notificationHub");

// Map attribute-routed APIs (e.g. /api/chat/ask)
app.MapControllers();

// Conventional route for MVC views
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
