using System.Data;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PharmaFlow.Data;
using PharmaFlow.Filters;
using PharmaFlow.Models;
using PharmaFlow.Models.ViewModels;
using PharmaFlow.Services;

namespace PharmaFlow.Controllers;

[SessionAuthorize]
public sealed class CustomersController : Controller
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IGlobalProductCatalogService _globalProductCatalogService;
    private readonly ILogger<CustomersController> _logger;

    public CustomersController(
        ApplicationDbContext dbContext,
        IGlobalProductCatalogService globalProductCatalogService,
        ILogger<CustomersController> logger)
    {
        _dbContext = dbContext;
        _globalProductCatalogService = globalProductCatalogService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? q,
        string? filter,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var searchTerm = q?.Trim() ?? string.Empty;
        if (searchTerm.Length > 80)
            searchTerm = searchTerm[..80];

        var normalizedFilter = NormalizeFilter(filter);

        var balanceRows = await _dbContext.CustomerLedgerEntries
            .AsNoTracking()
            .Where(entry => entry.ProfileId == profileId)
            .GroupBy(entry => entry.CustomerId)
            .Select(group => new CustomerBalanceRow
            {
                CustomerId = group.Key,
                Balance = group.Sum(entry => entry.BalanceChange),
                LastActivityAt = group.Max(entry => entry.CreatedAt)
            })
            .ToListAsync(cancellationToken);

        var balanceByCustomer = balanceRows.ToDictionary(row => row.CustomerId, row => row);
        var totalOwedToYou = balanceRows
            .Where(row => row.Balance > 0)
            .Sum(row => row.Balance);
        var totalOwedByYou = balanceRows
            .Where(row => row.Balance < 0)
            .Sum(row => Math.Abs(row.Balance));

        var customerQuery = _dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.ProfileId == profileId);

        var reminderCustomerOptions = await _dbContext.Customers
            .AsNoTracking()
            .Where(customer => customer.ProfileId == profileId)
            .OrderBy(customer => customer.FullName)
            .ThenBy(customer => customer.CustomerId)
            .Select(customer => new CustomerReminderCustomerOptionViewModel
            {
                CustomerId = customer.CustomerId,
                FullName = string.IsNullOrWhiteSpace(customer.FullName)
                    ? "Unnamed Customer"
                    : customer.FullName.Trim(),
                PhoneNumber = customer.PhoneNumber
            })
            .ToListAsync(cancellationToken);

        var pendingReminderTotal = await _dbContext.CustomerReminders
            .AsNoTracking()
            .CountAsync(reminder =>
                reminder.ProfileId == profileId &&
                (reminder.Status == "Pending" || reminder.Status == "Ordered"),
                cancellationToken);

        HashSet<long>? searchIds = null;

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var pattern = $"%{searchTerm.Replace("%", "\\%").Replace("_", "\\_")}%";

            var textIds = await customerQuery
                .Where(customer =>
                    (customer.FullName != null && EF.Functions.ILike(customer.FullName, pattern, "\\"))
                    || (customer.PhoneNumber != null && EF.Functions.ILike(customer.PhoneNumber, pattern, "\\")))
                .Select(customer => customer.CustomerId)
                .ToListAsync(cancellationToken);

            var amountIds = new List<long>();
            if (TryParseMoney(searchTerm, out var searchedAmount))
            {
                amountIds = balanceRows
                    .Where(row => Math.Abs(row.Balance) == searchedAmount)
                    .Select(row => row.CustomerId)
                    .ToList();
            }

            searchIds = textIds
                .Concat(amountIds)
                .Distinct()
                .ToHashSet();
        }

        HashSet<long>? filterIds = null;
        if (normalizedFilter != "all")
        {
            if (normalizedFilter == "settled")
            {
                var allCustomerIds = await customerQuery
                    .Select(customer => customer.CustomerId)
                    .ToListAsync(cancellationToken);

                filterIds = allCustomerIds
                    .Where(customerId =>
                        !balanceByCustomer.TryGetValue(customerId, out var row) ||
                        row.Balance == 0)
                    .ToHashSet();
            }
            else
            {
                filterIds = balanceRows
                    .Where(row => normalizedFilter switch
                    {
                        "owed-to-you" => row.Balance > 0,
                        "owed-by-you" => row.Balance < 0,
                        _ => false
                    })
                    .Select(row => row.CustomerId)
                    .ToHashSet();
            }
        }

        if (searchIds is not null && filterIds is not null)
        {
            searchIds.IntersectWith(filterIds);
            customerQuery = searchIds.Count == 0
                ? customerQuery.Where(_ => false)
                : customerQuery.Where(customer => searchIds.Contains(customer.CustomerId));
        }
        else if (searchIds is not null)
        {
            customerQuery = searchIds.Count == 0
                ? customerQuery.Where(_ => false)
                : customerQuery.Where(customer => searchIds.Contains(customer.CustomerId));
        }
        else if (filterIds is not null)
        {
            customerQuery = filterIds.Count == 0
                ? customerQuery.Where(_ => false)
                : customerQuery.Where(customer => filterIds.Contains(customer.CustomerId));
        }

        var customers = await customerQuery
            .OrderBy(customer => customer.FullName)
            .ThenBy(customer => customer.CustomerId)
            .ToListAsync(cancellationToken);

        var customerIds = customers.Select(customer => customer.CustomerId).ToList();

        var billCounts = customerIds.Count == 0
            ? new Dictionary<long, int>()
            : await _dbContext.SalesBills
                .AsNoTracking()
                .Where(bill =>
                    bill.ProfileId == profileId &&
                    bill.CustomerId.HasValue &&
                    customerIds.Contains(bill.CustomerId.Value))
                .GroupBy(bill => bill.CustomerId!.Value)
                .Select(group => new { CustomerId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.CustomerId, row => row.Count, cancellationToken);

        var pendingReminderCounts = customerIds.Count == 0
            ? new Dictionary<long, int>()
            : await _dbContext.CustomerReminders
                .AsNoTracking()
                .Where(reminder =>
                    reminder.ProfileId == profileId &&
                    customerIds.Contains(reminder.CustomerId) &&
                    (reminder.Status == "Pending" || reminder.Status == "Ordered"))
                .GroupBy(reminder => reminder.CustomerId)
                .Select(group => new { CustomerId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.CustomerId, row => row.Count, cancellationToken);

        var items = customers
            .Select(customer =>
            {
                balanceByCustomer.TryGetValue(customer.CustomerId, out var balanceRow);

                return new CustomerListItemViewModel
                {
                    CustomerId = customer.CustomerId,
                    FullName = string.IsNullOrWhiteSpace(customer.FullName)
                        ? "Unnamed Customer"
                        : customer.FullName.Trim(),
                    PhoneNumber = customer.PhoneNumber,
                    CurrentBalance = balanceRow?.Balance ?? 0m,
                    LastActivityAt = balanceRow?.LastActivityAt ?? customer.UpdatedAt,
                    BillCount = billCounts.GetValueOrDefault(customer.CustomerId),
                    PendingReminderCount = pendingReminderCounts.GetValueOrDefault(customer.CustomerId)
                };
            })
            .ToList();

        return View(new CustomerManagementViewModel
        {
            SearchTerm = searchTerm,
            Filter = normalizedFilter,
            TotalOwedToYou = totalOwedToYou,
            TotalOwedByYou = totalOwedByYou,
            Customers = items,
            PendingReminderTotal = pendingReminderTotal,
            ReminderCustomers = reminderCustomerOptions
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CustomerCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        CustomerCreateViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var fullName = model.FullName?.Trim();
        var phoneNumber = NormalizePhoneNumber(model.PhoneNumber);
        var balanceType = NormalizeBalanceType(model.BalanceType);

        if (string.IsNullOrWhiteSpace(fullName))
            ModelState.AddModelError(nameof(model.FullName), "Enter the customer's name.");

        if (!string.IsNullOrWhiteSpace(model.PhoneNumber) && phoneNumber is null)
            ModelState.AddModelError(nameof(model.PhoneNumber), "Enter a valid 10-digit Indian mobile number.");

        if (model.OpeningBalance < 0)
            ModelState.AddModelError(nameof(model.OpeningBalance), "Opening balance cannot be negative.");

        if (model.OpeningBalance > 1_000_000_000m)
            ModelState.AddModelError(nameof(model.OpeningBalance), "Opening balance is too large.");

        var roundedOpeningBalance = decimal.Round(
            model.OpeningBalance,
            2,
            MidpointRounding.AwayFromZero);

        if (model.OpeningBalance > 0m && roundedOpeningBalance <= 0m)
            ModelState.AddModelError(
                nameof(model.OpeningBalance),
                "Opening balance must be at least ₹0.01.");

        if (!ModelState.IsValid)
            return View(model);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            if (phoneNumber is not null)
            {
                var duplicate = await _dbContext.Customers
                    .AsNoTracking()
                    .AnyAsync(customer =>
                        customer.ProfileId == profileId &&
                        customer.PhoneNumber == phoneNumber,
                        cancellationToken);

                if (duplicate)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    ModelState.AddModelError(
                        nameof(model.PhoneNumber),
                        "A customer with this phone number already exists. Open that customer instead of creating a duplicate.");
                    return View(model);
                }
            }

            var now = DateTime.UtcNow;
            var customer = new Customer
            {
                ProfileId = profileId,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                CreatedAt = now,
                UpdatedAt = now
            };

            _dbContext.Customers.Add(customer);
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (roundedOpeningBalance > 0m)
            {
                var balanceChange = balanceType == "OwedByYou"
                    ? -roundedOpeningBalance
                    : roundedOpeningBalance;

                _dbContext.CustomerLedgerEntries.Add(new CustomerLedgerEntry
                {
                    ProfileId = profileId,
                    CustomerId = customer.CustomerId,
                    EntryType = "OpeningBalance",
                    BalanceChange = balanceChange,
                    Description = string.IsNullOrWhiteSpace(model.Note)
                        ? "Opening customer balance"
                        : model.Note.Trim(),
                    CreatedAt = now
                });

                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);

            TempData["CustomerSuccess"] =
                roundedOpeningBalance > 0m
                    ? "Customer and opening balance saved successfully."
                    : "Customer saved successfully.";

            return RedirectToAction(nameof(Details), new { id = customer.CustomerId });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                ex,
                "Failed to create customer for profile {ProfileId}.",
                profileId);

            ModelState.AddModelError(
                string.Empty,
                "The customer could not be saved. No customer or balance changes were made.");
            return View(model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CustomerId == id && item.ProfileId == profileId,
                cancellationToken);

        if (customer is null)
            return NotFound();

        var entries = await _dbContext.CustomerLedgerEntries
            .AsNoTracking()
            .Where(entry =>
                entry.ProfileId == profileId &&
                entry.CustomerId == id)
            .OrderBy(entry => entry.CreatedAt)
            .ThenBy(entry => entry.LedgerEntryId)
            .ToListAsync(cancellationToken);

        var billIds = entries
            .Where(entry => entry.BillId.HasValue)
            .Select(entry => entry.BillId!.Value)
            .Distinct()
            .ToList();

        var billNumbers = billIds.Count == 0
            ? new Dictionary<long, string>()
            : await _dbContext.SalesBills
                .AsNoTracking()
                .Where(bill => bill.ProfileId == profileId && billIds.Contains(bill.BillId))
                .ToDictionaryAsync(bill => bill.BillId, bill => bill.BillNumber, cancellationToken);

        var runningBalance = 0m;
        var transactions = new List<CustomerTransactionViewModel>(entries.Count);

        foreach (var entry in entries)
        {
            runningBalance += entry.BalanceChange;

            transactions.Add(new CustomerTransactionViewModel
            {
                LedgerEntryId = entry.LedgerEntryId,
                EntryType = entry.EntryType,
                Description = entry.Description ?? string.Empty,
                BalanceChange = entry.BalanceChange,
                BalanceAfter = runningBalance,
                BillId = entry.BillId,
                BillNumber = entry.BillId.HasValue && billNumbers.TryGetValue(entry.BillId.Value, out var billNumber)
                    ? billNumber
                    : null,
                CreatedAt = entry.CreatedAt
            });
        }

        var billCount = await _dbContext.SalesBills
            .AsNoTracking()
            .CountAsync(
                bill => bill.ProfileId == profileId && bill.CustomerId == id,
                cancellationToken);

        var reminders = await _dbContext.CustomerReminders
            .AsNoTracking()
            .Where(reminder =>
                reminder.ProfileId == profileId &&
                reminder.CustomerId == id)
            .OrderBy(reminder => reminder.Status == "Completed" || reminder.Status == "Cancelled")
            .ThenBy(reminder => reminder.ReminderDate ?? DateOnly.MaxValue)
            .ThenByDescending(reminder => reminder.CreatedAt)
            .Select(reminder => new CustomerReminderViewModel
            {
                ReminderId = reminder.ReminderId,
                ProductName = reminder.ProductName,
                GenericName = reminder.GenericName,
                BrandName = reminder.BrandName,
                ProductSource = reminder.ProductSource,
                Status = reminder.Status,
                Note = reminder.Note,
                ReminderDate = reminder.ReminderDate,
                CreatedAt = reminder.CreatedAt,
                CompletedAt = reminder.CompletedAt
            })
            .ToListAsync(cancellationToken);

        return View(new CustomerDetailsViewModel
        {
            CustomerId = customer.CustomerId,
            FullName = string.IsNullOrWhiteSpace(customer.FullName)
                ? "Unnamed Customer"
                : customer.FullName.Trim(),
            PhoneNumber = customer.PhoneNumber,
            CreatedAt = customer.CreatedAt,
            CurrentBalance = runningBalance,
            BillCount = billCount,
            Transactions = transactions
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.LedgerEntryId)
                .ToList(),
            Reminders = reminders
        });
    }

    [HttpGet]
    public async Task<IActionResult> SearchProducts(
        long customerId,
        string? query,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var customerExists = await _dbContext.Customers
            .AsNoTracking()
            .AnyAsync(
                customer => customer.CustomerId == customerId && customer.ProfileId == profileId,
                cancellationToken);

        if (!customerExists)
            return NotFound();

        var normalizedQuery = NormalizeNullableText(query, 100) ?? string.Empty;
        if (normalizedQuery.Length < 2)
            return Ok(Array.Empty<CustomerReminderSearchItemViewModel>());

        var results = await _globalProductCatalogService.SearchAsync(
            normalizedQuery,
            cancellationToken);

        var catalogIds = results
            .Where(item => item.CatalogId.HasValue)
            .Select(item => item.CatalogId!.Value)
            .Distinct()
            .ToArray();

        var barcodes = results
            .Where(item => !string.IsNullOrWhiteSpace(item.Barcode))
            .Select(item => item.Barcode!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var productNames = results
            .Select(item => item.ProductName.Trim().ToLower())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .ToArray();

        var localProducts = await _dbContext.Products
            .AsNoTracking()
            .Where(product =>
                product.ProfileId == profileId &&
                (
                    (product.CatalogId.HasValue && catalogIds.Contains(product.CatalogId.Value)) ||
                    (product.Barcode != null && barcodes.Contains(product.Barcode)) ||
                    productNames.Contains(product.ProductName.ToLower())
                ))
            .Select(product => new
            {
                product.CatalogId,
                product.Barcode,
                product.ProductName
            })
            .ToListAsync(cancellationToken);

        var localCatalogIds = localProducts
            .Where(product => product.CatalogId.HasValue)
            .Select(product => product.CatalogId!.Value)
            .ToHashSet();

        var localBarcodes = localProducts
            .Where(product => !string.IsNullOrWhiteSpace(product.Barcode))
            .Select(product => product.Barcode!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var localNames = localProducts
            .Select(product => product.ProductName.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return Ok(results
            .Select(item => new CustomerReminderSearchItemViewModel
            {
                CatalogId = item.CatalogId,
                Source = item.Source,
                ExternalId = item.ExternalId,
                ProductType = item.ProductType,
                ProductName = item.ProductName,
                GenericName = item.GenericName,
                BrandName = item.BrandName,
                Manufacturer = item.Manufacturer,
                DosageForm = item.DosageForm,
                Strength = item.Strength,
                PackSize = item.PackSize,
                Barcode = item.Barcode,
                IsAlreadyInInventory =
                    (item.CatalogId.HasValue && localCatalogIds.Contains(item.CatalogId.Value)) ||
                    (!string.IsNullOrWhiteSpace(item.Barcode) && localBarcodes.Contains(item.Barcode.Trim())) ||
                    localNames.Contains(item.ProductName.Trim())
            })
            .Take(100)
            .ToList());
    }

    [HttpGet]
    public async Task<IActionResult> CreateReminder(
        long customerId,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CustomerId == customerId && item.ProfileId == profileId,
                cancellationToken);

        if (customer is null)
            return NotFound();

        ViewData["Title"] = "Add Reminder";
        return View(new CustomerReminderCreateViewModel
        {
            CustomerId = customer.CustomerId,
            CustomerName = string.IsNullOrWhiteSpace(customer.FullName)
                ? "Unnamed Customer"
                : customer.FullName.Trim()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateReminder(
        CustomerReminderCreateViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CustomerId == model.CustomerId && item.ProfileId == profileId,
                cancellationToken);

        if (customer is null)
            return NotFound();

        var productName = model.ProductName?.Trim();
        if (string.IsNullOrWhiteSpace(productName))
            ModelState.AddModelError(nameof(model.ProductName), "Select a product or enter a product name.");

        if (!string.IsNullOrWhiteSpace(model.ProductSource) ||
            !string.IsNullOrWhiteSpace(model.ProductExternalId) ||
            model.CatalogId.HasValue)
        {
            if (string.IsNullOrWhiteSpace(model.ProductSource) ||
                string.IsNullOrWhiteSpace(model.ProductExternalId))
            {
                ModelState.AddModelError(string.Empty, "The selected product reference is incomplete. Search and select the product again.");
            }
        }

        var today = DateOnly.FromDateTime(
            DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        if (model.ReminderDate.HasValue && model.ReminderDate.Value < today)
            ModelState.AddModelError(nameof(model.ReminderDate), "Reminder date cannot be earlier than today.");

        GlobalProductSearchResult? selectedProduct = null;
        if (ModelState.IsValid &&
            !string.IsNullOrWhiteSpace(model.ProductSource) &&
            !string.IsNullOrWhiteSpace(model.ProductExternalId))
        {
            selectedProduct = await _globalProductCatalogService.GetAsync(
                model.ProductSource.Trim(),
                model.ProductExternalId.Trim(),
                model.CatalogId,
                cancellationToken);

            if (selectedProduct is null)
            {
                ModelState.AddModelError(string.Empty, "The selected product is no longer available from the product database. Search again or enter the product name manually.");
            }
        }

        if (!ModelState.IsValid)
        {
            model.ProductName = productName ?? string.Empty;
            model.CustomerName = string.IsNullOrWhiteSpace(customer.FullName)
                ? "Unnamed Customer"
                : customer.FullName.Trim();
            return View(model);
        }

        var reminder = new CustomerReminder
        {
            ProfileId = profileId,
            CustomerId = customer.CustomerId,
            CatalogId = selectedProduct?.CatalogId,
            ProductSource = selectedProduct?.Source,
            ProductExternalId = selectedProduct?.ExternalId,
            ProductName = selectedProduct?.ProductName ?? productName!,
            GenericName = selectedProduct?.GenericName ?? NormalizeNullableText(model.GenericName, 500),
            BrandName = selectedProduct?.BrandName ?? NormalizeNullableText(model.BrandName, 160),
            Manufacturer = selectedProduct?.Manufacturer ?? NormalizeNullableText(model.Manufacturer, 200),
            DosageForm = selectedProduct?.DosageForm ?? NormalizeNullableText(model.DosageForm, 100),
            Strength = selectedProduct?.Strength ?? NormalizeNullableText(model.Strength, 100),
            PackSize = selectedProduct?.PackSize ?? NormalizeNullableText(model.PackSize, 100),
            Barcode = selectedProduct?.Barcode ?? NormalizeNullableText(model.Barcode, 100),
            Note = NormalizeNullableText(model.Note, 500),
            ReminderDate = model.ReminderDate,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        try
        {
            _dbContext.CustomerReminders.Add(reminder);
            await _dbContext.SaveChangesAsync(cancellationToken);

            TempData["CustomerSuccess"] =
                $"Reminder added for {reminder.ProductName}.";

            return RedirectToAction(nameof(Details), new { id = customer.CustomerId });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create customer reminder for profile {ProfileId}, customer {CustomerId}.",
                profileId,
                customer.CustomerId);

            ModelState.AddModelError(string.Empty, "The reminder could not be saved. No changes were made.");
            model.CustomerName = string.IsNullOrWhiteSpace(customer.FullName)
                ? "Unnamed Customer"
                : customer.FullName.Trim();
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateReminderStatus(
        long reminderId,
        string? status,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var normalizedStatus = NormalizeReminderStatus(status);
        if (normalizedStatus is null)
            return BadRequest();

        var reminder = await _dbContext.CustomerReminders
            .SingleOrDefaultAsync(
                item => item.ReminderId == reminderId && item.ProfileId == profileId,
                cancellationToken);

        if (reminder is null)
            return NotFound();

        reminder.Status = normalizedStatus;
        reminder.CompletedAt = normalizedStatus == "Completed"
            ? DateTime.UtcNow
            : null;
        reminder.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        TempData["CustomerSuccess"] = normalizedStatus switch
        {
            "Ordered" => $"Marked {reminder.ProductName} as ordered.",
            "Completed" => $"Completed reminder for {reminder.ProductName}.",
            "Cancelled" => $"Cancelled reminder for {reminder.ProductName}.",
            _ => $"Reminder reopened for {reminder.ProductName}."
        };

        return RedirectToAction(nameof(Details), new { id = reminder.CustomerId });
    }

    [HttpGet]
    public async Task<IActionResult> Payment(
        long id,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        var customer = await _dbContext.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.CustomerId == id && item.ProfileId == profileId,
                cancellationToken);

        if (customer is null)
            return NotFound();

        var balance = await GetCurrentBalanceAsync(profileId, id, cancellationToken);

        if (balance == 0m)
        {
            TempData["CustomerSuccess"] = "This customer is already settled.";
            return RedirectToAction(nameof(Details), new { id });
        }

        return View(new CustomerPaymentPageViewModel
        {
            CustomerId = id,
            CustomerName = string.IsNullOrWhiteSpace(customer.FullName)
                ? "Unnamed Customer"
                : customer.FullName.Trim(),
            CurrentBalance = balance,
            PaymentType = balance > 0 ? "CustomerPayment" : "PaymentToCustomer"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Payment(
        CustomerPaymentPageViewModel model,
        CancellationToken cancellationToken)
    {
        if (!TryGetProfileId(out var profileId))
            return Unauthorized();

        if (model.Amount <= 0)
            ModelState.AddModelError(nameof(model.Amount), "Enter a payment amount greater than ₹0.");

        var roundedAmount = decimal.Round(
            model.Amount,
            2,
            MidpointRounding.AwayFromZero);

        if (model.Amount > 0m && roundedAmount <= 0m)
            ModelState.AddModelError(
                nameof(model.Amount),
                "Payment amount must be at least ₹0.01.");

        var paymentType = model.PaymentType?.Trim();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var customer = await _dbContext.Customers
                .SingleOrDefaultAsync(
                    item => item.CustomerId == model.CustomerId && item.ProfileId == profileId,
                    cancellationToken);

            if (customer is null)
                return NotFound();

            var balance = await GetCurrentBalanceAsync(profileId, model.CustomerId, cancellationToken);

            if (balance == 0m)
            {
                ModelState.AddModelError(string.Empty, "This customer is already settled.");
            }
            else if (balance > 0 && paymentType != "CustomerPayment")
            {
                ModelState.AddModelError(string.Empty, "This customer owes you money, so record the payment received from the customer.");
            }
            else if (balance < 0 && paymentType != "PaymentToCustomer")
            {
                ModelState.AddModelError(string.Empty, "You owe this customer money, so record the payment you made to the customer.");
            }
            else if (model.Amount > Math.Abs(balance))
            {
                ModelState.AddModelError(
                    nameof(model.Amount),
                    $"The maximum payment for this balance is ₹{Math.Abs(balance):N2}.");
            }

            if (!ModelState.IsValid)
            {
                await transaction.RollbackAsync(cancellationToken);
                return View(new CustomerPaymentPageViewModel
                {
                    CustomerId = customer.CustomerId,
                    CustomerName = string.IsNullOrWhiteSpace(customer.FullName)
                        ? "Unnamed Customer"
                        : customer.FullName.Trim(),
                    CurrentBalance = balance,
                    PaymentType = balance > 0 ? "CustomerPayment" : "PaymentToCustomer",
                    Amount = model.Amount,
                    Note = model.Note
                });
            }

            var balanceChange = paymentType == "CustomerPayment"
                ? -roundedAmount
                : roundedAmount;

            _dbContext.CustomerLedgerEntries.Add(new CustomerLedgerEntry
            {
                ProfileId = profileId,
                CustomerId = customer.CustomerId,
                EntryType = paymentType == "CustomerPayment"
                    ? "CustomerPayment"
                    : "PaymentToCustomer",
                BalanceChange = balanceChange,
                Description = string.IsNullOrWhiteSpace(model.Note)
                    ? paymentType == "CustomerPayment"
                        ? "Payment received from customer"
                        : "Payment made to customer"
                    : model.Note.Trim(),
                CreatedAt = DateTime.UtcNow
            });

            customer.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            TempData["CustomerSuccess"] = "Payment recorded successfully.";
            return RedirectToAction(nameof(Details), new { id = customer.CustomerId });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            _logger.LogError(
                ex,
                "Failed to record customer payment for profile {ProfileId}, customer {CustomerId}.",
                profileId,
                model.CustomerId);

            ModelState.AddModelError(
                string.Empty,
                "The payment could not be recorded. No balance changes were saved.");

            return View(model);
        }
    }

    private async Task<decimal> GetCurrentBalanceAsync(
        long profileId,
        long customerId,
        CancellationToken cancellationToken)
    {
        return await _dbContext.CustomerLedgerEntries
            .AsNoTracking()
            .Where(entry =>
                entry.ProfileId == profileId &&
                entry.CustomerId == customerId)
            .Select(entry => (decimal?)entry.BalanceChange)
            .SumAsync(cancellationToken) ?? 0m;
    }

    private static string? NormalizeNullableText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength].Trim();
    }

    private static string? NormalizeReminderStatus(string? value) =>
        value?.Trim() switch
        {
            "Pending" => "Pending",
            "Ordered" => "Ordered",
            "Completed" => "Completed",
            "Cancelled" => "Cancelled",
            _ => null
        };

    private static string NormalizeFilter(string? filter) =>
        filter?.Trim().ToLowerInvariant() switch
        {
            "owed-to-you" => "owed-to-you",
            "owed-by-you" => "owed-by-you",
            "settled" => "settled",
            _ => "all"
        };

    private static string NormalizeBalanceType(string? value) =>
        value?.Trim() switch
        {
            "OwedByYou" => "OwedByYou",
            _ => "OwedToYou"
        };

    private static bool TryParseMoney(string value, out decimal amount)
    {
        return decimal.TryParse(
            value.Replace(",", string.Empty),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out amount) && amount >= 0;
    }

    private static string? NormalizePhoneNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = new string(value.Where(char.IsDigit).ToArray());

        if (normalized.StartsWith("91", StringComparison.Ordinal) && normalized.Length == 12)
            normalized = normalized[2..];

        if (normalized.Length != 10 || normalized[0] is < '6' or > '9')
            return null;

        return normalized;
    }

    private bool TryGetProfileId(out long profileId) =>
        long.TryParse(HttpContext.Session.GetString("ProfileId"), out profileId);

    private sealed class CustomerBalanceRow
    {
        public long CustomerId { get; init; }
        public decimal Balance { get; init; }
        public DateTime LastActivityAt { get; init; }
    }
}
