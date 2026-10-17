namespace PharmaFlow.Models.ViewModels;

public sealed class NotificationCenterViewModel
{
    public DateTimeOffset GeneratedAt { get; init; }

    public IReadOnlyList<NotificationCenterItemViewModel> Alerts { get; init; } = [];

    public int CriticalCount => Alerts.Count(alert =>
        string.Equals(alert.Severity, "critical", StringComparison.Ordinal));

    public int WarningCount => Alerts.Count(alert =>
        string.Equals(alert.Severity, "warning", StringComparison.Ordinal));

    public bool HasAlerts => Alerts.Count > 0;

    public int AffectedCount => Alerts.Sum(alert => Math.Max(0, alert.AffectedCount));
}

public sealed class NotificationCenterItemViewModel
{
    public string Category { get; init; } = "stock";

    public string CategoryLabel { get; init; } = "Stock";

    public string Severity { get; init; } = "info";

    public string SeverityLabel { get; init; } = "Info";

    public string Icon { get; init; } = "•";

    public string Title { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public int AffectedCount { get; init; } = 1;

    public string ActionLabel { get; init; } = "View";

    public string ActionUrl { get; init; } = "/";
}
