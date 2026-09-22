using Microsoft.AspNetCore.Mvc;
using PharmaFlow.Models.ViewModels;

namespace PharmaFlow.Controllers
{
    public class AccountController : Controller
    {
        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // OTP integration will be added next.
            return View(model);
        }
    }
}