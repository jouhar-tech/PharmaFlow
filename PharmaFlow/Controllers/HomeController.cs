using Microsoft.AspNetCore.Mvc;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using System.Diagnostics;

namespace PharmaFlow.Controllers
{
    public class HomeController : Controller
    {
        [SessionAuthorize]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Index()
        {
            var dashboard = new DashboardViewModel();

            return View(dashboard);
        }

        [HttpGet]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Ping()
        {
            return long.TryParse(HttpContext.Session.GetString("ProfileId"), out _)
                ? Ok()
                : Unauthorized();
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
