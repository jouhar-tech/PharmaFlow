using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpClient<ISupabaseAuthService, SupabaseAuthService>();
builder.Services.AddHttpClient<IInvoiceVisionService, GeminiInvoiceVisionService>();
builder.Services.AddHttpClient("IndiaMedicine");
builder.Services.AddHttpClient("OpenFda", client => client.BaseAddress = new Uri("https://api.fda.gov"));
builder.Services.AddHttpClient("OpenFoodFacts", client =>
{
    client.BaseAddress = new Uri("https://world.openfoodfacts.org");
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
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}")
    .WithStaticAssets();

app.Run();
