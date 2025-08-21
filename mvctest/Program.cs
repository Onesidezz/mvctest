using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using mvctest.Context;
using mvctest.Models;
using mvctest.Services;

var builder = WebApplication.CreateBuilder(args);

// ===== 1. Configure Database & Infrastructure Services =====
builder.Services.AddDbContext<ContentManagerContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add distributed cache for general caching (in-memory for now, Redis for production)
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();

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

// Register search session management service
builder.Services.AddScoped<ISessionSearchManager, SessionSearchManager>();

// Register background search cleanup service
builder.Services.AddHostedService<SearchCleanupService>();

// Two-phase search removed as it was not working properly


// ===== 4. Register HTTP & Session Services =====
builder.Services.AddHttpClient(); // 👈 Register HttpClientFactory
builder.Services.AddHttpContextAccessor(); // 👈 For accessing HttpContext in services
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestHeadersTotalSize = 65536; // 64 KB - increased from 32 KB to handle larger headers
    options.Limits.MaxRequestHeaderCount = 100; // Increase header count limit
});

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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