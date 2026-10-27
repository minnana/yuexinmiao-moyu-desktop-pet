namespace YueXinCat.Models;

public sealed record SalarySnapshot(
    DateTimeOffset At,
    decimal Balance,
    decimal Increment,
    decimal SecondPay,
    bool IsPaidTime,
    bool IsOvertime,
    string Status,
    string? CurrentKey = null);
