using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public sealed class AdminLoginViewModel
{
    [Required, StringLength(100)]
    public string Username { get; set; } = string.Empty;

    [Required, StringLength(200)]
    public string Password { get; set; } = string.Empty;
}

public sealed class AdminDashboardViewModel
{
    public string StatusFilter { get; init; } = "all";
    public string PlanFilter { get; init; } = "all";
    public string Search { get; init; } = string.Empty;
    public int CurrentPage { get; init; } = 1;
    public int PageSize { get; init; } = 25;
    public int TotalPages { get; init; }

    public int TotalUsers { get; init; }
    public int ActiveUsers { get; init; }
    public int InactiveUsers { get; init; }
    public int UnlimitedUsers { get; init; }
    public int FreeUsers { get; init; }
    public int ExpiringSoonUsers { get; init; }

    public IReadOnlyList<AdminUserRowViewModel> Users { get; init; } = [];
    public IReadOnlyList<AdminRecentUserViewModel> RecentUsers { get; init; } = [];
    public IReadOnlyList<AdminExpiringSubscriptionViewModel> ExpiringSubscriptions { get; init; } = [];
}

public sealed class AdminUserRowViewModel
{
    public long ProfileId { get; init; }
    public Guid UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public DateTime? LoginDate { get; init; }
    public string ShopName { get; init; } = string.Empty;
    public string? Address { get; init; }
    public bool IsActive { get; init; }
    public bool IsUnlimited { get; init; }
    public DateTime? SubscriptionStartsAt { get; init; }
    public DateTime? SubscriptionEndsAt { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed class AdminRecentUserViewModel
{
    public long ProfileId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string ShopName { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed class AdminExpiringSubscriptionViewModel
{
    public long ProfileId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string ShopName { get; init; } = string.Empty;
    public DateTime SubscriptionEndsAt { get; init; }
}

public sealed class AdminUserEditViewModel
{
    public long ProfileId { get; init; }
    public Guid UserId { get; init; }

    [Required, StringLength(50)]
    public string Username { get; set; } = string.Empty;

    [StringLength(120)]
    public string? Name { get; set; }

    [EmailAddress, StringLength(160)]
    public string? Email { get; set; }

    [Phone, StringLength(20)]
    public string? Phone { get; set; }

    [StringLength(120)]
    public string? ShopName { get; set; }

    [StringLength(500)]
    public string? Address { get; set; }

    public bool IsActive { get; set; }

    [Required]
    public string SubscriptionPlan { get; set; } = "free";

    public DateTime? SubscriptionStartsAt { get; set; }
    public DateTime? SubscriptionEndsAt { get; set; }

    public DateTime? LastLoginAt { get; init; }
    public DateTime? LastLogoutAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
