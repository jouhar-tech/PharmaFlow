namespace PharmaFlow.Models.ViewModels
{
    public sealed class DashboardViewModel
    {
        public int ProductsExpiringSoonCount { get; init; }

        public int LowStockCount { get; init; }

        public decimal ExpiryAtRiskValue { get; init; }

        public decimal SlowMovingStockValue { get; init; }

        public decimal EstimatedMoneySaved { get; init; }

        public decimal ExpiryLossPrevented { get; init; }

        public decimal CashRecovered { get; init; }

        public decimal InventoryLossReduced { get; init; }

        public bool HasPharmaFlowValue =>
            EstimatedMoneySaved > 0m ||
            ExpiryLossPrevented > 0m ||
            CashRecovered > 0m ||
            InventoryLossReduced > 0m;
    }
}
