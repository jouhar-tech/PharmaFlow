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

        return View(new ReportsViewModel());
    }
}
