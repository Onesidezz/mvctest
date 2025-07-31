using Microsoft.EntityFrameworkCore;
using mvctest.Context;
using mvctest.Models;
using mvctest.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== 1. Configure Database & Infrastructure Services =====
builder.Services.AddDbContext<ContentManagerContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ===== 2. Register Configuration (AppSettings) =====
builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("AppSettings"));

// ===== 3. Register Application Services =====
builder.Services.AddScoped<IContentManager, ContentManager>();
builder.Services.AddScoped<IChatMLService, ChatMLService>();
builder.Services.AddScoped<IStartupFunctionalities, MyStartupTasks>();
builder.Services.AddScoped<ICachedCount,CachedCount>();
builder.Services.AddSingleton<ILuceneInterface, LuceneServices>();
// ===== 4. Register HTTP & Session Services =====
builder.Services.AddHttpClient(); // 👈 Register HttpClientFactory
builder.Services.AddHttpContextAccessor(); // 👈 For accessing HttpContext in services

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ===== 5. Register MVC/Controllers =====
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ===== Middleware Pipeline =====
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession(); // 👈 Must come after UseRouting()
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();