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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Generate(CreateBillViewModel model)
    {
        var paymentMethod = NormalizePaymentMethod(model.PaymentMethod);

        var customerName = string.IsNullOrWhiteSpace(model.CustomerName)
            ? null
            : model.CustomerName.Trim();

        var phoneNumber = string.IsNullOrWhiteSpace(model.PhoneNumber)
            ? null
            : model.PhoneNumber.Trim();

        if (customerName?.Length > 120)
            customerName = customerName[..120];

        if (phoneNumber?.Length > 15)
            phoneNumber = phoneNumber[..15];

        var result = new GeneratedBillViewModel
        {
            InvoiceNumber = $"PF-{DateTime.Now:yyMMddHHmmss}",
            InvoiceDate = DateTime.Now,
            CustomerName = customerName,
            PhoneNumber = phoneNumber,
            TotalAmount = model.Items?.Sum(item => item.Amount) ?? 0m,
            PaymentMethod = paymentMethod
        };

        return View("Generated", result);
    }

    private static string NormalizePaymentMethod(string? paymentMethod) =>
        paymentMethod?.Trim().ToUpperInvariant() switch
        {
            "UPI" => "UPI",
            "UDHAAR" => "Udhaar",
            _ => "Cash"
        };
}
