using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using MNBEMART.Data;
using MNBEMART.Services;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddControllersWithViews();

// ✅ Cấu hình DbContext với SQL Server
// Add DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ✅ Cấu hình xác thực Cookie
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/AccessDenied"; // Nếu cần phân quyền
        options.Cookie.Name = "BEMART.Auth"; // Tên cookie tùy chỉnh
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60); // Thời gian hết
        options.SlidingExpiration = true; // Gia hạn thời gian hết hạn khi người dùng hoạt động
    });
// ✅ Cấu hình DI cho StockService
builder.Services.AddScoped<IStockService, StockService>();

builder.Services.AddScoped<IDocumentNumberingService, DocumentNumberingService>();
builder.Services.AddControllersWithViews(o =>
{
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});


builder.Services.AddAuthorization();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

// ✅ Thứ tự quan trọng
app.UseAuthentication();  // PHẢI có dòng này trước Authorization
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
