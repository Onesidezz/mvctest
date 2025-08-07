using Hangfire;
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
builder.Services.AddScoped<ICachedCount,CachedCount>();

// Register enhanced Lucene services with high-resolution capabilities
builder.Services.AddSingleton<ILuceneInterface, LuceneServices>();

// Register the high-resolution text analyzer
builder.Services.AddSingleton<HighResolutionTextAnalyzer>();


// ===== 4. Register HTTP & Session Services =====
builder.Services.AddHttpClient(); // 👈 Register HttpClientFactory
builder.Services.AddHttpContextAccessor(); // 👈 For accessing HttpContext in services

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// In Program.cs
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection")));
// ===== 5. Register MVC/Controllers =====
builder.Services.AddControllersWithViews();

var app = builder.Build();

// ===== Middleware Pipeline =====
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
app.UseHangfireDashboard();
app.UseHangfireServer();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession(); // 👈 Must come after UseRouting()
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();