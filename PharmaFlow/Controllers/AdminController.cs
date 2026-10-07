using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

public sealed class AdminController : Controller
{
    // Replace these two values before production deployment.
    // Do NOT reuse a real user password here; move them to environment/configuration
    // and add a rate-limiter before exposing the admin endpoint publicly.
    private const string AdminUsername = "XYZ";
    private const string AdminPassword = "XXXXXX";

    private const string AdminSessionKey = "PharmaFlowAdminAuthenticated";
    private const int PageSize = 25;

    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        ApplicationDbContext dbContext,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login()
    {
        if (IsAdminAuthenticated())
            return RedirectToAction(nameof(Index));

        return View(new AdminLoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Login(AdminLoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var username = model.Username.Trim();

        if (!FixedTimeEquals(username, AdminUsername) ||
            !FixedTimeEquals(model.Password, AdminPassword))
        {
            ModelState.AddModelError(string.Empty, "Invalid admin login details.");
            return View(model);
        }

        HttpContext.Session.SetString(AdminSessionKey, "1");
        HttpContext.Session.SetInt32("AdminAuthenticatedAt", (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Remove(AdminSessionKey);
        HttpContext.Session.Remove("AdminAuthenticatedAt");
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? status,
        string? plan,
        string? search,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdminAuthenticated())
            return RedirectToAction(nameof(Login));

        var normalizedStatus = NormalizeFilter(status);
        var normalizedPlan = NormalizePlanFilter(plan);
        var normalizedSearch = NormalizeSearch(search);
        var currentPage = Math.Max(page, 1);

        var now = DateTime.UtcNow;
        var soonCutoff = now.AddDays(30);

        var baseQuery = _dbContext.Profiles.AsNoTracking();

        var totalUsers = await baseQuery.CountAsync(cancellationToken);
        var activeUsers = await baseQuery.CountAsync(p => p.ActiveStatus == 1, cancellationToken);
        var inactiveUsers = totalUsers - activeUsers;

        var unlimitedUsers = await baseQuery.CountAsync(
            p => p.SubscriptionPlan == "unlimited" &&
                 (!p.SubscriptionEndsAt.HasValue || p.SubscriptionEndsAt >= now),
            cancellationToken);

        var freeUsers = totalUsers - unlimitedUsers;

        var expiringSoonUsers = await baseQuery.CountAsync(
            p => p.SubscriptionPlan == "unlimited" &&
                 p.SubscriptionEndsAt.HasValue &&
                 p.SubscriptionEndsAt >= now &&
                 p.SubscriptionEndsAt <= soonCutoff,
            cancellationToken);

        if (normalizedStatus == "active")
            baseQuery = baseQuery.Where(p => p.ActiveStatus == 1);
        else if (normalizedStatus == "inactive")
            baseQuery = baseQuery.Where(p => p.ActiveStatus != 1);

        if (normalizedPlan == "unlimited")
        {
            baseQuery = baseQuery.Where(p =>
                p.SubscriptionPlan == "unlimited" &&
                (!p.SubscriptionEndsAt.HasValue || p.SubscriptionEndsAt >= now));
        }
        else if (normalizedPlan == "free")
        {
            baseQuery = baseQuery.Where(p =>
                p.SubscriptionPlan != "unlimited" ||
                (p.SubscriptionEndsAt.HasValue && p.SubscriptionEndsAt < now));
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var term = normalizedSearch.ToLowerInvariant();

            baseQuery = baseQuery.Where(p =>
                p.Username.ToLower().Contains(term) ||
                (p.Email != null && p.Email.ToLower().Contains(term)) ||
                (p.FullName != null && p.FullName.ToLower().Contains(term)) ||
                (p.PhoneNumber != null && p.PhoneNumber.ToLower().Contains(term)) ||
                (p.BusinessName != null && p.BusinessName.ToLower().Contains(term)) ||
                (p.Address != null && p.Address.ToLower().Contains(term)) ||
                p.Id.ToString().Contains(term));
        }

        var filteredCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(filteredCount / (double)PageSize));
        currentPage = Math.Min(currentPage, totalPages);

        var users = await baseQuery
            .OrderByDescending(p => p.CreatedAt)
            .Skip((currentPage - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new AdminUserRowViewModel
            {
                ProfileId = p.Id,
                UserId = p.UserId,
                Username = p.Username,
                Name = string.IsNullOrWhiteSpace(p.FullName) ? p.Username : p.FullName!,
                Phone = p.PhoneNumber,
                LoginDate = p.LastLoginAt,
                ShopName = p.BusinessName ?? string.Empty,
                Address = p.Address,
                IsActive = p.ActiveStatus == 1,
                IsUnlimited = p.SubscriptionPlan == "unlimited" &&
                              (!p.SubscriptionEndsAt.HasValue || p.SubscriptionEndsAt >= now),
                SubscriptionStartsAt = p.SubscriptionStartsAt,
                SubscriptionEndsAt = p.SubscriptionEndsAt,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var recentUsers = await _dbContext.Profiles
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Take(5)
            .Select(p => new AdminRecentUserViewModel
            {
                ProfileId = p.Id,
                Username = p.Username,
                ShopName = p.BusinessName ?? "Shop not set",
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var expiringSubscriptions = await _dbContext.Profiles
            .AsNoTracking()
            .Where(p =>
                p.SubscriptionPlan == "unlimited" &&
                p.SubscriptionEndsAt.HasValue &&
                p.SubscriptionEndsAt >= now &&
                p.SubscriptionEndsAt <= soonCutoff)
            .OrderBy(p => p.SubscriptionEndsAt)
            .Take(8)
            .Select(p => new AdminExpiringSubscriptionViewModel
            {
                ProfileId = p.Id,
                Username = p.Username,
                ShopName = p.BusinessName ?? "Shop not set",
                SubscriptionEndsAt = p.SubscriptionEndsAt!.Value
            })
            .ToListAsync(cancellationToken);

        ViewData["Title"] = "Admin Dashboard";

        return View(new AdminDashboardViewModel
        {
            StatusFilter = normalizedStatus,
            PlanFilter = normalizedPlan,
            Search = normalizedSearch,
            CurrentPage = currentPage,
            PageSize = PageSize,
            TotalPages = totalPages,
            TotalUsers = totalUsers,
            ActiveUsers = activeUsers,
            InactiveUsers = inactiveUsers,
            UnlimitedUsers = unlimitedUsers,
            FreeUsers = freeUsers,
            ExpiringSoonUsers = expiringSoonUsers,
            Users = users,
            RecentUsers = recentUsers,
            ExpiringSubscriptions = expiringSubscriptions
        });
    }

    [HttpGet]
    public async Task<IActionResult> Unlimited(CancellationToken cancellationToken)
    {
        if (!IsAdminAuthenticated())
            return RedirectToAction(nameof(Login));

        var now = DateTime.UtcNow;

        var users = await _dbContext.Profiles
            .AsNoTracking()
            .Where(p =>
                p.SubscriptionPlan == "unlimited" &&
                (!p.SubscriptionEndsAt.HasValue || p.SubscriptionEndsAt >= now))
            .OrderBy(p => p.SubscriptionEndsAt.HasValue)
            .ThenBy(p => p.SubscriptionEndsAt)
            .ThenBy(p => p.Username)
            .Select(p => new AdminUserRowViewModel
            {
                ProfileId = p.Id,
                UserId = p.UserId,
                Username = p.Username,
                Name = string.IsNullOrWhiteSpace(p.FullName) ? p.Username : p.FullName!,
                Phone = p.PhoneNumber,
                LoginDate = p.LastLoginAt,
                ShopName = p.BusinessName ?? string.Empty,
                Address = p.Address,
                IsActive = p.ActiveStatus == 1,
                IsUnlimited = true,
                SubscriptionStartsAt = p.SubscriptionStartsAt,
                SubscriptionEndsAt = p.SubscriptionEndsAt,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync(cancellationToken);

        ViewData["Title"] = "Unlimited Plan Users";
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        if (!IsAdminAuthenticated())
            return RedirectToAction(nameof(Login));

        if (id <= 0)
            return NotFound();

        var profile = await _dbContext.Profiles
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile is null)
            return NotFound();

        return View(new AdminUserEditViewModel
        {
            ProfileId = profile.Id,
            UserId = profile.UserId,
            Username = profile.Username,
            Name = profile.FullName,
            Email = profile.Email,
            Phone = profile.PhoneNumber,
            ShopName = profile.BusinessName,
            Address = profile.Address,
            IsActive = profile.ActiveStatus == 1,
            SubscriptionPlan = NormalizeSubscriptionPlan(profile.SubscriptionPlan),
            SubscriptionStartsAt = profile.SubscriptionStartsAt,
            SubscriptionEndsAt = profile.SubscriptionEndsAt,
            LastLoginAt = profile.LastLoginAt,
            LastLogoutAt = profile.LastLogoutAt,
            CreatedAt = profile.CreatedAt
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        long id,
        AdminUserEditViewModel model,
        CancellationToken cancellationToken)
    {
        if (!IsAdminAuthenticated())
            return RedirectToAction(nameof(Login));

        if (id <= 0 || id != model.ProfileId)
            return BadRequest();

        var profile = await _dbContext.Profiles
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile is null)
            return NotFound();

        model.Username = NormalizeText(model.Username, 50) ?? string.Empty;
        model.Name = NormalizeText(model.Name, 120);
        model.Phone = NormalizeText(model.Phone, 20);
        model.ShopName = NormalizeText(model.ShopName, 120);
        model.Address = NormalizeText(model.Address, 500);
        model.SubscriptionPlan = NormalizeSubscriptionPlan(model.SubscriptionPlan);

        if (string.IsNullOrWhiteSpace(model.Username))
            ModelState.AddModelError(nameof(model.Username), "Username is required.");

        var usernameExists = await _dbContext.Profiles
            .AsNoTracking()
            .AnyAsync(p => p.Id != id && p.Username == model.Username, cancellationToken);

        if (usernameExists)
            ModelState.AddModelError(nameof(model.Username), "That username is already in use.");

        if (model.SubscriptionPlan == "unlimited")
        {
            if (model.SubscriptionStartsAt.HasValue &&
                model.SubscriptionEndsAt.HasValue &&
                model.SubscriptionStartsAt.Value > model.SubscriptionEndsAt.Value)
            {
                ModelState.AddModelError(
                    nameof(model.SubscriptionEndsAt),
                    "Subscription end date cannot be before the start date.");
            }
        }
        else
        {
            model.SubscriptionStartsAt = null;
            model.SubscriptionEndsAt = null;
        }

        if (!ModelState.IsValid)
            return View(model);

        profile.Username = model.Username;
        profile.FullName = model.Name;
        profile.PhoneNumber = model.Phone;
        profile.BusinessName = model.ShopName;
        profile.Address = model.Address;
        profile.ActiveStatus = model.IsActive ? (short)1 : (short)0;

        if (model.SubscriptionPlan == "unlimited")
        {
            profile.SubscriptionPlan = "unlimited";
            profile.SubscriptionStartsAt = model.SubscriptionStartsAt ?? DateTime.UtcNow;
            profile.SubscriptionEndsAt = model.SubscriptionEndsAt;
        }
        else
        {
            profile.SubscriptionPlan = "free";
            profile.SubscriptionStartsAt = null;
            profile.SubscriptionEndsAt = null;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update admin user profile {ProfileId}.", id);
            ModelState.AddModelError(
                string.Empty,
                "User details could not be saved. Please try again.");
            return View(model);
        }

        TempData["AdminMessage"] = "User details updated successfully.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveUnlimited(
        long id,
        CancellationToken cancellationToken)
    {
        if (!IsAdminAuthenticated())
            return RedirectToAction(nameof(Login));

        if (id <= 0)
            return NotFound();

        var profile = await _dbContext.Profiles
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (profile is null)
            return NotFound();

        profile.SubscriptionPlan = "free";
        profile.SubscriptionStartsAt = null;
        profile.SubscriptionEndsAt = null;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove unlimited plan for profile {ProfileId}.", id);
            TempData["AdminMessage"] = "The Unlimited Plan could not be removed. Please try again.";
            return RedirectToAction(nameof(Unlimited));
        }

        TempData["AdminMessage"] = "Unlimited Plan removed. User moved to Free Plan.";
        return RedirectToAction(nameof(Unlimited));
    }

    private bool IsAdminAuthenticated() =>
        HttpContext.Session.GetString(AdminSessionKey) == "1";

    private static string NormalizeFilter(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "inactive" => "inactive",
            _ => "all"
        };

    private static string NormalizePlanFilter(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "unlimited" => "unlimited",
            "free" => "free",
            _ => "all"
        };

    private static string NormalizeSubscriptionPlan(string? value) =>
        string.Equals(value?.Trim(), "unlimited", StringComparison.OrdinalIgnoreCase)
            ? "unlimited"
            : "free";

    private static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim().Length <= 100
            ? value.Trim()
            : value.Trim()[..100];
    }

    private static string? NormalizeText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = string.Join(
            " ",
            value.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].Trim();
    }

    private static bool FixedTimeEquals(string? left, string right)
    {
        if (left is null)
            return false;

        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
