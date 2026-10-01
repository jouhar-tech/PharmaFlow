using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;

namespace PharmaFlow.Services;

public sealed class NotificationSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationSchedulerService> _logger;
    private static readonly TimeSpan DailySendTime = new(8, 30, 0);
    private const int MaxParallelProfiles = 10;

    public NotificationSchedulerService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var india = NotificationTimeZone.GetIndiaStandardTimeZone();
            var nowUtc = DateTime.UtcNow;
            var nowIndia = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, india);

            var nextRun = nowIndia.Date.Add(DailySendTime);
            if (nowIndia >= nextRun)
                nextRun = nextRun.AddDays(1);

            var delay = nextRun - nowIndia;

            _logger.LogInformation(
                "PharmaFlow notification scheduler next run: {NextRunIndia:yyyy-MM-dd HH:mm:ss} IST.",
                nextRun);

            await Task.Delay(delay, stoppingToken);

            var runDate = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, india));

            await SendDailyNotificationsAsync(runDate, stoppingToken);

            // On the first day of the month, also summarize the completed previous month.
            if (runDate.Day == 1)
            {
                var previousMonth = runDate.AddMonths(-1);
                await SendMonthlySavingsNotificationsAsync(previousMonth, stoppingToken);
            }
        }
    }

    private async Task<List<long>> GetActiveProfileIdsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        return await db.Profiles
            .AsNoTracking()
            .Where(p => p.ActiveStatus == 1)
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
    }

    private async Task SendDailyNotificationsAsync(
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        var profileIds = await GetActiveProfileIdsAsync(cancellationToken);

        await Parallel.ForEachAsync(
            profileIds,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxParallelProfiles
            },
            async (profileId, token) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider
                    .GetRequiredService<IPharmaFlowPushNotificationService>();

                await notifications.SendDailyStockNotificationAsync(
                    profileId,
                    localDate,
                    token);
            });
    }

    private async Task SendMonthlySavingsNotificationsAsync(
        DateOnly monthStart,
        CancellationToken cancellationToken)
    {
        var profileIds = await GetActiveProfileIdsAsync(cancellationToken);

        await Parallel.ForEachAsync(
            profileIds,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxParallelProfiles
            },
            async (profileId, token) =>
            {
                using var scope = _scopeFactory.CreateScope();
                var notifications = scope.ServiceProvider
                    .GetRequiredService<IPharmaFlowPushNotificationService>();

                await notifications.SendMonthlySavingsNotificationAsync(
                    profileId,
                    monthStart,
                    token);
            });
    }
}
