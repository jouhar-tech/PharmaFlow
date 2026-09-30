using Microsoft.AspNetCore.Mvc;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class ReportsController : Controller
{
    [HttpGet]
    public IActionResult Index(string period = "7 days", DateTime? startDate = null, DateTime? endDate = null)
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out _))
            return RedirectToAction("Login", "Account");

        if (string.Equals(HttpContext.Session.GetString("UserRole"), "Staff", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Home");

        var allowedPeriods = new[] { "Today", "7 days", "30 days", "All time", "Custom" };
        if (!allowedPeriods.Contains(period, StringComparer.OrdinalIgnoreCase))
            period = "7 days";

        if (string.Equals(period, "Custom", StringComparison.OrdinalIgnoreCase) &&
            (startDate is null || endDate is null || startDate > endDate))
            period = "7 days";

        return View(new ReportsViewModel
        {
            SelectedPeriod = period,
            StartDate = startDate?.Date,
            EndDate = endDate?.Date
        });
    }
}
