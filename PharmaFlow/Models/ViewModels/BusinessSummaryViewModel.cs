namespace PharmaFlow.Models.ViewModels;

public sealed class BusinessSummaryViewModel
{
    public decimal TodaySales { get; init; }
    public decimal MonthSales { get; init; }
    public decimal MonthProfit { get; init; }
    public decimal StockValue { get; init; }
    public decimal CustomerOutstanding { get; init; }
    public int CustomersWithOutstanding { get; init; }
}
