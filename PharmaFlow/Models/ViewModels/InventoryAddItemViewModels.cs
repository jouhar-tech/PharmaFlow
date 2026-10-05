using System.ComponentModel.DataAnnotations;

namespace PharmaFlow.Models.ViewModels;

public sealed class InventoryAddItemViewModel
{
    public long? ExistingProductId { get; set; }

    [Required(ErrorMessage = "Product Name is required.")]
    [StringLength(200, MinimumLength = 2, ErrorMessage = "Product Name must be between 2 and 200 characters.")]
    [Display(Name = "Product Name")]
    public string ProductName { get; set; } = string.Empty;

    [Range(0.01, 999999999, ErrorMessage = "Quantity must be greater than 0.")]
    [Display(Name = "Quantity")]
    public decimal Quantity { get; set; }

    [Range(0.01, 999999999, ErrorMessage = "MRP must be greater than 0.")]
    [Display(Name = "MRP")]
    public decimal Mrp { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Expiry")]
    public DateOnly ExpiryDate { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(2);
}

public sealed class InventoryProductSearchItemViewModel
{
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? Barcode { get; init; }
    public decimal? LatestMrp { get; init; }
}

public sealed class InventoryProductSearchViewModel
{
    public string Query { get; init; } = string.Empty;
    public IReadOnlyList<InventoryProductSearchItemViewModel> Products { get; init; } = [];
}
