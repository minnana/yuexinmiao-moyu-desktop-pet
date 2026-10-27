namespace YueXinCat.Models;

/// <summary>
/// A persisted balance anchor. The live balance is calculated from this anchor
/// and the elapsed payable schedule, so closing the app does not lose earnings.
/// </summary>
public sealed class SalaryState
{
    public decimal Balance { get; set; }
    public DateTimeOffset BalanceAsOf { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.Now;
    public bool IsPaused { get; set; }
}

public sealed class AppState
{
    public SalarySettings Settings { get; set; } = new();
    public SalaryState Salary { get; set; } = new();
}
