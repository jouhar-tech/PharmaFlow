namespace PharmaFlow.Models.ViewModels;

public sealed class ReportsViewModel
{
    public string SelectedPeriod { get; set; } = "7 days";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal TotalSalesAmount { get; set; }
    public decimal Profit { get; set; }
    public int BillCount { get; set; }
    public decimal TotalMrp { get; set; }
    public decimal CashPercentage { get; set; }
    public decimal UpiPercentage { get; set; }
    public IReadOnlyList<TopSellerViewModel> TopSellers { get; set; } = [];
    public IReadOnlyList<SalesReportRowViewModel> SalesRows { get; set; } = [];
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

public sealed class SalesReportRowViewModel
{
    public string BillNumber { get; set; } = string.Empty;
    public DateTimeOffset DateTime { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string PaymentMode { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal NetProfit { get; set; }
}
