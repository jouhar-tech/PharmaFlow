using Microsoft.AspNetCore.Mvc;

namespace PharmaFlow.Controllers;

/// <summary>
/// Public-facing legal information pages. These draft pages do not require authentication.
/// Legal copy must be reviewed and the marked proprietor, address, processor and retention details
/// completed before deploying them to production.
/// </summary>
public sealed class LegalController : Controller
{
    [HttpGet]
    public IActionResult Terms()
    {
        return View();
    }

    [HttpGet]
    public IActionResult Privacy()
    {
        return View();
    }
}
