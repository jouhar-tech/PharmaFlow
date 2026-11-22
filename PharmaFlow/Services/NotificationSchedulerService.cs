using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;

namespace PharmaFlow.Services;

public sealed class NotificationSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<NotificationSchedulerService> _logger;
    private static readonly TimeSpan DailySendTime = new(8, 30, 0);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    private const int MaxParallelProfiles = 10;
    private const int SavingsCycleDays = 30;

    public NotificationSchedulerService(
        IServiceScopeFactory scopeFactory,
        ILogger<NotificationSchedulerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var india = NotificationTimeZone.GetIndiaStandardTimeZone();

        _logger.LogInformation(
            "PharmaHex notification scheduler started. Daily notifications are due at 8:30 AM IST.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var nowIndia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, india);

            // Run immediately after startup when today's 8:30 AM schedule has passed.
            // This catches up the current day after an idle app pool or server wake-up.
            if (nowIndia.TimeOfDay >= DailySendTime)
            {
                var runDate = DateOnly.FromDateTime(nowIndia);

                try
                {
                    var profiles = await GetActiveNotificationProfilesAsync(stoppingToken);

                    _logger.LogInformation(
                        "Processing daily notifications for {RunDate}; {ProfileCount} active profiles found.",
                        runDate,
                        profiles.Count);

                    // The dispatch log prevents duplicate sends for profiles that already
                    // received today's notification. Failed sends are released for retry.
                    await SendDailyNotificationsAsync(profiles, runDate, stoppingToken);
                    await SendDueSavingsNotificationsAsync(profiles, runDate, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Notification scheduler cycle failed for {RunDate}. It will retry.",
                        runDate);
                }
            }

            nowIndia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, india);

            var nextScheduledTime = nowIndia.Date.Add(DailySendTime);
            var delay = nowIndia < nextScheduledTime
                ? nextScheduledTime - nowIndia
                : RetryInterval;

            // Before 8:30 AM, sleep until the scheduled time. After 8:30 AM,
            // poll every five minutes so transient failures are retried the same day.
            _logger.LogDebug(
                "Notification scheduler next check in {Delay}. Current India time: {CurrentTime:yyyy-MM-dd HH:mm:ss}.",
                delay,
                nowIndia);

            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task<List<(long ProfileId, DateTime CycleStartUtc)>> GetActiveNotificationProfilesAsync(
        CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await db.Profiles
            .AsNoTracking()
            .Where(p =>
                p.ActiveStatus == 1 &&
                p.NotificationCycleStartAt != null)
            .Select(p => new
            {
                p.Id,
                p.NotificationCycleStartAt
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(p => (p.Id, p.NotificationCycleStartAt!.Value))
            .ToList();
    }

    private async Task SendDailyNotificationsAsync(
        IReadOnlyList<(long ProfileId, DateTime CycleStartUtc)> profiles,
        DateOnly localDate,
        CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(
            profiles.Select(p => p.ProfileId),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxParallelProfiles
            },
            async (profileId, token) =>
            {
                using var scope = _scopeFactory.CreateScope();
                try
                {
                    var notifications = scope.ServiceProvider
                        .GetRequiredService<IPharmaFlowPushNotificationService>();

                    await notifications.SendDailyStockNotificationAsync(
                        profileId,
                        localDate,
                        token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Daily stock notification failed for profile {ProfileId}. It will be retried.",
                        profileId);
                }
            });
    }

    private async Task SendDueSavingsNotificationsAsync(
        IReadOnlyList<(long ProfileId, DateTime CycleStartUtc)> profiles,
        DateOnly runDate,
        CancellationToken cancellationToken)
    {
        await Parallel.ForEachAsync(
            profiles,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MaxParallelProfiles
            },
            async (profile, token) =>
            {
                try
                {
                    var india = NotificationTimeZone.GetIndiaStandardTimeZone();
                    var anchorIndia = TimeZoneInfo.ConvertTimeFromUtc(profile.CycleStartUtc, india);
                    var elapsedDays = runDate.DayNumber - DateOnly.FromDateTime(anchorIndia).DayNumber;

                    if (elapsedDays < SavingsCycleDays)
                        return;

                    var completedCycles = elapsedDays / SavingsCycleDays;

                    // Send at most one overdue cycle per scheduler check. If the server
                    // was asleep, the next five-minute check can catch up the next cycle.
                    var cycleNumber = await GetFirstUnsentCycleAsync(
                        profile.ProfileId,
                        completedCycles,
                        token);

                    if (cycleNumber <= 0)
                        return;

                    var cycleStartDate = DateOnly.FromDateTime(anchorIndia).AddDays(
                        SavingsCycleDays * (cycleNumber - 1));
                    var cycleEndDate = cycleStartDate.AddDays(SavingsCycleDays);

                    var cycleStartUtc = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(
                            cycleStartDate.ToDateTime(TimeOnly.MinValue),
                            DateTimeKind.Unspecified),
                        india);

                    var cycleEndUtc = TimeZoneInfo.ConvertTimeToUtc(
                        DateTime.SpecifyKind(
                            cycleEndDate.ToDateTime(TimeOnly.MinValue),
                            DateTimeKind.Unspecified),
                        india);

                    using var scope = _scopeFactory.CreateScope();
                    var notifications = scope.ServiceProvider
                        .GetRequiredService<IPharmaFlowPushNotificationService>();

                    await notifications.Send30DaySavingsNotificationAsync(
                        profile.ProfileId,
                        cycleStartUtc,
                        cycleEndUtc,
                        cycleNumber,
                        token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "30-day savings notification failed for profile {ProfileId}.",
                        profile.ProfileId);
                }
            });
    }

    private async Task<int> GetFirstUnsentCycleAsync(
        long profileId,
        int completedCycles,
        CancellationToken cancellationToken)
    {
        if (completedCycles <= 0)
            return 0;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sentKeys = await db.NotificationDispatchLogs
            .AsNoTracking()
            .Where(n =>
                n.ProfileId == profileId &&
                n.NotificationType == "monthly-savings" &&
                n.Status == "sent" &&
                n.PeriodKey.StartsWith("30day-"))
            .Select(n => n.PeriodKey)
            .ToListAsync(cancellationToken);

        var sentCycles = sentKeys
            .Select(key => int.TryParse(key["30day-".Length..], out var cycle) ? cycle : 0)
            .Where(cycle => cycle > 0)
            .ToHashSet();

        for (var cycle = 1; cycle <= completedCycles; cycle++)
        {
            if (!sentCycles.Contains(cycle))
                return cycle;
        }

        return 0;
    }
}
