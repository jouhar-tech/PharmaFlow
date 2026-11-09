using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using PharmaFlow.Data;
using PharmaFlow.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();

var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is not configured.");

builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseNpgsql(defaultConnectionString));

// Render containers can restart or scale to zero. Production sessions and
// Data Protection keys must therefore live outside the web container.
if (builder.Environment.IsProduction())
{
    builder.Services.AddDistributedPostgresCache(options =>
    {
        options.ConnectionString = defaultConnectionString;
        options.SchemaName = "pharmaflow_private";
        options.TableName = "session_cache";
        options.CreateIfNotExists = true;
        options.DefaultSlidingExpiration = TimeSpan.FromDays(14);
        options.ExpiredItemsDeletionInterval = TimeSpan.FromHours(1);
    });

    builder.Services.AddDataProtection()
        .SetApplicationName("PharmaFlow")
        .PersistKeysToDbContext<ApplicationDbContext>();
}
else
{
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes
        .Concat(["application/json"]);
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Fastest;
});

builder.Services.AddHttpClient<ISupabaseAuthService, SupabaseAuthService>();
builder.Services.AddHttpClient<IInvoiceVisionService, GeminiInvoiceVisionService>();
builder.Services.AddHttpClient("IndiaMedicine", client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient("OpenFda", client =>
{
    client.BaseAddress = new Uri("https://api.fda.gov");
    client.Timeout = TimeSpan.FromSeconds(8);
});
builder.Services.AddHttpClient("OpenFoodFacts", client =>
{
    client.BaseAddress = new Uri("https://world.openfoodfacts.org");
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PharmaFlow/1.0 (pharmaflow-app)");
});
builder.Services.AddScoped<IGlobalProductCatalogService, GlobalProductCatalogService>();
builder.Services.AddSingleton<WebPush.WebPushClient>();
builder.Services.AddScoped<IPharmaFlowPushNotificationService, PharmaFlowPushNotificationService>();
builder.Services.AddScoped<IPharmaFlowSavingsService, PharmaFlowSavingsService>();
builder.Services.AddHostedService<NotificationSchedulerService>();
builder.Services.AddHostedService<ProfileInactivityService>();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = ".PharmaFlow.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.MaxAge = TimeSpan.FromDays(14);
    options.IdleTimeout = TimeSpan.FromDays(14);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseResponseCompression();
}

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
    {
        var path = context.Context.Request.Path.Value;
        if (!string.Equals(path, "/service-worker.js", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(path, "/offline.html", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl =
                "public,max-age=604800,must-revalidate";
        }
    }
});
app.UseRouting();
app.UseSession();

// Session cookies normally disappear when the browser closes. Reissue the
// authenticated session cookie with a fresh 14-day expiry on each app request.
// The PostgreSQL-backed session entry has the same 14-day sliding idle timeout.
app.Use(async (context, next) =>
{
    const string sessionCookieName = ".PharmaFlow.Session";

    if (context.Request.Cookies.TryGetValue(sessionCookieName, out var sessionCookieValue))
    {
        await context.Session.LoadAsync(context.RequestAborted);

        var hasAuthenticatedSession =
            !string.IsNullOrWhiteSpace(context.Session.GetString("SupabaseUserId")) &&
            !string.IsNullOrWhiteSpace(context.Session.GetString("ProfileId"));

        if (hasAuthenticatedSession)
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Cookies.Append(
                    sessionCookieName,
                    sessionCookieValue,
                    new CookieOptions
                    {
                        HttpOnly = true,
                        IsEssential = true,
                        Secure = true,
                        SameSite = SameSiteMode.Lax,
                        Path = "/",
                        MaxAge = TimeSpan.FromDays(14)
                    });

                return Task.CompletedTask;
            });
        }
    }

    await next();
});

app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();
