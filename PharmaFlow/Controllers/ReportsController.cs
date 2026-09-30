using Microsoft.AspNetCore.Mvc;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class ReportsController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        if (!long.TryParse(HttpContext.Session.GetString("ProfileId"), out _))
            return RedirectToAction("Login", "Account");

        if (string.Equals(HttpContext.Session.GetString("UserRole"), "Staff", StringComparison.OrdinalIgnoreCase))
            return RedirectToAction("Index", "Home");

        return View(new ReportsViewModel());
    }
}
