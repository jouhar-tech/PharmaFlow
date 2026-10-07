using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using PharmaFlow.Data;
using PharmaFlow.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddMemoryCache();
builder.Services.AddDbContextPool<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
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
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();
