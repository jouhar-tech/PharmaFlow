namespace PharmaFlow.Models.ViewModels
{
    public sealed class DashboardViewModel
    {
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
