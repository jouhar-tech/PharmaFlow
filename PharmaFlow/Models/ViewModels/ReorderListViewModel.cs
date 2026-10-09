using PharmaFlow.Services;

namespace PharmaFlow.Models.ViewModels;

public sealed class ReorderListViewModel
{
    // Used by the single Global Product Catalog search field.
    public string SearchTerm { get; init; } = string.Empty;
    public int Count { get; init; }
    public IReadOnlyList<ReorderListProductViewModel> Items { get; init; } = Array.Empty<ReorderListProductViewModel>();
    public IReadOnlyList<ReorderCatalogProductViewModel> CatalogProducts { get; init; } = Array.Empty<ReorderCatalogProductViewModel>();
}

public sealed class ReorderListProductViewModel
{
    public long ReorderListItemId { get; init; }
    public long? ProductId { get; init; }
    public long? CatalogId { get; init; }
    public string? Source { get; init; }
    public string? ExternalId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string? GenericName { get; init; }
    public string? BrandName { get; init; }
    public string? Barcode { get; init; }
    public decimal CurrentQuantity { get; init; }
    public decimal QuantityWhenAdded { get; init; }
    public DateTime AddedAt { get; init; }
    public bool IsCatalogOnly => !ProductId.HasValue;
}

public sealed class ReorderCatalogProductViewModel
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
    public string SourceLabel => Source switch
    {
        "india-medicine-api" => "Indian Medicine Catalog",
        "openfda-ndc" => "Drug Catalog",
        "openfoodfacts" => "Open Product Catalog",
        _ => Source
    };
    public string TypeLabel => ProductType switch
    {
        "medicine" => "Medicine",
        "cosmetic" => "Cosmetic",
        "pet-product" => "Other Product",
        _ => "Product"
    };

    public static ReorderCatalogProductViewModel FromResult(GlobalProductSearchResult item) => new()
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
        Barcode = item.Barcode
    };
}
