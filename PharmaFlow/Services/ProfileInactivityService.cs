using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;

namespace PharmaFlow.Services;

public sealed class ProfileInactivityService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProfileInactivityService> _logger;

    public ProfileInactivityService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProfileInactivityService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once shortly after application startup, then check daily.
        await DeactivateInactiveProfilesAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await DeactivateInactiveProfilesAsync(stoppingToken);
        }
    }

    private async Task DeactivateInactiveProfilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cutoff = DateTime.UtcNow.AddDays(-60);

            var affected = await db.Profiles
                .Where(p => p.ActiveStatus == 1 &&
                            (p.LastLoginAt == null || p.LastLoginAt <= cutoff))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(p => p.ActiveStatus, (short)0)
                    .SetProperty(p => p.LastLogoutAt, DateTime.UtcNow), cancellationToken);

            if (affected > 0)
                _logger.LogInformation("Marked {Count} profile(s) inactive after 60 days without a login.", affected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Application is shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deactivate profiles inactive for 60 days.");
        }
    }
}
