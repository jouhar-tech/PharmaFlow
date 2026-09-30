namespace PharmaFlow.Models.ViewModels;

public sealed class ReportsViewModel
{
    public string SelectedPeriod { get; set; } = "30 days";
    public decimal TotalSalesAmount { get; set; }
    public decimal Profit { get; set; }
    public int BillCount { get; set; }
    public decimal TotalMrp { get; set; }
    public decimal CashPercentage { get; set; }
    public decimal UpiPercentage { get; set; }
    public IReadOnlyList<TopSellerViewModel> TopSellers { get; set; } = [];
}

public sealed class TopSellerViewModel
{
    public string ProductName { get; set; } = string.Empty;
    public decimal QuantitySold { get; set; }
    public decimal Revenue { get; set; }
    public decimal Mrp { get; set; }
    public decimal Cost { get; set; }
    public decimal Profit { get; set; }
}
