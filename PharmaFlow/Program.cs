using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using System.Diagnostics;
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
builder.Services.AddHttpClient<ISupabaseStorageService, SupabaseStorageService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddHttpClient<IInvoiceVisionService, GeminiInvoiceVisionService>();
builder.Services.AddHttpClient("IndiaMedicine", client =>
{
    client.Timeout = TimeSpan.FromSeconds(45);
});
builder.Services.AddHttpClient("IndiaMedicineDataset", client =>
{
    client.Timeout = TimeSpan.FromMinutes(5);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("PharmaHex/1.0 (Indian medicine catalogue)");
});
builder.Services.AddSingleton<IIndiaMedicineDatasetService, IndiaMedicineDatasetService>();
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
builder.Services.AddSingleton<FirebaseAccessTokenProvider>();
builder.Services.AddHttpClient("FirebaseOAuth", client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddHttpClient<IFirebaseCloudMessagingService, FirebaseCloudMessagingService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(20);
});
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
    var isInventoryIndex =
        HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.Equals("/Inventory/Index", StringComparison.OrdinalIgnoreCase);
    var sessionLoadMilliseconds = 0d;

    if (context.Request.Cookies.TryGetValue(sessionCookieName, out var sessionCookieValue))
    {
        var sessionStopwatch = Stopwatch.StartNew();
        await context.Session.LoadAsync(context.RequestAborted);
        sessionStopwatch.Stop();
        sessionLoadMilliseconds = sessionStopwatch.Elapsed.TotalMilliseconds;

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

    if (isInventoryIndex)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.Append(
                "Server-Timing",
                $"inventory_session_load;dur={sessionLoadMilliseconds:0.0}");

            app.Logger.LogInformation(
                "Inventory session load took {SessionLoadMilliseconds} ms.",
                sessionLoadMilliseconds);

            return Task.CompletedTask;
        });
    }

    await next();
});

app.UseAuthorization();

// Diagnose the full Inventory MVC request, including SessionAuthorize,
// Razor view rendering and response generation. The controller-level
// Server-Timing entries intentionally measure only the action's own work.
app.Use(async (context, next) =>
{
    var isInventoryIndex =
        HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.Equals("/Inventory/Index", StringComparison.OrdinalIgnoreCase);

    if (!isInventoryIndex)
    {
        await next();
        return;
    }

    var totalRequestStopwatch = Stopwatch.StartNew();
    var firstByteStopwatch = Stopwatch.StartNew();

    context.Response.OnStarting(() =>
    {
        firstByteStopwatch.Stop();
        context.Response.Headers.Append(
            "Server-Timing",
            $"inventory_pipeline;dur={firstByteStopwatch.Elapsed.TotalMilliseconds:0.0}");

        return Task.CompletedTask;
    });

    context.Response.OnCompleted(() =>
    {
        totalRequestStopwatch.Stop();

        app.Logger.LogInformation(
            "Inventory full HTTP request completed. Status {StatusCode}; server elapsed {ElapsedMilliseconds} ms; response content length {ContentLength}; content type {ContentType}.",
            context.Response.StatusCode,
            totalRequestStopwatch.Elapsed.TotalMilliseconds,
            context.Response.ContentLength,
            context.Response.ContentType);

        return Task.CompletedTask;
    });

    await next();
});

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();
