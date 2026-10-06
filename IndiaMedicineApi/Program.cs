using IndiaMedicineApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<MedicineCatalogService>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "india-medicine-api" }));

app.MapGet("/search", async (
    string? q,
    int? limit,
    MedicineCatalogService catalog,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        return Results.BadRequest(new { message = "q must contain at least 2 characters." });

    var requestedLimit = Math.Clamp(limit ?? 20, 1, 100);
    var results = await catalog.SearchAsync(q, requestedLimit, cancellationToken);

    return Results.Ok(new { results });
});

app.MapGet("/medicine/{id:long}", async (
    long id,
    MedicineCatalogService catalog,
    CancellationToken cancellationToken) =>
{
    if (id <= 0)
        return Results.BadRequest(new { message = "Invalid medicine id." });

    var medicine = await catalog.GetAsync(id, cancellationToken);

    return medicine is null
        ? Results.NotFound(new { message = "Medicine not found." })
        : Results.Ok(medicine);
});

app.Run();
