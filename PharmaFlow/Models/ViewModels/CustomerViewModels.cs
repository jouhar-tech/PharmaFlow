using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public sealed class CustomerManagementViewModel
{
    public string SearchTerm { get; init; } = string.Empty;
    public string Filter { get; init; } = "all";
    public decimal TotalOwedToYou { get; init; }
    public decimal TotalOwedByYou { get; init; }
    public IReadOnlyList<CustomerListItemViewModel> Customers { get; init; } = [];
}

public sealed class CustomerListItemViewModel
{
    public long CustomerId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public decimal CurrentBalance { get; init; }
    public DateTime LastActivityAt { get; init; }
    public int BillCount { get; init; }
    public int PendingReminderCount { get; init; }

    public bool IsOwedToYou => CurrentBalance > 0;
    public bool IsOwedByYou => CurrentBalance < 0;
}

public sealed class CustomerCreateViewModel
{
    [Required(ErrorMessage = "Enter the customer's name.")]
    [StringLength(120, ErrorMessage = "Customer name cannot exceed 120 characters.")]
    [Display(Name = "Customer Name")]
    public string FullName { get; set; } = string.Empty;

    [StringLength(20, ErrorMessage = "Phone number cannot exceed 20 characters.")]
    [Display(Name = "Phone Number")]
    public string? PhoneNumber { get; set; }

    [Range(0, 1000000000, ErrorMessage = "Opening balance must be between ₹0 and ₹1,000,000,000.")]
    [Display(Name = "Opening Balance")]
    public decimal OpeningBalance { get; set; }

    [Display(Name = "Balance Direction")]
    public string BalanceType { get; set; } = "OwedToYou";

    [StringLength(500, ErrorMessage = "Note cannot exceed 500 characters.")]
    public string? Note { get; set; }
}

public sealed class CustomerDetailsViewModel
{
    public long CustomerId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string? PhoneNumber { get; init; }
    public DateTime CreatedAt { get; init; }
    public decimal CurrentBalance { get; init; }
    public int BillCount { get; init; }
    public IReadOnlyList<CustomerTransactionViewModel> Transactions { get; init; } = [];
    public IReadOnlyList<CustomerReminderViewModel> Reminders { get; init; } = [];
}

public sealed class CustomerReminderViewModel
{
    public long ReminderId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? ProductSource { get; init; }
    public string Status { get; init; } = "Pending";
    public string? Note { get; init; }
    public DateOnly? ReminderDate { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }

    public bool IsDue =>
        Status is "Pending" or "Ordered" &&
        ReminderDate.HasValue &&
        ReminderDate.Value <= DateOnly.FromDateTime(DateTime.Today);
}

public sealed class CustomerReminderCreateViewModel
{
    public long CustomerId { get; init; }
    public string CustomerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Select a product or enter a product name.")]
    [StringLength(200, ErrorMessage = "Product name cannot exceed 200 characters.")]
    [Display(Name = "Product / Medicine")]
    public string ProductName { get; set; } = string.Empty;

    public long? CatalogId { get; set; }
    public string? ProductSource { get; set; }
    public string? ProductExternalId { get; set; }
    public string? GenericName { get; set; }
    public string? BrandName { get; set; }
    public string? Manufacturer { get; set; }
    public string? DosageForm { get; set; }
    public string? Strength { get; set; }
    public string? PackSize { get; set; }
    public string? Barcode { get; set; }

    [StringLength(500, ErrorMessage = "Note cannot exceed 500 characters.")]
    public string? Note { get; set; }

    [Display(Name = "Reminder Date")]
    public DateOnly? ReminderDate { get; set; }
}

public sealed class CustomerReminderSearchItemViewModel
{
    public long? CatalogId { get; init; }
    public string Source { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public string ProductType { get; init; } = string.Empty;
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? Manufacturer { get; init; }
    public string? DosageForm { get; init; }
    public string? Strength { get; init; }
    public string? PackSize { get; init; }
    public string? Barcode { get; init; }
    public bool IsAlreadyInInventory { get; init; }
}

public sealed class CustomerTransactionViewModel
{
    public long LedgerEntryId { get; init; }
    public string EntryType { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal BalanceChange { get; init; }
    public decimal BalanceAfter { get; init; }
    public long? BillId { get; init; }
    public string? BillNumber { get; init; }
    public DateTime CreatedAt { get; init; }

    public string EntryLabel => EntryType switch
    {
        "UdhaarSale" => "Udhaar Sale",
        "CustomerPayment" => "Payment Received",
        "PaymentToCustomer" => "Payment to Customer",
        "OpeningBalance" => "Opening Balance",
        "Adjustment" => "Balance Adjustment",
        _ => "Balance Entry"
    };
}

public sealed class CustomerPaymentPageViewModel
{
    public long CustomerId { get; init; }
    public string CustomerName { get; init; } = string.Empty;
    public decimal CurrentBalance { get; init; }
    public string PaymentType { get; set; } = "CustomerPayment";
    public decimal Amount { get; set; }

    [StringLength(500, ErrorMessage = "Note cannot exceed 500 characters.")]
    public string? Note { get; set; }

    public bool CustomerOwesYou => CurrentBalance > 0;
    public bool YouOweCustomer => CurrentBalance < 0;
}
