using Microsoft.AspNetCore.Mvc;
using PharmaFlow.Filters;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class BillingController : Controller
{
    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateBillViewModel());
    }
}
